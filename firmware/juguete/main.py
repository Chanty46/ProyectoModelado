"""
Juguete Cognitivo de 5 Botones (Pico W #5) — MicroPython.

RF34  Mecánica de juego: un LED iluminado a la vez. Cuando el infante pulsa el botón
      iluminado, reproduce tono de acierto, apaga ese LED, ilumina uno nuevo al azar
      en < 200 ms e incrementa contador de aciertos.
RF35  Reinicio por fallo: si pulsa un botón cuyo LED está apagado, reproduce tono
      de error, finaliza la secuencia transmitiendo `game_end` (RF14) con `hits` y `secs`,
      reinicia aciertos a 0 e ilumina un nuevo botón en < 1.0 s.
RF36  Batería baja: aviso por TCP con `battery_low` cuando el nivel desciende por debajo de 10 %.
Telemetría por minuto: `battery_pct`.
"""

from common.device_runtime import DeviceNode, run
from common.hal import (
    IS_MICROPYTHON,
    BatteryAdcAdapter,
    DigitalPinAdapter,
    PwmOutputAdapter,
    ticks_diff,
    ticks_ms,
)
from common.pins import JUGUETE_PINS

LOW_BATTERY_THRESHOLD = 10  # < 10 % según RF36
NOTES_HIT = [262, 330, 392, 523, 659]  # Do4, Mi4, Sol4, Do5, Mi5
NOTE_ERROR = 150  # Tono grave distintivo de error


class JugueteNode(DeviceNode):
    device_type = "juguete"
    tick_interval_ms = 25  # Alta reactividad para pulsadores (< 200 ms RF34)

    def __init__(self, buffer=None, clock=None):
        DeviceNode.__init__(self, buffer, clock)
        p = JUGUETE_PINS
        self.buttons = [DigitalPinAdapter(pin, is_output=False, pull_up=True) for pin in p["BUTTON_PINS"]]
        self.leds = [DigitalPinAdapter(pin, is_output=True) for pin in p["LED_PINS"]]
        self.buzzer = PwmOutputAdapter(p["BUZZER_PWM"], 440)
        self.battery_adc = BatteryAdcAdapter(p["BATTERY_ADC"])

        self.current_target = 0
        self.hits = 0
        self.game_active = True
        self.resets_count = 0
        self._last_btn_states = [1] * len(self.buttons)  # Pull-up: 1 = suelto, 0 = presionado
        self._session_start_ms = ticks_ms()
        self._sound_until_ms = 0
        self._low_battery_alerted = False
        self._minute_start_ms = None

        self._activate_target(0)

    def heartbeat_data(self):
        data = DeviceNode.heartbeat_data(self)
        data["battery_pct"] = self.battery_adc.read_battery_pct()
        return data

    def _activate_target(self, idx):
        self.current_target = idx % len(self.leds)
        for i, led in enumerate(self.leds):
            led.write(1 if i == self.current_target else 0)

    def _next_random_target(self):
        # Generador determinista simple sin depender de módulo urandom
        curr = self.current_target
        # Selecciona un botón distinto
        return (curr + 1 + (ticks_ms() % (len(self.leds) - 1))) % len(self.leds)

    def _play_tone(self, freq, duration_ms, now_ms):
        self.buzzer.set_freq(freq)
        self.buzzer.set_duty_u16(16384)
        self._sound_until_ms = now_ms + duration_ms

    def _handle_press(self, btn_idx, now_ms):
        """
        Procesa pulsación física de un botón.
        Devuelve (is_hit, latency_ms).
        """
        t0 = ticks_ms()
        if btn_idx == self.current_target:
            # Acierto (RF34)
            self.hits += 1
            self._play_tone(NOTES_HIT[btn_idx], 120, now_ms)
            nxt = self._next_random_target()
            self._activate_target(nxt)
            latency = ticks_diff(ticks_ms(), t0)
            return True, max(1, latency)
        else:
            # Fallo (RF35): sonido de error, reportar fin de secuencia (RF14) y reiniciar
            self._play_tone(NOTE_ERROR, 250, now_ms)
            elapsed_s = max(1, ticks_diff(now_ms, self._session_start_ms) // 1000)
            self.emit_event("game_end", hits=self.hits, secs=elapsed_s)
            self.hits = 0
            self.resets_count += 1
            self._session_start_ms = now_ms
            nxt = self._next_random_target()
            self._activate_target(nxt)
            latency = ticks_diff(ticks_ms(), t0)
            return False, max(1, latency)

    def tick(self, now_ms=None):
        now_ms = ticks_ms() if now_ms is None else now_ms
        if self._minute_start_ms is None:
            self._minute_start_ms = now_ms

        # Apagar buzzer si terminó el tono
        if self._sound_until_ms > 0 and ticks_diff(now_ms, self._sound_until_ms) >= 0:
            self.buzzer.set_duty_u16(0)
            self._sound_until_ms = 0

        # Sondeo de pulsadores con detección de flanco descendente (0 = pulsado con PULL_UP)
        for i, btn in enumerate(self.buttons):
            v = btn.read()
            if v == 0 and self._last_btn_states[i] == 1:
                self._handle_press(i, now_ms)
            self._last_btn_states[i] = v

        # Verificación de batería baja < 10 % (RF36)
        pct = self.battery_adc.read_battery_pct()
        if pct < LOW_BATTERY_THRESHOLD and not self._low_battery_alerted:
            self._low_battery_alerted = True
            self.emit_event("battery_low", battery_pct=pct)
        elif pct >= LOW_BATTERY_THRESHOLD:
            self._low_battery_alerted = False

        # Telemetría cada 60 s
        if ticks_diff(now_ms, self._minute_start_ms) >= 60000:
            self._minute_start_ms = now_ms
            self.emit_telemetry({"battery_pct": pct})


if IS_MICROPYTHON:
    import secrets  # type: ignore  # noqa: E402

    run(JugueteNode(), secrets)
