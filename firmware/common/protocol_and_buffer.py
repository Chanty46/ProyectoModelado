"""
Protocolo TCP v1 (una línea JSON por mensaje) y búfer offline de 48 h.

- docs/PROTOCOL.md: sobre {"v":1,"t":...,"seq":...,"ts":...,"d":{...}}, línea <= 4096 B,
  comandos {"id","cmd","params"}, ack {"id","ok"}, reconexión 1..30 s.
- D12 / RNF04: hasta 2880 registros (48 h x 1 por minuto). En la Pico se guardan en
  flash (archivo JSONL) porque no caben en los 264 KB de RAM; en PC pueden ir en memoria.
- El `seq` nunca retrocede: se reserva por bloques en flash y se sincroniza con
  `hello_ack.last_seq`, porque el servidor descarta los `seq` ya recibidos.

Compatible con MicroPython: sin `typing`, sin `collections.deque` indexado.
"""

try:
    import ujson as json  # type: ignore
except ImportError:
    import json

try:
    import os
except ImportError:  # pragma: no cover
    import uos as os  # type: ignore

PROTOCOL_VERSION = 1
MAX_LINE_BYTES = 4096
MAX_48H_RECORDS = 2880
HEARTBEAT_INTERVAL_S = 15
HELLO_TIMEOUT_S = 5
BACKOFF_MIN_S = 1
BACKOFF_MAX_S = 30
SEQ_RESERVE_BLOCK = 100  # escrituras de flash del contador: 1 cada 100 mensajes


def encode_line(envelope):
    raw = (json.dumps(envelope) + "\n").encode("utf-8")
    if len(raw) > MAX_LINE_BYTES:
        raise ValueError("linea_excede_4kb")
    return raw


def decode_line(raw):
    if isinstance(raw, str):
        raw = raw.encode("utf-8")
    if len(raw) > MAX_LINE_BYTES:
        raise ValueError("linea_excede_4kb")
    obj = json.loads(raw.decode("utf-8").strip())
    if not isinstance(obj, dict) or obj.get("v") != PROTOCOL_VERSION or "t" not in obj:
        raise ValueError("sobre_invalido")
    if not isinstance(obj.get("d", {}), dict):
        raise ValueError("sobre_invalido")
    return obj


def build_envelope(msg_type, seq, ts, data):
    return {"v": PROTOCOL_VERSION, "t": msg_type, "seq": seq, "ts": ts, "d": data}


def build_ack(seq, ts, cmd_id, ok, error=None):
    data = {"id": cmd_id, "ok": bool(ok)}
    if error:
        data["error"] = error
    return build_envelope("ack", seq, ts, data)


def parse_command(envelope):
    """Devuelve (id, cmd, params) de un sobre `command` del servidor."""
    d = envelope.get("d") or {}
    params = d.get("params")
    if not isinstance(params, dict):
        params = {}
    return str(d.get("id", "")), str(d.get("cmd", "")), params


def reconnect_backoff_s(attempt):
    """1, 2, 4, 8, 16, 30, 30... segundos."""
    attempt = max(0, min(10, int(attempt)))
    return min(BACKOFF_MAX_S, BACKOFF_MIN_S * (1 << attempt))


class ServerClock:
    """La Pico no tiene RTC fiable: la hora se toma de `hello_ack.server_time` (D-limitaciones)."""

    def __init__(self, ticks_ms_fn):
        self._ticks_ms = ticks_ms_fn
        self._base_server_s = None
        self._base_ticks = 0

    def sync(self, server_time_s):
        self._base_server_s = int(server_time_s)
        self._base_ticks = self._ticks_ms()

    def now(self):
        """Segundos epoch según el servidor, o None si aún no se sincronizó (permitido por el protocolo)."""
        if self._base_server_s is None:
            return None
        return self._base_server_s + (self._ticks_ms() - self._base_ticks) // 1000


