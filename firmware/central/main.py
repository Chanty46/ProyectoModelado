"""
Unidad Central (Pico W #1) — MicroPython.

RF07/RF08  T/H fuera de rango >= 10 s sostenidos -> event `env_out_of_range` (una vez por episodio).
RF09       Luz nocturna PWM >= 1 kHz con rampa no bloqueante (por defecto 1.5 s).
RF05       Botón táctil TTP223 -> event `button_attention`.
IU-03      LED RGB según `status_led` / `mode_set` (verde monitoreo, amarillo atención, rojo alarma).
RF04       `alarm_set` -> tono de alarma local (prioridad sobre todo lo demás).
RF12/D12   `song_play` -> secuencia de notas [{freq_hz, ms}] enviada por el servidor.
RF11/D5    `audio_chunk` -> PCM u8 reproducido por PWM con un temporizador a `rate` Hz.
Telemetría por minuto: `temp_c`, `hum_pct` (promedios).

En la Pico: copiar `common/`, este archivo como `main.py` y `secrets.py` a la raíz.
"""

from common.device_runtime import DeviceNode, run
from common.hal import (
    IS_MICROPYTHON,
    DigitalPinAdapter,
    PwmOutputAdapter,
    TemperatureHumiditySensorAdapter,
    b64decode,
    ticks_diff,
    ticks_ms,
)
from common.pins import CENTRAL_PINS

ENV_SUSTAIN_MS = 10000
TOUCH_DEBOUNCE_MS = 300
# TODO-HW: ajustar con sonómetro + potenciómetro del PAM8403 para no superar 65 dBA (RNF08)
MAX_TONE_DUTY = 16384
ALARM_TONES = ((880, 400), (660, 400))
MODE_COLORS = {"monitoreo": "green", "atencion": "yellow", "alarma": "red"}
RGB_MAP = {
    "green": (0, 1, 0),
    "yellow": (1, 1, 0),
    "red": (1, 0, 0),
    "blue": (0, 0, 1),
    "off": (0, 0, 0),
}
MAX_AUDIO_QUEUE_BYTES = 8 * 1280  # ~1.3 s a 8 kHz; lo demás se descarta para no agotar la RAM


class IntercomAudioPlayer:
    """
    Reproduce PCM u8 escribiendo cada muestra en el duty del PWM desde un temporizador.
    TODO-HW: verificar en la Pico que el callback a 8 kHz no pierda muestras.
    """

    def __init__(self, pwm, carrier_hz):
        self.pwm = pwm
        self.carrier_hz = carrier_hz
        self._queue = bytearray()
        self._pos = 0
        self._timer = None
        self.samples_received = 0

    @property
    def active(self):
        return self._pos < len(self._queue)

    def enqueue(self, pcm, rate_hz):
        if len(self._queue) - self._pos + len(pcm) > MAX_AUDIO_QUEUE_BYTES:
            return False
        if self._pos and self._pos >= len(self._queue):
            self._queue = bytearray()
            self._pos = 0
        self._queue.extend(pcm)
        self.samples_received += len(pcm)
        if IS_MICROPYTHON and self._timer is None:
            import machine  # type: ignore

            self.pwm.set_freq(self.carrier_hz)
            self._timer = machine.Timer(freq=int(rate_hz), mode=machine.Timer.PERIODIC, callback=self._on_sample)
        return True

    def _on_sample(self, _timer):
        if self._pos < len(self._queue):
            self.pwm.set_duty_u16(self._queue[self._pos] << 8)
            self._pos += 1
        else:
            self.stop()

    def stop(self):
        if self._timer is not None:
            self._timer.deinit()
            self._timer = None
        self._queue = bytearray()
        self._pos = 0
        self.pwm.set_duty_u16(0)


