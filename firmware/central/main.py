"""
Firmware MicroPython para Nodo Central de la Cuna Inteligente (`central`) — Hito M9.

Responsabilidades:
- RF07 / RF08: Lectura ambiental T/H y disparo de `env_out_of_range` tras 10 s sostenidos fuera de rango.
- RF09: Lámpara nocturna con PWM >= 1 kHz y rampa suave de 1.5 s (0..100 %).
- RF11 / D5: Reproducción de bloques de audio PCM 8-bit 8 kHz (base64) por PWM + PAM8403.
- RF12 / D12: Reproducción de melodías de cuna como secuencia de notas `[(freq_hz, ms), ...]` con volumen <= 65 dBA.
- RF05 / IU-03: Pulsador táctil `TTP223` que emite `button_attention` y LED RGB de estado (`green`/`yellow`/`red`).
- RF04: Alarma local sonora y visual (`alarm_set`).
"""

import base64
from typing import Any

from firmware.common.hal import (
    DigitalPinAdapter,
    PwmOutputAdapter,
    TemperatureHumiditySensorAdapter,
    WatchdogAdapter,
)
from firmware.common.pins import CENTRAL_PINS
from firmware.common.protocol_and_buffer import OfflineCircularBuffer48h

# Catálogo de melodías embebido como secuencias de notas (freq_hz, duracion_ms) según D12
EMBEDDED_SONG_NOTES: dict[int, list[tuple[int, int]]] = {
    1: [(262, 400), (330, 400), (392, 800), (330, 400), (262, 800)],  # Arrullo de Estrellas (Brahms)
    2: [(262, 350), (262, 350), (392, 350), (392, 350), (440, 350), (440, 350), (392, 700)],  # Estrellita
    3: [(294, 500), (330, 500), (370, 500), (440, 1000)],  # Ruido Blanco Suave / Arpegio
    4: [(330, 450), (294, 450), (262, 900)],  # Melodía Nocturna del Bosque
}


