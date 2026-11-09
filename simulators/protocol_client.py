"""
Cliente de protocolo TCP, búfer circular offline de 48 h y motores de reglas puras
para los 5 dispositivos del ecosistema Cuna Inteligente (M3).
"""

from __future__ import annotations
from collections import deque
from dataclasses import dataclass, field
import json
import time
from typing import Any, Deque, Dict, List, Optional, Tuple


MAX_LINE_BYTES = 4096
OFFLINE_BUFFER_CAPACITY = 2880  # 48 h * 60 min (D12 / RNF04)


def compute_backoff_seconds(attempt: int) -> int:
    """Espera exponencial 1, 2, 4, 8, 16... hasta un máximo de 30 s (PROTOCOL.md)."""
    if attempt <= 0:
        return 1
    delay = 1 << min(attempt - 1, 5)
    return min(delay, 30)


def build_envelope(msg_type: str, seq: int, data: Dict[str, Any], ts: Optional[int] = None) -> str:
    """Construye una línea JSON terminada en \\n garantizando el límite de 4 KB."""
    payload = {
        "v": 1,
        "t": msg_type,
        "seq": seq,
        "ts": ts if ts is not None else int(time.time()),
        "d": data,
    }
    encoded = json.dumps(payload, separators=(",", ":")) + "\n"
    if len(encoded.encode("utf-8")) > MAX_LINE_BYTES:
        raise ValueError("El mensaje serializado supera el límite de 4 KB.")
    return encoded


class OfflineRingBuffer:
    """
    Cola circular en memoria (48 h de registros agregados por minuto) con número
    de secuencia monotónico para reenvío en orden tras reconexión sin duplicados.
    """

    def __init__(self, capacity: int = OFFLINE_BUFFER_CAPACITY, initial_seq: int = 0) -> None:
        self.capacity = capacity
        self._seq = initial_seq
        self._queue: Deque[Dict[str, Any]] = deque(maxlen=capacity)

    @property
    def current_seq(self) -> int:
        return self._seq

    def next_seq(self) -> int:
        self._seq += 1
        return self._seq

    def enqueue(self, msg_type: str, data: Dict[str, Any], ts: Optional[int] = None) -> Dict[str, Any]:
        seq = self.next_seq()
        entry = {
            "v": 1,
            "t": msg_type,
            "seq": seq,
            "ts": ts if ts is not None else int(time.time()),
            "d": data,
        }
        self._queue.append(entry)
        return entry

    def ack_up_to(self, acked_seq: int) -> None:
        while self._queue and self._queue[0]["seq"] <= acked_seq:
            self._queue.popleft()

    def pending_messages(self) -> List[Dict[str, Any]]:
        return list(self._queue)


