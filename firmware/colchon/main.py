"""
Nodo Colchón (Pico W #2) — MicroPython.

D14 / RF04  Celda de 1 kg con `mass_scale` (por defecto 0.2).
            Retiro súbito: masa pasa de >= 0.50 kg a < 0.20 kg en <= 1.0 s -> event `removal`.
            Presencia: masa > 0.20 kg.
RF06        Muestreo a 5 Hz (200 ms). Acumula |Δmasa| ignorando ruido (< noise_floor)
            y descartando saltos de retiro/colocación. Se persiste cada 60 s como `movement_idx`.
RF29        Ventana deslizante de 30 s (150 muestras a 5 Hz). Si el movimiento supera
            `movement_threshold` durante 30 s sostenidos -> event `movement_high`.
Telemetría por minuto: `mass_kg`, `movement_idx`.
"""

from common.device_runtime import DeviceNode, run
from common.hal import IS_MICROPYTHON, Hx711LoadCellAdapter, ticks_diff, ticks_ms
from common.pins import COLCHON_PINS

SAMPLE_INTERVAL_MS = 200  # 5 Hz
SLIDING_WINDOW_SAMPLES = 150  # 30 s a 5 Hz


class ColchonNode(DeviceNode):
    device_type = "colchon"
    tick_interval_ms = SAMPLE_INTERVAL_MS

    def __init__(self, buffer=None, clock=None, mass_scale=0.2):
        DeviceNode.__init__(self, buffer, clock)
        self.mass_scale = float(mass_scale)
        self.load_cell = Hx711LoadCellAdapter(
            COLCHON_PINS["HX711_DT"], COLCHON_PINS["HX711_SCK"]
        )

        # Configuración dinámica
        self.noise_floor = 0.005
        self.jump_filter = 0.22  # Descarta saltos bruscos para no inflar el índice de movimiento
        self.movement_threshold = 0.45
        self.movement_alert_enabled = True

        # Estado de masa y retiro
        self._prev_mass = None
        self._recent_1s = []  # últimas 5 muestras (1 s)
        self._was_present = False

        # Estado de movimiento (RF06 / RF29)
        self._sliding_30s = []  # 150 deltas válidos
        self._high_sustained_samples = 0
        self._high_alerted = False

        # Acumulación por minuto
        self._minute_masses = []
        self._minute_movement_accum = 0.0
        self._minute_start_ms = None

    @property
    def presence_threshold(self):
        return round(1.00 * self.mass_scale, 4)

    @property
    def removal_threshold(self):
        return round(2.50 * self.mass_scale, 4)

    def apply_config(self, config):
        if "mass_scale" in config:
            self.mass_scale = float(config["mass_scale"])
        if "movement_threshold" in config:
            self.movement_threshold = float(config["movement_threshold"])
        if "movement_alert_enabled" in config:
            self.movement_alert_enabled = bool(config["movement_alert_enabled"])
        if "mass_noise_floor" in config:
            self.noise_floor = float(config["mass_noise_floor"])

    def handle_command(self, cmd, params):
        if cmd == "thresholds_set":
            self.apply_config(params)
            return True, None
        return False, "unknown_cmd"

    def tick(self, now_ms=None):
        now_ms = ticks_ms() if now_ms is None else now_ms
        if self._minute_start_ms is None:
            self._minute_start_ms = now_ms

        mass = self.load_cell.read_kg()
        if mass is None:
            return
        mass = round(mass, 4)

        self._recent_1s.append(mass)
        if len(self._recent_1s) > 5:
            self._recent_1s.pop(0)

        self._minute_masses.append(mass)

        # 1. Detección de retiro súbito en <= 1.0 s (RF04 / D14)
        max_1s = max(self._recent_1s)
        if self._was_present and max_1s >= self.removal_threshold and mass < self.presence_threshold:
            self._was_present = False
            self.emit_event("removal", prev_kg=round(max_1s, 3), curr_kg=round(mass, 3))
        elif not self._was_present and mass >= self.removal_threshold:
            self._was_present = True

        # 2. Cálculo de diferencial |Δmasa| (RF06)
        valid_delta = 0.0
        if self._prev_mass is not None:
            diff = abs(mass - self._prev_mass)
            if self.noise_floor < diff < self.jump_filter:
                valid_delta = diff
                self._minute_movement_accum += valid_delta
        self._prev_mass = mass

        # 3. Ventana deslizante de 30 s para movimiento alto (RF29)
        self._sliding_30s.append(valid_delta)
        if len(self._sliding_30s) > SLIDING_WINDOW_SAMPLES:
            self._sliding_30s.pop(0)

        # Multiplicado por 2 equivale a tasa kg/min
        equiv_kg_min = sum(self._sliding_30s) * 2.0
        if self.movement_alert_enabled and equiv_kg_min >= self.movement_threshold:
            self._high_sustained_samples += 1
            if self._high_sustained_samples >= SLIDING_WINDOW_SAMPLES and not self._high_alerted:
                self._high_alerted = True
                self.emit_event("movement_high", movement_idx=round(equiv_kg_min, 3))
        else:
            self._high_sustained_samples = 0
            self._high_alerted = False

        # 4. Telemetría agregada cada 60 s
        if ticks_diff(now_ms, self._minute_start_ms) >= 60000:
            self._minute_start_ms = now_ms
            avg_m = round(sum(self._minute_masses) / len(self._minute_masses), 3) if self._minute_masses else mass
            mov_idx = round(self._minute_movement_accum, 3)
            self.emit_telemetry({"mass_kg": avg_m, "movement_idx": mov_idx})
            self._minute_masses = []
            self._minute_movement_accum = 0.0


if IS_MICROPYTHON:
    import secrets  # type: ignore  # noqa: E402

    run(ColchonNode(), secrets)
