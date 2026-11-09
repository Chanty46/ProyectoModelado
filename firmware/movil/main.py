"""
Nodo Móvil Colgante (Pico W #3) — MicroPython.

RF13  9 LEDs normales con patrones circulares/cascada y velocidades:
      `lenta` (600 ms), `media` (300 ms), `rapida` (120 ms).
RF33  Registra duración efectiva de la sesión y emite `event: mobile_session_end`
      (`secs`, `pattern`, `speed`) al detenerse o completarse el tiempo fijado.
"""

from common.device_runtime import DeviceNode, run
from common.hal import IS_MICROPYTHON, DigitalPinAdapter, ticks_diff, ticks_ms
from common.pins import MOVIL_PINS

SPEED_INTERVAL_MS = {
    "lenta": 600,
    "media": 300,
    "rapida": 120,
}


class MovilNode(DeviceNode):
    device_type = "movil"
    tick_interval_ms = 40  # Resolución rápida para cambios de paso fluidos

    def __init__(self, buffer=None, clock=None):
        DeviceNode.__init__(self, buffer, clock)
        self.leds = [DigitalPinAdapter(p, is_output=True) for p in MOVIL_PINS["LED_PINS"]]
        self.is_active = False
        self.pattern = "ola_circular"
        self.speed = "media"
        self.duration_s = 0
        self._session_start_ms = None
        self._last_step_ms = 0
        self._step_idx = 0
        self._turn_all_off()

    def _turn_all_off(self):
        for led in self.leds:
            led.write(0)

    def handle_command(self, cmd, params, now_ms=None):
        now_ms = ticks_ms() if now_ms is None else now_ms
        if cmd == "mobile_play":
            action = params.get("action", "play")
            if action == "stop":
                self.stop_session(now_ms)
                return True, None

            patron = str(params.get("pattern", "ola_circular"))
            velocidad = str(params.get("speed", "media"))
            if velocidad not in SPEED_INTERVAL_MS:
                return False, "velocidad_invalida"

            dur = int(params.get("duration_s", 60))
            if dur < 0 or dur > 3600:
                return False, "duracion_invalida"

            self.pattern = patron
            self.speed = velocidad
            self.duration_s = dur
            self.is_active = True
            self._session_start_ms = now_ms
            self._last_step_ms = now_ms
            self._step_idx = 0
            self._advance_pattern()
            return True, None
        return False, "unknown_cmd"

    def stop_session(self, now_ms):
        if not self.is_active:
            return
        elapsed_s = max(1, ticks_diff(now_ms, self._session_start_ms) // 1000)
        self.is_active = False
        self._turn_all_off()
        self.emit_event(
            "mobile_session_end",
            secs=elapsed_s,
            pattern=self.pattern,
            speed=self.speed,
        )

    def _advance_pattern(self):
        n = len(self.leds)
        if not self.is_active:
            self._turn_all_off()
            return

        if self.pattern == "cascada_suave":
            # Encendido progresivo hasta llenar y reiniciar
            stage = self._step_idx % (n + 1)
            for i, led in enumerate(self.leds):
                led.write(1 if i < stage else 0)
        elif self.pattern == "destello_alterno":
            # Par / impar alternado
            even_on = (self._step_idx % 2) == 0
            for i, led in enumerate(self.leds):
                led.write(1 if (i % 2 == 0) == even_on else 0)
        else:  # "ola_circular" por defecto
            active_pin = self._step_idx % n
            for i, led in enumerate(self.leds):
                led.write(1 if i == active_pin else 0)

        self._step_idx += 1

    def tick(self, now_ms=None):
        now_ms = ticks_ms() if now_ms is None else now_ms
        if not self.is_active:
            return

        # Verificar si expiró la duración límite fijada
        if self.duration_s > 0 and ticks_diff(now_ms, self._session_start_ms) >= (self.duration_s * 1000):
            self.stop_session(now_ms)
            return

        interval = SPEED_INTERVAL_MS.get(self.speed, 300)
        if ticks_diff(now_ms, self._last_step_ms) >= interval:
            self._last_step_ms = now_ms
            self._advance_pattern()


if IS_MICROPYTHON:
    import secrets  # type: ignore  # noqa: E402

    run(MovilNode(), secrets)