class CentralNode(DeviceNode):
    device_type = "central"
    tick_interval_ms = 50

    def __init__(self, buffer=None, clock=None):
        DeviceNode.__init__(self, buffer, clock)
        p = CENTRAL_PINS
        self.env_sensor = TemperatureHumiditySensorAdapter(
            p["ENV_SENSOR"], p["I2C_ID"], p["I2C_SDA"], p["I2C_SCL"], p["DHT_FALLBACK"]
        )
        self.lamp_pwm = PwmOutputAdapter(p["LAMP_PWM"], max(1000, p["LAMP_PWM_HZ"]))
        self.audio_pwm = PwmOutputAdapter(p["AUDIO_PWM"], p["AUDIO_CARRIER_HZ"])
        self.touch_pin = DigitalPinAdapter(p["TOUCH_TTP223"], is_output=False)
        self.rgb = (
            DigitalPinAdapter(p["RGB_RED"]),
            DigitalPinAdapter(p["RGB_GREEN"]),
            DigitalPinAdapter(p["RGB_BLUE"]),
        )
        self.intercom = IntercomAudioPlayer(self.audio_pwm, p["AUDIO_CARRIER_HZ"])

        self.temp_min, self.temp_max = 18.0, 26.0
        self.hum_min, self.hum_max = 40.0, 60.0
        self.mode = "monitoreo"
        self.led_color = "green"
        self.alarm_active = False

        # Rampa de la luz
        self.lamp_level = 0.0
        self._ramp_from = 0.0
        self._ramp_to = 0.0
        self._ramp_start_ms = 0
        self._ramp_ms = 0

        # Canción
        self.song_notes = []
        self.song_code = None
        self._song_idx = 0
        self._note_start_ms = 0
        self._song_end_ms = None

        self._alarm_idx = 0
        self._alarm_tone_ms = 0

        self._out_of_range_since = None
        self._env_alerted = False
        self._last_env_ms = None
        self._minute_start_ms = None
        self._temps = []
        self._hums = []
        self._last_touch = 0
        self._last_touch_ms = None

        self.set_led("green")

    # ---------- actuadores ----------
    def set_led(self, color):
        bits = RGB_MAP.get(color)
        if bits is None:
            return False
        self.led_color = color
        for pin, bit in zip(self.rgb, bits):
            pin.write(bit)
        return True

    def start_lamp_ramp(self, target_level, ramp_ms, now_ms):
        self._ramp_from = self.lamp_level
        self._ramp_to = float(max(0, min(100, target_level)))
        self._ramp_start_ms = now_ms
        self._ramp_ms = max(0, int(ramp_ms))
        if self._ramp_ms == 0:
            self._apply_lamp(self._ramp_to)

    def _apply_lamp(self, level):
        self.lamp_level = level
        self.lamp_pwm.set_duty_u16(int(level * 65535 / 100))

    def _update_lamp(self, now_ms):
        if self.lamp_level == self._ramp_to:
            return
        elapsed = ticks_diff(now_ms, self._ramp_start_ms)
        if elapsed >= self._ramp_ms:
            self._apply_lamp(self._ramp_to)
        else:
            frac = elapsed / self._ramp_ms
            self._apply_lamp(self._ramp_from + (self._ramp_to - self._ramp_from) * frac)

    def _tone(self, freq_hz, duty):
        self.audio_pwm.set_freq(freq_hz)
        self.audio_pwm.set_duty_u16(duty)

    def _update_audio(self, now_ms):
        # Prioridad: alarma > intercomunicador > canción
        if self.alarm_active:
            freq, dur = ALARM_TONES[self._alarm_idx]
            if ticks_diff(now_ms, self._alarm_tone_ms) >= dur:
                self._alarm_idx = (self._alarm_idx + 1) % len(ALARM_TONES)
                self._alarm_tone_ms = now_ms
                freq = ALARM_TONES[self._alarm_idx][0]
            self._tone(freq, MAX_TONE_DUTY)
            return
        if self.intercom.active:
            return
        if not self.song_notes:
            return
        if self._song_end_ms is not None and ticks_diff(now_ms, self._song_end_ms) >= 0:
            self.stop_song()
            return
        note = self.song_notes[self._song_idx]
        if ticks_diff(now_ms, self._note_start_ms) >= note[1]:
            self._song_idx += 1
            if self._song_idx >= len(self.song_notes):
                if self._song_end_ms is None:  # sin duración: una sola vez
                    self.stop_song()
                    return
                self._song_idx = 0
            self._note_start_ms = now_ms
            note = self.song_notes[self._song_idx]
        self._tone(note[0], MAX_TONE_DUTY // 2 if note[0] > 0 else 0)

    def stop_song(self):
        self.song_notes = []
        self.song_code = None
        self.audio_pwm.set_duty_u16(0)

    # ---------- sensores ----------
    def _sample_env(self, now_ms):
        reading = self.env_sensor.read()
        if reading is None:
            return
        temp, hum = reading
        self._temps.append(temp)
        self._hums.append(hum)
        out = temp < self.temp_min or temp > self.temp_max or hum < self.hum_min or hum > self.hum_max
        if not out:
            self._out_of_range_since = None
            self._env_alerted = False
            return
        if self._out_of_range_since is None:
            self._out_of_range_since = now_ms
        if not self._env_alerted and ticks_diff(now_ms, self._out_of_range_since) >= ENV_SUSTAIN_MS:
            self._env_alerted = True
            if temp > self.temp_max:
                cause = "temp_high"
            elif temp < self.temp_min:
                cause = "temp_low"
            else:
                cause = "hum_high" if hum > self.hum_max else "hum_low"
            self.emit_event("env_out_of_range", temp_c=temp, hum_pct=hum, cause=cause)

    def _flush_minute(self):
        if not self._temps:
            return
        self.emit_telemetry({
            "temp_c": round(sum(self._temps) / len(self._temps), 2),
            "hum_pct": round(sum(self._hums) / len(self._hums), 2),
        })
        self._temps = []
        self._hums = []

    def _poll_touch(self, now_ms):
        value = self.touch_pin.read()
        if value and not self._last_touch:
            if self._last_touch_ms is None or ticks_diff(now_ms, self._last_touch_ms) >= TOUCH_DEBOUNCE_MS:
                self._last_touch_ms = now_ms
                self.emit_event("button_attention")
        self._last_touch = value

    def tick(self, now_ms=None):
        now_ms = ticks_ms() if now_ms is None else now_ms
        if self._minute_start_ms is None:
            self._minute_start_ms = now_ms
        self._poll_touch(now_ms)
        self._update_lamp(now_ms)
        self._update_audio(now_ms)
        if self._last_env_ms is None or ticks_diff(now_ms, self._last_env_ms) >= 1000:
            self._last_env_ms = now_ms
            self._sample_env(now_ms)
        if ticks_diff(now_ms, self._minute_start_ms) >= 60000:
            self._minute_start_ms = now_ms
            self._flush_minute()

    # ---------- configuración y comandos ----------
    def apply_config(self, config):
        self.temp_min = float(config.get("temp_min", self.temp_min))
        self.temp_max = float(config.get("temp_max", self.temp_max))
        self.hum_min = float(config.get("hum_min", self.hum_min))
        self.hum_max = float(config.get("hum_max", self.hum_max))

    def handle_command(self, cmd, params, now_ms=None):
        now_ms = ticks_ms() if now_ms is None else now_ms
        if cmd == "light_set":
            level = int(params.get("level", 50)) if params.get("on") else 0
            if level < 0 or level > 100:
                return False, "level_fuera_de_rango"
            self.start_lamp_ramp(level, int(params.get("ramp_ms", 1500)), now_ms)
        elif cmd == "status_led":
            if not self.set_led(str(params.get("color", ""))):
                return False, "color_invalido"
        elif cmd == "mode_set":
            mode = str(params.get("mode", ""))
            if mode not in MODE_COLORS:
                return False, "modo_invalido"
            self.mode = mode
            self.set_led(MODE_COLORS[mode])
        elif cmd == "alarm_set":
            self.alarm_active = bool(params.get("on"))
            if self.alarm_active:
                self.intercom.stop()
                self._alarm_idx = 0
                self._alarm_tone_ms = now_ms
                self._tone(ALARM_TONES[0][0], MAX_TONE_DUTY)
                self.set_led("red")
            else:
                self.audio_pwm.set_duty_u16(0)
                self.set_led(MODE_COLORS.get(self.mode, "green"))
        elif cmd == "thresholds_set":
            self.apply_config(params)
        elif cmd == "song_play":
            if params.get("action", "play") == "stop":
                self.stop_song()
                return True, None
            notes = []
            for n in params.get("notes") or []:
                freq = int(n.get("freq_hz", n.get("freq", 0)))
                ms = int(n.get("ms", 0))
                if 0 <= freq <= 5000 and 0 < ms <= 5000:
                    notes.append((freq, ms))
            if not notes:
                return False, "sin_notas"
            self.song_notes = notes
            self.song_code = params.get("song")
            self._song_idx = 0
            self._note_start_ms = now_ms
            duration_s = int(params.get("duration_s", 0))
            self._song_end_ms = now_ms + duration_s * 1000 if duration_s > 0 else None
            if not self.alarm_active and not self.intercom.active:
                self._tone(notes[0][0], MAX_TONE_DUTY // 2)
        elif cmd == "audio_chunk":
            if self.alarm_active:
                return False, "alarma_activa"
            rate = int(params.get("rate", 8000))
            if rate < 4000 or rate > 16000:
                return False, "rate_invalido"
            pcm = b64decode(str(params.get("pcm_b64", "")))
            if not pcm or len(pcm) > 1280:
                return False, "pcm_invalido"
            if not self.intercom.enqueue(pcm, rate):
                return False, "cola_audio_llena"
        else:
            return False, "unknown_cmd"
        return True, None


if IS_MICROPYTHON:
    import secrets  # type: ignore  # noqa: E402 - solo existe en la Pico, nunca en Git

    run(CentralNode(), secrets)
