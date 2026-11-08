"""
Firmware MicroPython para Nodo de Colchón (`colchon`) — Hito M9.

Responsabilidades:
- D14 / RF04: Celda de carga de 1 kg con `mass_scale` (0.2 por defecto).
  Detecta retiro súbito cuando la masa cae de `>= 2.50 * mass_scale` (0.50 kg)
  a `< 1.00 * mass_scale` (0.20 kg) en `<= 1.0 s` y emite `event: removal` de inmediato.
- RF06: Muestreo a 5 Hz (cada 200 ms); acumula `|Δmasa|` ignorando piso de ruido (`noise_floor_kg`)
  y descartando saltos correspondientes a retiro/colocación; persiste cada 60 s (`movement_idx`).
- RF29: Ventana deslizante de 30 s (150 muestras a 5 Hz) expresada en kg/min equivalentes;
  si supera `movement_threshold_kg_min` de forma sostenida durante 30 s, emite `event: movement_high`.
"""

from collections import deque
from typing import Any

from firmware.common.hal import Hx711LoadCellAdapter, WatchdogAdapter
from firmware.common.pins import COLCHON_PINS
from firmware.common.protocol_and_buffer import OfflineCircularBuffer48h


class ColchonFirmwareNode:
    """Controlador principal del nodo `colchon` en Raspberry Pi Pico W."""

    def __init__(self, serial: str = "colchon-pico-01", mass_scale: float = 0.2) -> None:
        self.serial = serial
        self.device_type = "colchon"
        self.mass_scale = float(mass_scale)
        self.buffer = OfflineCircularBuffer48h()
        self.wdt = WatchdogAdapter(timeout_ms=COLCHON_PINS["wdt_timeout_ms"])

        self.load_cell = Hx711LoadCellAdapter(
            dout_pin=COLCHON_PINS["hx711_dout_pin"],
            pd_sck_pin=COLCHON_PINS["hx711_pd_sck_pin"],
        )

        # Parámetros configurables (calibrables en hello_ack / thresholds_set)
        self.noise_floor_kg = 0.005
        self.jump_filter_kg = 0.22  # Descarta saltos bruscos de retiro/colocación del índice de movimiento
        self.movement_threshold_kg_min = 0.45

        # Estado de muestreo a 5 Hz (dt = 0.2 s)
        self._prev_mass_kg: float | None = None
        self._recent_1s_masses: deque[float] = deque(maxlen=6)  # 1.0 s a 5 Hz = 5 intervalos (6 muestras)
        self._sliding_30s_deltas: deque[float] = deque(maxlen=150)  # 30 s a 5 Hz = 150 muestras
        self._high_movement_sustained_ticks = 0
        self._minute_masses: list[float] = []
        self._minute_movement_accum_kg = 0.0
        self._baby_was_present = False

    @property
    def presence_threshold_kg(self) -> float:
        """Umbral de presencia escalado (1.00 kg * 0.2 = 0.20 kg según D14)."""
        return round(1.00 * self.mass_scale, 4)

    @property
    def removal_high_threshold_kg(self) -> float:
        """Masa previa mínima para considerar retiro súbito (2.50 kg * 0.2 = 0.50 kg según D14)."""
        return round(2.50 * self.mass_scale, 4)

    def sample_5hz_tick(self, ts: int) -> list[dict[str, Any]]:
        """
        Ejecuta una muestra de 200 ms (5 Hz).
        Devuelve la lista de eventos urgentes generados en este tick (`removal`, `placed`, `movement_high`).
        """
        self.wdt.feed()
        mass_kg = round(self.load_cell.read_kg(), 4)
        emitted_events: list[dict[str, Any]] = []

        self._recent_1s_masses.append(mass_kg)
        self._minute_masses.append(mass_kg)

        # 1. Detección de retiro súbito en <= 1 s (RF04 / D14)
        max_last_1s = max(self._recent_1s_masses)
        if self._baby_was_present and max_last_1s >= self.removal_high_threshold_kg and mass_kg < self.presence_threshold_kg:
            self._baby_was_present = False
            emitted_events.append(
                self.buffer.enqueue_envelope(
                    "event",
                    {"name": "removal", "prev_kg": round(max_last_1s, 3), "curr_kg": round(mass_kg, 3)},
                    ts=ts,
                )
            )
        elif not self._baby_was_present and mass_kg >= self.removal_high_threshold_kg:
            self._baby_was_present = True
            emitted_events.append(
                self.buffer.enqueue_envelope(
                    "event",
                    {"name": "placed", "mass_kg": round(mass_kg, 3)},
                    ts=ts,
                )
            )

        # 2. Cálculo del diferencial |Δmasa| a 5 Hz con filtro de ruido y descarte de salto de retiro/colocación (RF06)
        valid_delta = 0.0
        if self._prev_mass_kg is not None:
            raw_delta = abs(mass_kg - self._prev_mass_kg)
            if self.noise_floor_kg < raw_delta < self.jump_filter_kg:
                valid_delta = raw_delta
                self._minute_movement_accum_kg += valid_delta
        self._prev_mass_kg = mass_kg

        # 3. Ventana deslizante de 30 s para movimiento anómalo (RF29)
        self._sliding_30s_deltas.append(valid_delta)
        # Suma de 30 s multiplicada por 2 equivale a tasa en kg/min
        equiv_kg_per_min = sum(self._sliding_30s_deltas) * 2.0
        if equiv_kg_per_min >= self.movement_threshold_kg_min:
            self._high_movement_sustained_ticks += 1
            # 30 segundos sostenidos a 5 Hz = 150 muestras
            if self._high_movement_sustained_ticks >= 150:
                self._high_movement_sustained_ticks = 0
                emitted_events.append(
                    self.buffer.enqueue_envelope(
                        "event",
                        {"name": "movement_high", "movement_idx": round(equiv_kg_per_min, 3)},
                        ts=ts,
                    )
                )
        else:
            self._high_movement_sustained_ticks = 0

        return emitted_events

    def flush_minute_telemetry(self, ts: int) -> dict[str, Any]:
        """Persiste el registro agregado del minuto (`mass_avg_kg`, `presence`, `movement_idx`) en el búfer de 48 h."""
        if not self._minute_masses:
            self._minute_masses.append(round(self.load_cell.read_kg(), 4))

        avg_mass = round(sum(self._minute_masses) / len(self._minute_masses), 3)
        presence = avg_mass > self.presence_threshold_kg
        movement_idx = round(self._minute_movement_accum_kg, 3)

        self._minute_masses.clear()
        self._minute_movement_accum_kg = 0.0

        return self.buffer.enqueue_envelope(
            "telemetry",
            {
                "mass_avg_kg": avg_mass,
                "presence": presence,
                "movement_idx": movement_idx,
            },
            ts=ts,
        )

    def handle_command(self, envelope: dict[str, Any], ts: int) -> dict[str, Any]:
        self.wdt.feed()
        cmd_seq = int(envelope.get("seq", 0))
        data = envelope.get("d", {})
        if data.get("cmd") == "thresholds_set":
            if "movement_threshold" in data:
                self.movement_threshold_kg_min = float(data["movement_threshold"])
            if "mass_scale" in data:
                self.mass_scale = float(data["mass_scale"])
        return {
            "v": 1,
            "t": "ack",
            "seq": self.buffer.next_seq(),
            "ts": ts,
            "d": {"ack_seq": cmd_seq, "ok": True},
        }
