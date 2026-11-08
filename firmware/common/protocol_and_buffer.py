"""
Cliente de protocolo TCP JSON por línea y búfer circular offline de 48 horas
para Raspberry Pi Pico W (MicroPython) y pruebas en PC (Python 3).

Cumple con:
- D1 / docs/PROTOCOL.md: Sobre JSON `{"v":1,"t":...,"seq":...,"ts":...,"d":{...}}`,
  máx. 4096 bytes por línea, heartbeat cada 15 s, reconexión exponencial 1..30 s.
- D12 / RNF04: Búfer circular offline de hasta 48 horas con registros agregados por minuto
  (48 h * 60 min = 2880 registros máx.) diseñado para caber holgadamente en los ~2 MB de flash
  de la Pico W y retransmitir en orden con `seq` monótono sin duplicados al reconectar.
"""

from collections import deque
import json
from typing import Any

MAX_LINE_BYTES = 4096
PROTOCOL_VERSION = 1
MAX_48H_MINUTES = 2880  # 48 horas * 60 registros agregados por minuto (D12 / RNF04)
HEARTBEAT_INTERVAL_S = 15
BACKOFF_MIN_S = 1.0
BACKOFF_MAX_S = 30.0


class OfflineCircularBuffer48h:
    """
    Búfer circular de hasta 2880 registros agregados por minuto (48 h) u otros eventos críticos.
    Mantiene un contador `seq` estrictamente monótono para que el servidor C# deduplique
    retransmisiones tras reconexión Wi-Fi/TCP (RNF04 / D12).
    """

    def __init__(self, max_records: int = MAX_48H_MINUTES, initial_seq: int = 0) -> None:
        if max_records <= 0 or max_records > MAX_48H_MINUTES:
            raise ValueError(f"max_records debe estar entre 1 y {MAX_48H_MINUTES}.")
        self.max_records = max_records
        self._next_seq = max(0, int(initial_seq))
        self._queue: deque[dict[str, Any]] = deque(maxlen=max_records)
        self._dropped_oldest_count = 0

    @property
    def last_seq(self) -> int:
        return self._next_seq

    @property
    def pending_count(self) -> int:
        return len(self._queue)

    @property
    def dropped_oldest_count(self) -> int:
        return self._dropped_oldest_count

    def next_seq(self) -> int:
        self._next_seq += 1
        return self._next_seq

    def enqueue_envelope(self, msg_type: str, payload: dict[str, Any], ts: int) -> dict[str, Any]:
        """Construye un sobre del protocolo v1 con `seq` único y lo encola en el búfer circular."""
        seq = self.next_seq()
        envelope = {
            "v": PROTOCOL_VERSION,
            "t": msg_type,
            "seq": seq,
            "ts": int(ts),
            "d": payload,
        }
        encoded = encode_json_line(envelope)
        if len(encoded) > MAX_LINE_BYTES:
            raise ValueError(f"El mensaje excede el límite de {MAX_LINE_BYTES} bytes del protocolo.")

        if len(self._queue) == self.max_records:
            self._dropped_oldest_count += 1
        self._queue.append(envelope)
        return envelope

    def peek_oldest(self) -> dict[str, Any] | None:
        if not self._queue:
            return None
        return self._queue[0]

    def acknowledge_seq(self, ack_seq: int) -> int:
        """
        Elimina del búfer todos los mensajes con `seq <= ack_seq` tras recibir `ack` del servidor.
        Devuelve la cantidad de registros confirmados y liberados.
        """
        removed = 0
        while self._queue and int(self._queue[0]["seq"]) <= int(ack_seq):
            self._queue.popleft()
            removed += 1
        return removed

    def drain_all_for_flush(self) -> list[dict[str, Any]]:
        """Devuelve una copia en orden FIFO de todos los sobres pendientes para retransmitir al reconectar."""
        return list(self._queue)


def encode_json_line(envelope: dict[str, Any]) -> bytes:
    """Codifica un sobre JSON compacto terminado en `\\n` y valida el límite de 4096 bytes."""
    line = json.dumps(envelope, separators=(",", ":")) + "\n"
    raw = line.encode("utf-8")
    if len(raw) > MAX_LINE_BYTES:
        raise ValueError(f"Línea TCP de {len(raw)} bytes supera el máximo de {MAX_LINE_BYTES} bytes.")
    return raw


def decode_json_line(raw_line: bytes | str) -> dict[str, Any]:
    """Decodifica una línea JSON recibida del servidor TCP validando tamaño y versión."""
    if isinstance(raw_line, bytes):
        if len(raw_line) > MAX_LINE_BYTES:
            raise ValueError("Línea recibida excede 4096 bytes.")
        text = raw_line.decode("utf-8").strip()
    else:
        if len(raw_line.encode("utf-8")) > MAX_LINE_BYTES:
            raise ValueError("Línea recibida excede 4096 bytes.")
        text = raw_line.strip()

    obj = json.loads(text)
    if not isinstance(obj, dict) or obj.get("v") != PROTOCOL_VERSION or "t" not in obj:
        raise ValueError("Sobre de protocolo inválido.")
    return obj


def compute_reconnect_backoff_s(attempt: int) -> float:
    """
    Calcula el tiempo de espera exponencial entre reconexiones Wi-Fi/TCP:
    1 s -> 2 s -> 4 s -> 8 s -> 16 s -> máx. 30 s (docs/PROTOCOL.md).
    """
    clamped_attempt = max(0, min(10, int(attempt)))
    delay = BACKOFF_MIN_S * (2 ** clamped_attempt)
    return float(min(BACKOFF_MAX_S, delay))
