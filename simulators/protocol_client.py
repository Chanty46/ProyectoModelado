"""
Cliente de protocolo TCP, búfer circular offline de 48 h y motores de reglas puras
para los 5 dispositivos del ecosistema Cuna Inteligente (M3).
"""

from __future__ import annotations

import base64
from collections import deque
from dataclasses import dataclass, field
import json
import time
from typing import Any, Deque, Dict, List, Optional, Tuple


MAX_LINE_BYTES = 4096
OFFLINE_BUFFER_CAPACITY = 2880  # 48 h * 60 min (D12 / RNF04)
REMOVAL_WINDOW_S = 1.0001
MOVEMENT_SLIDING_WINDOW_S = 60.0
MOVEMENT_ALERT_SUSTAIN_S = 30.0


def compute_backoff_seconds(attempt: int) -> int:
    """Calcula espera exponencial `1, 2, 4, 8, 16...` hasta un máximo de 30 s (PROTOCOL.md)."""
    if attempt <= 0:
        return 1
    delay = 1 << min(attempt - 1, 5)
    return min(delay, 30)


def build_envelope(msg_type: str, seq: int, data: Dict[str, Any], ts: Optional[int] = None) -> str:
    """Construye una línea JSON terminada en `\\n` garantizando el límite de 4 KB."""
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
        """Crea la cola circular acotada a `capacity` sobres y fija el contador `seq`."""
        self.capacity = capacity
        self._seq = initial_seq
        self._queue: Deque[Dict[str, Any]] = deque(maxlen=capacity)

    @property
    def current_seq(self) -> int:
        """Último número de secuencia asignado o sincronizado."""
        return self._seq

    def next_seq(self) -> int:
        """Incrementa y devuelve el siguiente `seq` monotónico del dispositivo."""
        self._seq += 1
        return self._seq

    def enqueue(self, msg_type: str, data: Dict[str, Any], ts: Optional[int] = None) -> Dict[str, Any]:
        """Construye y encola un sobre con el siguiente `seq`, descartando el más antiguo si está llena."""
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
        """Elimina de la cola todos los mensajes ya confirmados con `seq <= acked_seq`."""
        while self._queue and self._queue[0]["seq"] <= acked_seq:
            self._queue.popleft()

    def sync_with_server(self, server_last_seq: int) -> None:
        """Tras `hello_ack`: evita reutilizar un `seq` que el servidor ya procesó."""
        server_seq = int(server_last_seq or 0)
        if server_seq > self._seq:
            self._seq = server_seq
        self.ack_up_to(min(server_seq, self._seq))

    def pending_messages(self) -> List[Dict[str, Any]]:
        """Devuelve una copia ordenada de los sobres pendientes de confirmación."""
        return list(self._queue)