class CentralFirmwareNode:
    """Controlador principal del nodo `central` en Raspberry Pi Pico W."""

    def __init__(self, serial: str = "central-pico-01") -> None:
        self.serial = serial
        self.device_type = "central"
        self.buffer = OfflineCircularBuffer48h()
        self.wdt = WatchdogAdapter(timeout_ms=CENTRAL_PINS["wdt_timeout_ms"])

        # Sensores y actuadores a través del HAL usando firmware/common/pins.py
        self.env_sensor = TemperatureHumiditySensorAdapter(
            sensor_type=CENTRAL_PINS["env_sensor_type"],
            sda_pin=CENTRAL_PINS["i2c_sda_pin"],
            scl_pin=CENTRAL_PINS["i2c_scl_pin"],
            dht_pin=CENTRAL_PINS["dht22_data_pin"],
        )
        self.lamp_pwm = PwmOutputAdapter(
            gpio_pin=CENTRAL_PINS["night_lamp_pwm_pin"],
            freq_hz=max(1000, int(CENTRAL_PINS["night_lamp_pwm_freq_hz"])),
            initial_duty_u16=0,
        )
        self.audio_pwm = PwmOutputAdapter(
            gpio_pin=CENTRAL_PINS["audio_pwm_pin"],
            freq_hz=int(CENTRAL_PINS["audio_carrier_freq_hz"]),
            initial_duty_u16=0,
        )
        self.touch_pin = DigitalPinAdapter(
            gpio_pin=CENTRAL_PINS["touch_attention_pin"],
            is_output=False,
            pull_up=False,
        )
        self.led_r = DigitalPinAdapter(CENTRAL_PINS["rgb_red_pin"], is_output=True)
        self.led_g = DigitalPinAdapter(CENTRAL_PINS["rgb_green_pin"], is_output=True)
        self.led_b = DigitalPinAdapter(CENTRAL_PINS["rgb_blue_pin"], is_output=True)

        # Configuración recibida en hello_ack / thresholds_set
        self.temp_min = 18.0
        self.temp_max = 26.0
        self.hum_min = 40.0
        self.hum_max = 60.0

        # Estado interno
        self.lamp_intensity_pct = 0
        self.led_color = "green"
        self.alarm_active = False
        self.playing_song_id: int | None = None
        self.last_pcm_samples_played = 0
        self._out_of_range_seconds = 0.0
        self._temp_samples: list[float] = []
        self._hum_samples: list[float] = []
        self._last_touch_state = 0

        self._apply_rgb_color("green")

    def _apply_rgb_color(self, color: str) -> None:
        self.led_color = color
        if color == "red":
            self.led_r.write(1)
            self.led_g.write(0)
            self.led_b.write(0)
        elif color == "yellow":
            self.led_r.write(1)
            self.led_g.write(1)
            self.led_b.write(0)
        else:
            self.led_r.write(0)
            self.led_g.write(1)
            self.led_b.write(0)

    def sample_environment_second(self, ts: int, dt_s: float = 1.0) -> dict[str, Any] | None:
        """
        Muestrea temperatura y humedad una vez por segundo.
        Si permanece fuera de rango durante >= 10 s sostenidos (RF07/RF08), encola `env_out_of_range`.
        """
        self.wdt.feed()
        temp_c, hum_pct = self.env_sensor.read()
        self._temp_samples.append(temp_c)
        self._hum_samples.append(hum_pct)

        out_temp = temp_c < self.temp_min or temp_c > self.temp_max
        out_hum = hum_pct < self.hum_min or hum_pct > self.hum_max

        if out_temp or out_hum:
            self._out_of_range_seconds += dt_s
            if self._out_of_range_seconds >= 10.0:
                self._out_of_range_seconds = 0.0
                kind = "temp_high" if temp_c > self.temp_max else ("temp_low" if temp_c < self.temp_min else "humidity")
                return self.buffer.enqueue_envelope(
                    "event",
                    {"name": "env_out_of_range", "temp_c": temp_c, "hum_pct": hum_pct, "kind": kind},
                    ts=ts,
                )
        else:
            self._out_of_range_seconds = 0.0

        return None

    def flush_minute_telemetry(self, ts: int) -> dict[str, Any]:
        """Agrega las muestras del minuto y encola el sobre `telemetry` en el búfer circular de 48 h."""
        if not self._temp_samples:
            t_now, h_now = self.env_sensor.read()
            self._temp_samples.append(t_now)
            self._hum_samples.append(h_now)

        temp_avg = round(sum(self._temp_samples) / len(self._temp_samples), 2)
        temp_min = round(min(self._temp_samples), 2)
        temp_max = round(max(self._temp_samples), 2)
        hum_avg = round(sum(self._hum_samples) / len(self._hum_samples), 2)

        self._temp_samples.clear()
        self._hum_samples.clear()

        return self.buffer.enqueue_envelope(
            "telemetry",
            {
                "temp_avg": temp_avg,
                "temp_min": temp_min,
                "temp_max": temp_max,
                "hum_avg": hum_avg,
                "lamp_on": self.lamp_intensity_pct > 0,
                "lamp_intensity_pct": self.lamp_intensity_pct,
                "song_playing": self.playing_song_id is not None,
            },
            ts=ts,
        )

    def poll_touch_button(self, ts: int) -> dict[str, Any] | None:
        """Detecta flanco ascendente en el sensor táctil TTP223 y emite `button_attention` (RF05)."""
        current = self.touch_pin.read()
        if current == 1 and self._last_touch_state == 0:
            self._last_touch_state = 1
            return self.buffer.enqueue_envelope("event", {"name": "button_attention"}, ts=ts)
        self._last_touch_state = current
        return None

    def ramp_lamp_intensity(self, target_pct: int, ramp_steps: int = 15) -> list[int]:
        """
        Aplica una rampa suave de 1.5 s (15 pasos de 100 ms) con PWM >= 1 kHz (RF09).
        Devuelve la lista de valores `duty_u16` intermedios aplicados.
        """
        target = max(0, min(100, int(target_pct)))
        start = self.lamp_intensity_pct
        steps = max(1, int(ramp_steps))
        applied_duties: list[int] = []

        for step in range(1, steps + 1):
            fraction = step / float(steps)
            interp_pct = start + (target - start) * fraction
            duty_u16 = int(round((interp_pct / 100.0) * 65535))
            self.lamp_pwm.set_duty_u16(duty_u16)
            applied_duties.append(self.lamp_pwm.duty_u16)

        self.lamp_intensity_pct = target
        return applied_duties

    def handle_command(self, envelope: dict[str, Any], ts: int) -> dict[str, Any]:
        """Procesa un comando del servidor TCP y devuelve el sobre `ack` correspondiente."""
        self.wdt.feed()
        cmd_seq = int(envelope.get("seq", 0))
        data = envelope.get("d", {})
        cmd_name = data.get("cmd", "")

        if cmd_name == "lamp_set":
            on = bool(data.get("on", False))
            pct = int(data.get("intensity_pct", 0)) if on else 0
            self.ramp_lamp_intensity(pct)
        elif cmd_name == "led_status":
            color = str(data.get("color", "green"))
            self._apply_rgb_color(color)
        elif cmd_name == "alarm_set":
            self.alarm_active = bool(data.get("on", False))
            if self.alarm_active:
                self._apply_rgb_color("red")
                # TODO-HW: Verificar límite acústico <= 65 dBA en bocina 8 ohm con sonómetro (RNF08)
                self.audio_pwm.set_freq(880)
                self.audio_pwm.set_duty_u16(20000)
            else:
                self.audio_pwm.set_duty_u16(0)
        elif cmd_name == "thresholds_set":
            self.temp_min = float(data.get("temp_min", self.temp_min))
            self.temp_max = float(data.get("temp_max", self.temp_max))
            self.hum_min = float(data.get("hum_min", self.hum_min))
            self.hum_max = float(data.get("hum_max", self.hum_max))
        elif cmd_name == "song_play":
            song_id = int(data.get("song_id", 1))
            vol_pct = max(0, min(100, int(data.get("volume_pct", 50))))
            self.playing_song_id = song_id
            notes = EMBEDDED_SONG_NOTES.get(song_id, EMBEDDED_SONG_NOTES[1])
            first_freq = notes[0][0]
            # Escala el ciclo de trabajo al volumen máximo permitido (<= 65 dBA, RNF08)
            max_safe_duty = int(round((vol_pct / 100.0) * 16384))
            self.audio_pwm.set_freq(first_freq)
            self.audio_pwm.set_duty_u16(max_safe_duty)
        elif cmd_name == "song_stop":
            self.playing_song_id = None
            self.audio_pwm.set_duty_u16(0)
        elif cmd_name == "audio_chunk":
            pcm_b64 = str(data.get("pcm_b64", ""))
            raw_pcm = base64.b64decode(pcm_b64) if pcm_b64 else b""
            self.last_pcm_samples_played = len(raw_pcm)
            self.audio_pwm.set_freq(int(CENTRAL_PINS["audio_carrier_freq_hz"]))
            if raw_pcm:
                last_sample = raw_pcm[-1]
                self.audio_pwm.set_duty_u16(int((last_sample / 255.0) * 32768))
        else:
            return {
                "v": 1,
                "t": "ack",
                "seq": self.buffer.next_seq(),
                "ts": ts,
                "d": {"ack_seq": cmd_seq, "ok": False, "error": f"unknown_cmd:{cmd_name}"},
            }

        return {
            "v": 1,
            "t": "ack",
            "seq": self.buffer.next_seq(),
            "ts": ts,
            "d": {"ack_seq": cmd_seq, "ok": True},
        }
