"""
Firmware MicroPython para Juguete Cognitivo de 5 Botones (`juguete`) — Hito M9.

Responsabilidades:
- RF14 / RF34 / RF35: Secuencia interactiva de 5 LEDs + 5 pulsadores + buzzer pasivo.
  Respuesta audiovisual en `< 200 ms` (RF34), reinicio automático del patrón al fallar un botón (RF35),
  y envío de `event: game_end` con `hits` y `secs` al concluir la partida (RF14).
- RF36: Monitoreo de batería (3 pilas AA por divisor ADC) y disparo de `event: battery_low` cuando `< 20 %`.
"""

from typing import Any

from firmware.common.hal import (
    BatteryAdcAdapter,
    DigitalPinAdapter,
    PwmOutputAdapter,
    WatchdogAdapter,
    ticks_diff,
    ticks_ms,
)
from firmware.common.pins import JUGUETE_PINS
from firmware.common.protocol_and_buffer import OfflineCircularBuffer48h

BUTTON_NOTES_HZ = [262, 330, 392, 523, 659]  # Do4, Mi4, Sol4, Do5, Mi5
LOW_BATTERY_THRESHOLD_PCT = 20


class JugueteFirmwareNode:
    """Controlador principal del nodo `juguete` en Raspberry Pi Pico W."""

    def __init__(self, serial: str = "juguete-pico-01") -> None:
        self.serial = serial
        self.device_type = "juguete"
        self.buffer = OfflineCircularBuffer48h()
        self.wdt = WatchdogAdapter(timeout_ms=JUGUETE_PINS["wdt_timeout_ms"])

        self.buttons = [
            DigitalPinAdapter(pin, is_output=False, pull_up=True)
            for pin in JUGUETE_PINS["button_pins"]
        ]
        self.leds = [
            DigitalPinAdapter(pin, is_output=True)
            for pin in JUGUETE_PINS["led_pins"]
        ]
        self.buzzer = PwmOutputAdapter(
            gpio_pin=JUGUETE_PINS["buzzer_pwm_pin"],
            freq_hz=440,
            initial_duty_u16=0,
        )
        self.battery_adc = BatteryAdcAdapter(adc_pin=JUGUETE_PINS["battery_adc_pin"])

        # Patrón cognitivo determinista de 5 botones (0..4)
        self.target_sequence: list[int] = [0, 2, 1, 4, 3]
        self.current_step = 0
        self.hits = 0
        self.resets_count = 0
        self.last_response_latency_ms = 0
        self._low_battery_alerted = False

    def press_button(self, button_index: int) -> tuple[bool, int]:
        """
        Procesa la pulsación de uno de los 5 botones (0..4) con respuesta audiovisual inmediata (< 200 ms, RF34).
        Si coincide con el paso esperado de la secuencia, incrementa `hits`.
        Si falla, emite tono de error y reinicia la secuencia al paso 0 (RF35).
        Devuelve `(fue_acierto, latencia_ms)`.
        """
        start_ms = ticks_ms()
        self.wdt.feed()

        if button_index < 0 or button_index >= len(self.leds):
            raise ValueError("Índice de botón fuera de rango (0..4).")

        expected_btn = self.target_sequence[self.current_step % len(self.target_sequence)]
        for idx, led in enumerate(self.leds):
            led.write(1 if idx == button_index else 0)

        if button_index == expected_btn:
            # Retroalimentación inmediata de acierto
            self.buzzer.set_freq(BUTTON_NOTES_HZ[button_index])
            self.buzzer.set_duty_u16(16384)
            self.hits += 1
            self.current_step += 1
            is_hit = True
        else:
            # Tono grave y reinicio inmediato del patrón tras error (RF35)
            self.buzzer.set_freq(150)
            self.buzzer.set_duty_u16(16384)
            self.current_step = 0
            self.resets_count += 1
            is_hit = False

        elapsed_ms = max(1, ticks_diff(ticks_ms(), start_ms))
        self.last_response_latency_ms = elapsed_ms
        return is_hit, elapsed_ms

    def finish_game_session(self, duration_secs: int, ts: int) -> dict[str, Any]:
        """Emite `event: game_end` con `hits`, `secs` y `battery_pct` al finalizar la partida (RF14)."""
        self.wdt.feed()
        for led in self.leds:
            led.write(0)
        self.buzzer.set_duty_u16(0)

        completed_hits = self.hits
        self.hits = 0
        self.current_step = 0

        return self.buffer.enqueue_envelope(
            "event",
            {
                "name": "game_end",
                "hits": completed_hits,
                "secs": max(1, int(duration_secs)),
                "battery_pct": self.battery_adc.read_battery_pct(),
            },
            ts=ts,
        )

    def check_battery(self, ts: int) -> dict[str, Any] | None:
        """Verifica nivel de batería y emite `event: battery_low` si cae por debajo del 20 % (RF36)."""
        self.wdt.feed()
        pct = self.battery_adc.read_battery_pct()
        if pct < LOW_BATTERY_THRESHOLD_PCT and not self._low_battery_alerted:
            self._low_battery_alerted = True
            return self.buffer.enqueue_envelope(
                "event",
                {"name": "battery_low", "battery_pct": pct},
                ts=ts,
            )
        if pct >= LOW_BATTERY_THRESHOLD_PCT:
            self._low_battery_alerted = False
        return None