class OfflineBuffer:
    """
    Cola FIFO de telemetría y eventos pendientes de `ack`, con `seq` monótono.

    path=None  -> en memoria (pruebas en PC).
    path="..." -> archivo JSONL en flash (Pico W), sobrevive a reinicios.
    """

    def __init__(self, max_records=MAX_48H_RECORDS, path=None, seq_path=None):
        if max_records < 1 or max_records > MAX_48H_RECORDS:
            raise ValueError("max_records fuera de rango")
        self.max_records = max_records
        self.path = path
        self.seq_path = seq_path
        self.dropped_oldest_count = 0
        self._mem = []
        self._count = 0
        self._acked_seq = 0
        self._seq = 0
        self._reserved_seq = 0
        self._load()

    # ---- contador seq ----
    def _load(self):
        if self.seq_path:
            try:
                with open(self.seq_path) as f:
                    self._seq = int(f.read().strip() or "0")
            except (OSError, ValueError):
                self._seq = 0
            self._reserved_seq = self._seq
        if self.path:
            self._count = sum(1 for _ in self._iter_file())

    def _persist_reservation(self):
        if not self.seq_path:
            return
        self._reserved_seq = self._seq + SEQ_RESERVE_BLOCK
        with open(self.seq_path, "w") as f:
            f.write(str(self._reserved_seq))

    @property
    def last_seq(self):
        return self._seq

    def next_seq(self):
        self._seq += 1
        if self.seq_path and self._seq > self._reserved_seq:
            self._persist_reservation()
        return self._seq

    def sync_with_server(self, server_last_seq):
        """Tras `hello_ack`: nunca reutilizar un `seq` que el servidor ya conoce."""
        server_last_seq = int(server_last_seq or 0)
        if server_last_seq > self._seq:
            self._seq = server_last_seq
            if self.seq_path:
                self._persist_reservation()
        self.acknowledge(min(server_last_seq, self._seq))

    # ---- cola ----
    @property
    def pending_count(self):
        return self._count

    def enqueue(self, msg_type, data, ts):
        envelope = build_envelope(msg_type, self.next_seq(), ts, data)
        line = encode_line(envelope)
        if self.path:
            with open(self.path, "ab") as f:
                f.write(line)
        else:
            self._mem.append(envelope)
        self._count += 1
        # En flash se descarta por lotes de 60 (1 h) para no reescribir el archivo cada minuto
        limit = self.max_records + (59 if self.path else 0)
        if self._count > limit:
            self._drop_oldest(self._count - self.max_records)
        return envelope

    def _iter_file(self):
        try:
            with open(self.path, "rb") as f:
                for raw in f:
                    raw = raw.strip()
                    if raw:
                        try:
                            yield json.loads(raw)
                        except ValueError:
                            continue  # línea corrupta por corte de energía
        except OSError:
            return

    def pending(self):
        """Itera en orden FIFO los sobres aún no confirmados (sin cargar todo en RAM)."""
        source = self._iter_file() if self.path else list(self._mem)
        for env in source:
            if env.get("seq", 0) > self._acked_seq:
                yield env

    def peek_oldest(self):
        for env in self.pending():
            return env
        return None

    def acknowledge(self, ack_seq):
        """Confirma todos los registros con seq <= ack_seq. Devuelve cuántos se liberaron."""
        ack_seq = int(ack_seq)
        if ack_seq <= self._acked_seq:
            return 0
        self._acked_seq = ack_seq
        before = self._count
        if self.path:
            if ack_seq >= self._seq:
                self._remove_file()
                self._count = 0
            else:
                self._count = sum(1 for e in self._iter_file() if e.get("seq", 0) > ack_seq)
                if before - self._count >= 60:  # compactar cada ~1 h de datos confirmados
                    self._rewrite(list(self.pending()))
        else:
            self._mem = [e for e in self._mem if e["seq"] > ack_seq]
            self._count = len(self._mem)
        return before - self._count

    def _drop_oldest(self, n):
        self.dropped_oldest_count += n
        if self.path:
            keep = []
            skipped = 0
            for env in self.pending():
                if skipped < n:
                    skipped += 1
                    continue
                keep.append(env)
            self._rewrite(keep)
        else:
            self._mem = self._mem[n:]
            self._count = len(self._mem)

    def _rewrite(self, envelopes):
        tmp = self.path + ".tmp"
        with open(tmp, "wb") as f:
            for env in envelopes:
                f.write(encode_line(env))
        self._remove_file()
        os.rename(tmp, self.path)
        self._count = len(envelopes)

    def _remove_file(self):
        try:
            os.remove(self.path)
        except OSError:
            pass
