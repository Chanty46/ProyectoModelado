#!/usr/bin/env python3
"""
Ejecutor asíncrono de los 5 simuladores de dispositivos de la Cuna Inteligente (M3).
Habla el protocolo TCP línea-JSON (docs/PROTOCOL.md) contra CunaServer (:9000).
"""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import time
from typing import Any, Dict, List, Optional

try:
    from simulators.protocol_client import (
        CentralEnvRuleEngine,
        MattressRuleEngine,
        OfflineRingBuffer,
        build_envelope,
        compute_backoff_seconds,
    )
except ImportError:
    from protocol_client import (
        CentralEnvRuleEngine,
        MattressRuleEngine,
        OfflineRingBuffer,
        build_envelope,
        compute_backoff_seconds,
    )


DEFAULT_SIM_DEVICES: Dict[str, Dict[str, str]] = {
    "central": {
        "device_id": "sim-central-a",
        "device_type": "central",
        "token_env": "SIM_TOKEN_CENTRAL",
        "default_demo_token": "TOKEN-CENTRAL-A-DEMO-000000000000",
    },
    "colchon": {
        "device_id": "sim-colchon-a",
        "device_type": "colchon",
        "token_env": "SIM_TOKEN_COLCHON",
        "default_demo_token": "TOKEN-COLCHON-A-DEMO-000000000000",
    },
    "movil": {
        "device_id": "sim-movil-a",
        "device_type": "movil",
        "token_env": "SIM_TOKEN_MOVIL",
        "default_demo_token": "TOKEN-MOVIL-A-DEMO-000000000000",
    },
    "biberon": {
        "device_id": "sim-biberon-a",
        "device_type": "biberon",
        "token_env": "SIM_TOKEN_BIBERON",
        "default_demo_token": "TOKEN-BIBERON-A-DEMO-000000000000",
    },
    "juguete": {
        "device_id": "sim-juguete-a",
        "device_type": "juguete",
        "token_env": "SIM_TOKEN_JUGUETE",
        "default_demo_token": "TOKEN-JUGUETE-A-DEMO-000000000000",
    },
}


LOCAL_DEVICES_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "devices.local.json")


def load_local_devices(path: str) -> Dict[str, Dict[str, str]]:
    """Lee `devices.local.json` (ignorado por Git): {tipo: {device_id, token}} de dispositivos emparejados desde la web."""
    if not os.path.isfile(path):
        return {}
    with open(path, encoding="utf-8-sig") as fh:
        data = json.load(fh)
    return {
        kind: entry
        for kind, entry in data.items()
        if kind in DEFAULT_SIM_DEVICES and entry.get("device_id") and entry.get("token")
    }