@dataclass
class MattressRuleEngine:
    """
    Reglas del Colchón (RF04, RF06, RF29 y Decisión D14):
    - Con mass_scale (por defecto 0.2 para celda de 1 kg):
      * Umbral de retiro alto: 2.50 * mass_scale (0.50 kg con escala 0.2)
      * Umbral de presencia / retiro bajo: 1.00 * mass_scale (0.20 kg con escala 0.2)
    - Retiro (RF04): masa cae de >= umbral_alto a < umbral_bajo en <= 1.0 s.
    - Índice de movimiento a 5 Hz (RF06): suma de |Δmasa| ignorando piso de ruido
      y descartando saltos bruscos que correspondan a retiro o colocación.
    - Alerta de movimiento anómalo (RF29): índice en ventana deslizante (kg/min)
      supera movement_threshold durante 30 s seguidos.
    """

    mass_scale: float = 0.20
    noise_floor_kg: float = 0.010
    movement_threshold_kg_per_min: float = 0.150
    movement_alert_enabled: bool = True

    _recent_samples: Deque[Tuple[float, float]] = field(default_factory=lambda: deque(maxlen=10))
    _minute_accumulated_delta: float = 0.0
    _sliding_deltas: Deque[Tuple[float, float]] = field(default_factory=deque)
    _high_movement_since: Optional[float] = None
    _movement_alert_emitted: bool = False
    _removal_emitted_for_drop: bool = False

    @property
    def presence_threshold_kg(self) -> float:
        return round(1.00 * self.mass_scale, 4)

    @property
    def removal_upper_threshold_kg(self) -> float:
        return round(2.50 * self.mass_scale, 4)

    @property
    def jump_ignore_threshold_kg(self) -> float:
        # Saltos mayores o iguales al cambio de colocación/retiro se descartan del índice de movimiento
        return round(1.25 * self.mass_scale, 4)

    def process_sample(self, timestamp_s: float, mass_kg: float) -> List[Dict[str, Any]]:
        events: List[Dict[str, Any]] = []
        prev_sample = self._recent_samples[-1] if self._recent_samples else None
        self._recent_samples.append((timestamp_s, mass_kg))

        # 1. Detección de retiro imprevisto en <= 1.0 s (RF04 + D14)
        if mass_kg >= self.presence_threshold_kg:
            self._removal_emitted_for_drop = False
        elif not self._removal_emitted_for_drop:
            had_recent_high_mass = any(
                (timestamp_s - t) <= 1.0001 and m >= self.removal_upper_threshold_kg
                for t, m in self._recent_samples
            )
            if had_recent_high_mass:
                self._removal_emitted_for_drop = True
                events.append({
                    "kind": "removal",
                    "mass_kg": round(mass_kg, 3),
                    "mass_scale": self.mass_scale,
                })

        # 2. Cálculo de índice de movimiento a 5 Hz (RF06) solo si hay presencia (> presence_threshold_kg)
        delta_valid = 0.0
        if prev_sample is not None and mass_kg > self.presence_threshold_kg and prev_sample[1] > self.presence_threshold_kg:
            raw_delta = abs(mass_kg - prev_sample[1])
            if self.noise_floor_kg < raw_delta < self.jump_ignore_threshold_kg:
                delta_valid = raw_delta
                self._minute_accumulated_delta += delta_valid

        self._sliding_deltas.append((timestamp_s, delta_valid))
        while self._sliding_deltas and (timestamp_s - self._sliding_deltas[0][0]) > 60.0:
            self._sliding_deltas.popleft()

        # 3. Evaluación de movimiento anómalo durante 30 s consecutivos (RF29)
        if self.movement_alert_enabled and mass_kg > self.presence_threshold_kg:
            sliding_rate_kg_min = sum(d for _, d in self._sliding_deltas)
            if sliding_rate_kg_min > self.movement_threshold_kg_per_min:
                if self._high_movement_since is None:
                    self._high_movement_since = timestamp_s
                elif (timestamp_s - self._high_movement_since) >= 30.0 and not self._movement_alert_emitted:
                    self._movement_alert_emitted = True
                    events.append({
                        "kind": "movement_high",
                        "movement_idx": round(sliding_rate_kg_min, 3),
                        "threshold": self.movement_threshold_kg_per_min,
                    })
            else:
                self._high_movement_since = None
                self._movement_alert_emitted = False
        else:
            self._high_movement_since = None
            self._movement_alert_emitted = False

        return events

    def pop_minute_telemetry(self, current_mass_kg: float) -> Dict[str, Any]:
        idx = round(self._minute_accumulated_delta, 3)
        self._minute_accumulated_delta = 0.0
        return {
            "mass_kg": round(current_mass_kg, 3),
            "movement_idx": idx,
        }


@dataclass
class CentralEnvRuleEngine:
    """
    Reglas de la Unidad Central (RF07 desvío térmico/humedad >= 10 s y RF09 rampa PWM 1.5 s).
    """

    temp_min: float = 18.0
    temp_max: float = 26.0
    hum_min: float = 40.0
    hum_max: float = 60.0
    light_level: int = 0
    _out_of_range_since: Optional[float] = None
    _alert_active: bool = False

    def evaluate_env(self, timestamp_s: float, temp_c: float, hum_pct: float) -> Optional[Dict[str, Any]]:
        out_of_range = (
            temp_c < self.temp_min
            or temp_c > self.temp_max
            or hum_pct < self.hum_min
            or hum_pct > self.hum_max
        )
        if out_of_range:
            if self._out_of_range_since is None:
                self._out_of_range_since = timestamp_s
            elif (timestamp_s - self._out_of_range_since) >= 10.0 and not self._alert_active:
                self._alert_active = True
                return {
                    "kind": "env_out_of_range",
                    "temp_c": round(temp_c, 2),
                    "hum_pct": round(hum_pct, 2),
                }
        else:
            self._out_of_range_since = None
            self._alert_active = False
        return None

    def compute_light_ramp_steps(self, target_level: int, steps: int = 15) -> List[int]:
        """RF09: Rampa lineal suave de 1.5 s entre el nivel actual y el nivel destino."""
        target = max(0, min(100, target_level))
        start = self.light_level
        result = [
            round(start + (target - start) * (i / steps))
            for i in range(1, steps + 1)
        ]
        self.light_level = target
        return result