@dataclass
class MattressRuleEngine:
    """
    Reglas del Colchón (RF04, RF06, RF29 y Decisión D14):
    - Con `mass_scale` (por defecto 0.2 para celda de 1 kg):
      * Umbral de retiro alto: `2.50 * mass_scale` (0.50 kg con escala 0.2)
      * Umbral de presencia / retiro bajo: `1.00 * mass_scale` (0.20 kg con escala 0.2)
    - Retiro (RF04): masa cae de >= umbral_alto a < umbral_bajo en <= 1.0 s.
    - Índice de movimiento a 5 Hz (RF06): suma de `|Δmasa|` ignorando piso de ruido
      y descartando saltos bruscos que correspondan a retiro o colocación.
    - Alerta de movimiento anómalo (RF29): índice en ventana deslizante (kg/min)
      supera `movement_threshold_kg_per_min` durante 30 s seguidos.
    """

    mass_scale: float = 0.20
    noise_floor_kg: float = 0.010
    movement_threshold_kg_per_min: float = 0.150
    movement_alert_enabled: bool = True

    _recent_samples: Deque[Tuple[float, float]] = field(default_factory=lambda: deque(maxlen=10))
    _minute_accumulated_delta: float = 0.0
    _sliding_deltas: Deque[Tuple[float, float]] = field(default_factory=deque)
    _sliding_delta_sum: float = 0.0
    _high_movement_since: Optional[float] = None
    _movement_alert_emitted: bool = False
    _removal_emitted_for_drop: bool = False

    @property
    def presence_threshold_kg(self) -> float:
        """Umbral mínimo de masa para considerar al bebé presente en el colchón."""
        return round(1.00 * self.mass_scale, 4)

    @property
    def removal_upper_threshold_kg(self) -> float:
        """Umbral alto previo desde el cual una caída brusca dispara `removal`."""
        return round(2.50 * self.mass_scale, 4)

    @property
    def jump_ignore_threshold_kg(self) -> float:
        """Saltos mayores o iguales a este cambio corresponden a retiro/colocación, no a movimiento."""
        return round(1.25 * self.mass_scale, 4)

    def _evaluate_removal(self, timestamp_s: float, mass_kg: float) -> Optional[Dict[str, Any]]:
        """Detecta caída súbita de >= umbral alto a < presencia en <= 1.0 s (RF04 + D14)."""
        if mass_kg >= self.presence_threshold_kg:
            self._removal_emitted_for_drop = False
            return None
        if self._removal_emitted_for_drop:
            return None
        upper = self.removal_upper_threshold_kg
        for t, m in self._recent_samples:
            if (timestamp_s - t) <= REMOVAL_WINDOW_S and m >= upper:
                self._removal_emitted_for_drop = True
                return {
                    "kind": "removal",
                    "mass_kg": round(mass_kg, 3),
                    "mass_scale": self.mass_scale,
                }
        return None

    def _accumulate_movement_delta(
        self,
        timestamp_s: float,
        prev_sample: Optional[Tuple[float, float]],
        mass_kg: float,
    ) -> None:
        """Actualiza el acumulador por minuto y la ventana deslizante de 60 s a 5 Hz (RF06)."""
        delta_valid = 0.0
        presence = self.presence_threshold_kg
        if prev_sample is not None and mass_kg > presence and prev_sample[1] > presence:
            raw_delta = abs(mass_kg - prev_sample[1])
            if self.noise_floor_kg < raw_delta < self.jump_ignore_threshold_kg:
                delta_valid = raw_delta
                self._minute_accumulated_delta += delta_valid

        self._sliding_deltas.append((timestamp_s, delta_valid))
        self._sliding_delta_sum += delta_valid
        while self._sliding_deltas and (timestamp_s - self._sliding_deltas[0][0]) > MOVEMENT_SLIDING_WINDOW_S:
            _, expired_delta = self._sliding_deltas.popleft()
            self._sliding_delta_sum = max(0.0, self._sliding_delta_sum - expired_delta)

    def _evaluate_anomalous_movement(self, timestamp_s: float, mass_kg: float) -> Optional[Dict[str, Any]]:
        """Emite `movement_high` si el índice supera el umbral durante 30 s continuos (RF29)."""
        if not self.movement_alert_enabled or mass_kg <= self.presence_threshold_kg:
            self._high_movement_since = None
            self._movement_alert_emitted = False
            return None

        sliding_rate_kg_min = self._sliding_delta_sum
        if sliding_rate_kg_min <= self.movement_threshold_kg_per_min:
            self._high_movement_since = None
            self._movement_alert_emitted = False
            return None

        if self._high_movement_since is None:
            self._high_movement_since = timestamp_s
            return None

        if (timestamp_s - self._high_movement_since) >= MOVEMENT_ALERT_SUSTAIN_S and not self._movement_alert_emitted:
            self._movement_alert_emitted = True
            return {
                "kind": "movement_high",
                "movement_idx": round(sliding_rate_kg_min, 3),
                "threshold": self.movement_threshold_kg_per_min,
            }
        return None

    def process_sample(self, timestamp_s: float, mass_kg: float) -> List[Dict[str, Any]]:
        """Procesa una muestra a 5 Hz y devuelve los eventos discretos generados."""
        events: List[Dict[str, Any]] = []
        prev_sample = self._recent_samples[-1] if self._recent_samples else None
        self._recent_samples.append((timestamp_s, mass_kg))

        removal_event = self._evaluate_removal(timestamp_s, mass_kg)
        if removal_event is not None:
            events.append(removal_event)

        self._accumulate_movement_delta(timestamp_s, prev_sample, mass_kg)

        movement_event = self._evaluate_anomalous_movement(timestamp_s, mass_kg)
        if movement_event is not None:
            events.append(movement_event)

        return events

    def pop_minute_telemetry(self, current_mass_kg: float) -> Dict[str, Any]:
        """Extrae el resumen de masa actual e índice de movimiento acumulado en el minuto."""
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
        """Evalúa temperatura/humedad y emite `env_out_of_range` tras 10 s fuera de rango."""
        out_of_range = (
            temp_c < self.temp_min
            or temp_c > self.temp_max
            or hum_pct < self.hum_min
            or hum_pct > self.hum_max
        )
        if not out_of_range:
            self._out_of_range_since = None
            self._alert_active = False
            return None

        if self._out_of_range_since is None:
            self._out_of_range_since = timestamp_s
            return None

        if (timestamp_s - self._out_of_range_since) >= 10.0 and not self._alert_active:
            self._alert_active = True
            return {
                "kind": "env_out_of_range",
                "temp_c": round(temp_c, 2),
                "hum_pct": round(hum_pct, 2),
            }
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


@dataclass
class CentralAudioPlayer:
    """
    Receptor de audio PCM 8-bit sin signo para el intercomunicador (RF11 / Spike M8).
    Decodifica bloques Base64 recibidos por TCP (`audio_chunk`), valida el tamaño del bloque
    y calcula los ciclos de trabajo PWM de 16 bits (`duty_u16`).
    """

    chunks_received: int = 0
    samples_played: int = 0
    last_rate_hz: int = 8000
    last_max_duty_u16: int = 0

    def process_audio_chunk(self, params: Dict[str, Any]) -> Dict[str, Any]:
        """Decodifica un bloque PCM u8 en Base64 (1..1280 bytes) y actualiza estadísticas."""
        rate = int(params.get("rate", 8000))
        pcm_b64 = str(params.get("pcm_b64", ""))
        raw_bytes = base64.b64decode(pcm_b64)
        if not raw_bytes or len(raw_bytes) > 1280:
            raise ValueError("Tamaño de bloque PCM fuera de rango (1..1280 bytes).")

        duties = [b * 257 for b in raw_bytes]
        self.chunks_received += 1
        self.samples_played += len(raw_bytes)
        self.last_rate_hz = rate
        self.last_max_duty_u16 = max(duties)

        duration_ms = round((len(raw_bytes) / rate) * 1000.0, 2)
        return {
            "samples": len(raw_bytes),
            "rate_hz": rate,
            "duration_ms": duration_ms,
            "max_duty_u16": self.last_max_duty_u16,
        }