class DeviceSimulatorNode:
    """Simulador TCP de un nodo Pico W con búfer offline, heartbeat y comandos."""

    def __init__(
        self,
        host: str,
        port: int,
        kind: str,
        once: bool = False,
        telemetry_interval_s: float = 60.0,
        override: Optional[Dict[str, str]] = None,
    ) -> None:
        """Inicializa identidad, token (local/entorno) y motores de reglas del nodo simulado."""
        info = DEFAULT_SIM_DEVICES[kind]
        self.host = host
        self.port = port
        self.kind = kind
        self.device_id = (override or {}).get("device_id") or info["device_id"]
        self.device_type = info["device_type"]
        self.token = (override or {}).get("token") or os.environ.get(info["token_env"], info["default_demo_token"])
        self.once = once
        self.telemetry_interval_s = max(1.0, telemetry_interval_s)
        self.buffer = OfflineRingBuffer()
        self.mattress_engine = MattressRuleEngine()
        self.central_engine = CentralEnvRuleEngine()
        self._cycle = 0

    async def run(self) -> None:
        """Mantiene la conexión contra `host:port` con backoff exponencial hasta 30 s."""
        attempt = 0
        while True:
            try:
                reader, writer = await asyncio.open_connection(self.host, self.port)
                attempt = 0
                await self._handle_session(reader, writer)
                if self.once:
                    return
            except Exception:
                if self.once:
                    raise
                attempt += 1
                await asyncio.sleep(compute_backoff_seconds(attempt))

    async def _send_line(self, writer: asyncio.StreamWriter, line: str) -> None:
        """Envía una línea JSON-UTF8 ya terminada en `\\n` y vacía el flujo TCP."""
        writer.write(line.encode("utf-8"))
        await writer.drain()

    async def _send_command_ack(self, writer: asyncio.StreamWriter, msg: Dict[str, Any]) -> None:
        """Responde `ack ok=True` a cualquier comando entrante durante la sesión."""
        cmd_id = str(msg.get("d", {}).get("id", ""))
        ack_line = build_envelope("ack", self.buffer.next_seq(), {"id": cmd_id, "ok": True})
        await self._send_line(writer, ack_line)

    async def _await_ack_for_seq(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
        seq: int,
    ) -> None:
        """Espera el `ack` de `seq` procesando comandos intercalados que lleguen antes."""
        deadline = time.monotonic() + 5.0
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                return
            resp = await asyncio.wait_for(reader.readline(), timeout=remaining)
            if not resp:
                return
            resp_obj = json.loads(resp.decode("utf-8"))
            msg_type = resp_obj.get("t")
            if msg_type == "command":
                await self._send_command_ack(writer, resp_obj)
                continue
            if msg_type == "ack" and str(resp_obj.get("d", {}).get("id", "")) == str(seq):
                self.buffer.ack_up_to(seq)
                return

    async def _perform_handshake(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
    ) -> None:
        """Envía `hello`, exige `hello_ack` en <= 5 s y sincroniza `seq` y `mass_scale`."""
        hello_line = build_envelope(
            "hello",
            0,
            {
                "device_id": self.device_id,
                "device_type": self.device_type,
                "fw": "1.0.0-sim",
                "token": self.token,
            },
        )
        await self._send_line(writer, hello_line)

        ack_raw = await asyncio.wait_for(reader.readline(), timeout=5.0)
        if not ack_raw:
            raise ConnectionError("Conexión cerrada antes de recibir hello_ack.")

        ack_msg = json.loads(ack_raw.decode("utf-8"))
        if ack_msg.get("t") != "hello_ack" or not ack_msg.get("d", {}).get("session_ok"):
            raise PermissionError(f"Fallo de autenticación hello: {ack_msg}")

        ack_data = ack_msg.get("d", {})
        self.buffer.sync_with_server(ack_data.get("last_seq", 0))
        cfg = ack_data.get("config", {})
        if "mass_scale" in cfg:
            self.mattress_engine.mass_scale = float(cfg["mass_scale"])

    async def _flush_offline_buffer(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
    ) -> None:
        """Reenvía en orden FIFO los mensajes encolados mientras no hubo conexión."""
        for pending in self.buffer.pending_messages():
            line = json.dumps(pending, separators=(",", ":")) + "\n"
            await self._send_line(writer, line)
            await self._await_ack_for_seq(reader, writer, pending["seq"])

    async def _run_live_loop(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
    ) -> None:
        """Mantiene la sesión activa respondiendo comandos, enviando heartbeats y muestras."""
        last_heartbeat = time.time()
        last_telemetry = time.time()
        while True:
            try:
                line = await asyncio.wait_for(reader.readline(), timeout=1.0)
                if not line:
                    break
                msg = json.loads(line.decode("utf-8"))
                if msg.get("t") == "command":
                    await self._send_command_ack(writer, msg)
            except asyncio.TimeoutError:
                pass

            now = time.time()
            if now - last_heartbeat >= 15.0:
                hb = build_envelope("heartbeat", self.buffer.next_seq(), {"uptime_s": int(now), "battery_pct": 95})
                await self._send_line(writer, hb)
                last_heartbeat = now

            if now - last_telemetry >= self.telemetry_interval_s:
                await self._emit_sample_cycle(reader, writer)
                last_telemetry = now

    async def _handle_session(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
    ) -> None:
        """Ejecuta el ciclo completo de una sesión TCP: handshake, búfer, muestra y modo vivo."""
        try:
            await self._perform_handshake(reader, writer)
            await self._flush_offline_buffer(reader, writer)

            hb_line = build_envelope("heartbeat", 0, {"uptime_s": 15, "battery_pct": 95})
            await self._send_line(writer, hb_line)

            await self._emit_sample_cycle(reader, writer)
            if self.once:
                return
            await self._run_live_loop(reader, writer)
        finally:
            writer.close()
            try:
                await writer.wait_closed()
            except Exception:
                pass

    def _build_sample_entry(self) -> Dict[str, Any]:
        """Genera y encola una muestra sintética de telemetría o evento según el nodo."""
        self._cycle += 1
        phase = self._cycle % 4
        if self.device_type == "central":
            temp_c = round(22.3 + phase * 0.15, 2)
            hum_pct = round(49.0 + phase * 0.6, 1)
            self.central_engine.evaluate_env(time.monotonic(), temp_c, hum_pct)
            return self.buffer.enqueue("telemetry", {"temp_c": temp_c, "hum_pct": hum_pct})
        if self.device_type == "colchon":
            now = time.time()
            base_mass = 0.650 + phase * 0.004
            self.mattress_engine.process_sample(now, base_mass)
            self.mattress_engine.process_sample(now + 0.2, base_mass + 0.018)
            telemetry_data = self.mattress_engine.pop_minute_telemetry(round(base_mass + 0.010, 3))
            return self.buffer.enqueue("telemetry", telemetry_data)
        if self.device_type == "biberon":
            return self.buffer.enqueue(
                "telemetry",
                {"mass_g": 145.0, "temp_c": round(36.7 + (phase % 2) * 0.1, 2), "battery_pct": 88},
            )
        if self.device_type == "juguete":
            return self.buffer.enqueue("event", {"kind": "game_end", "hits": 5 + phase, "secs": 18})
        return self.buffer.enqueue(
            "event",
            {"kind": "mobile_session_end", "secs": 120, "pattern": "ola_circular", "speed": "media"},
        )

    async def _emit_sample_cycle(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
    ) -> None:
        """Envía una muestra del dispositivo y espera el `ack` de confirmación del servidor."""
        entry = self._build_sample_entry()
        await self._send_line(writer, json.dumps(entry, separators=(",", ":")) + "\n")
        await self._await_ack_for_seq(reader, writer, entry["seq"])


