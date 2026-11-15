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
from typing import Any, Dict, List
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


class DeviceSimulatorNode:
    def __init__(
        self,
        host: str,
        port: int,
        kind: str,
        once: bool = False,
        telemetry_interval_s: float = 60.0,
    ) -> None:
        info = DEFAULT_SIM_DEVICES[kind]
        self.host = host
        self.port = port
        self.kind = kind
        self.device_id = info["device_id"]
        self.device_type = info["device_type"]
        self.token = os.environ.get(info["token_env"], info["default_demo_token"])
        self.once = once
        self.telemetry_interval_s = max(1.0, telemetry_interval_s)
        self.buffer = OfflineRingBuffer()
        self.mattress_engine = MattressRuleEngine()
        self.central_engine = CentralEnvRuleEngine()
        self._cycle = 0

    async def run(self) -> None:
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

    async def _send_command_ack(self, writer: asyncio.StreamWriter, msg: Dict[str, Any]) -> None:
        cmd_id = str(msg.get("d", {}).get("id", ""))
        ack_line = build_envelope("ack", self.buffer.next_seq(), {"id": cmd_id, "ok": True})
        writer.write(ack_line.encode("utf-8"))
        await writer.drain()

    async def _await_ack_for_seq(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
        seq: int,
    ) -> None:
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

    async def _handle_session(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
    ) -> None:
        try:
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
            writer.write(hello_line.encode("utf-8"))
            await writer.drain()

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

            # Reenviar búfer offline pendiente en orden
            for pending in self.buffer.pending_messages():
                line = json.dumps(pending, separators=(",", ":")) + "\n"
                writer.write(line.encode("utf-8"))
                await writer.drain()
                await self._await_ack_for_seq(reader, writer, pending["seq"])

            # Enviar heartbeat y una muestra de telemetría/evento según el tipo de dispositivo
            hb_line = build_envelope("heartbeat", 0, {"uptime_s": 15, "battery_pct": 95})
            writer.write(hb_line.encode("utf-8"))
            await writer.drain()

            await self._emit_sample_cycle(reader, writer)
            if self.once:
                return

            # Modo continuo (demostración en vivo): mantiene la sesión activa, responde comandos y emite heartbeats
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
                    writer.write(hb.encode("utf-8"))
                    await writer.drain()
                    last_heartbeat = now

                if now - last_telemetry >= self.telemetry_interval_s:
                    await self._emit_sample_cycle(reader, writer)
                    last_telemetry = now
        finally:
            writer.close()
            try:
                await writer.wait_closed()
            except Exception:
                pass

    async def _emit_sample_cycle(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
    ) -> None:
        self._cycle += 1
        phase = self._cycle % 4
        if self.device_type == "central":
            temp_c = round(22.3 + phase * 0.15, 2)
            hum_pct = round(49.0 + phase * 0.6, 1)
            self.central_engine.evaluate_env(time.monotonic(), temp_c, hum_pct)
            entry = self.buffer.enqueue("telemetry", {"temp_c": temp_c, "hum_pct": hum_pct})
        elif self.device_type == "colchon":
            now = time.time()
            base_mass = 0.650 + phase * 0.004
            self.mattress_engine.process_sample(now, base_mass)
            self.mattress_engine.process_sample(now + 0.2, base_mass + 0.018)
            telemetry_data = self.mattress_engine.pop_minute_telemetry(round(base_mass + 0.010, 3))
            entry = self.buffer.enqueue("telemetry", telemetry_data)
        elif self.device_type == "biberon":
            entry = self.buffer.enqueue(
                "telemetry",
                {"mass_g": 145.0, "temp_c": round(36.7 + (phase % 2) * 0.1, 2), "battery_pct": 88},
            )
        elif self.device_type == "juguete":
            entry = self.buffer.enqueue("event", {"kind": "game_end", "hits": 5 + phase, "secs": 18})
        else:  # movil
            entry = self.buffer.enqueue(
                "event",
                {"kind": "mobile_session_end", "secs": 120, "pattern": "ola_circular", "speed": "media"},
            )

        writer.write((json.dumps(entry, separators=(",", ":")) + "\n").encode("utf-8"))
        await writer.drain()
        await self._await_ack_for_seq(reader, writer, entry["seq"])


async def main_async(args: argparse.Namespace) -> None:
    kinds: List[str] = (
        list(DEFAULT_SIM_DEVICES.keys()) if args.device == "all" else [args.device]
    )
    nodes = [
        DeviceSimulatorNode(
            args.host,
            args.port,
            k,
            once=args.once,
            telemetry_interval_s=args.telemetry_interval,
        )
        for k in kinds
    ]
    await asyncio.gather(*(n.run() for n in nodes))


def main() -> None:
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
    args = parser.parse_args()
    asyncio.run(main_async(args))


if __name__ == "__main__":
    main()