async def main_async(args: argparse.Namespace) -> None:
    """Lanza en paralelo uno o los cinco simuladores TCP configurados."""
    local = load_local_devices(args.devices_file)
    if args.device != "all":
        kinds: List[str] = [args.device]
    elif local:
        kinds = list(local.keys())
        print(f"Usando dispositivos emparejados de {os.path.basename(args.devices_file)}: {', '.join(kinds)}")
    else:
        kinds = list(DEFAULT_SIM_DEVICES.keys())

    nodes = [
        DeviceSimulatorNode(
            args.host,
            args.port,
            kind,
            once=args.once,
            telemetry_interval_s=args.telemetry_interval,
            override=local.get(kind),
        )
        for kind in kinds
    ]
    await asyncio.gather(*(node.run() for node in nodes))


def main() -> None:
    """Punto de entrada CLI para ejecutar los simuladores de la Cuna Inteligente."""
    parser = argparse.ArgumentParser(description="Simuladores TCP de la Cuna Inteligente")
    parser.add_argument("--host", default=os.environ.get("TCP_HOST", "127.0.0.1"))
    parser.add_argument("--port", type=int, default=int(os.environ.get("TCP_PORT", "9000")))
    parser.add_argument(
        "--device",
        choices=["all", "central", "colchon", "movil", "biberon", "juguete"],
        default="all",
    )
    parser.add_argument("--once", action="store_true", help="Ejecuta un ciclo de conexión y termina")
    parser.add_argument(
        "--telemetry-interval",
        type=float,
        default=60.0,
        help="Intervalo entre envíos de telemetría en modo continuo (s)",
    )
    parser.add_argument(
        "--devices-file",
        default=LOCAL_DEVICES_FILE,
        help="JSON con {tipo: {device_id, token}} de dispositivos emparejados (por defecto simulators/devices.local.json)",
    )
    args = parser.parse_args()
    asyncio.run(main_async(args))


if __name__ == "__main__":
    main()
