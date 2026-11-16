"""
Clase base de los 5 nodos y cliente de red (Wi-Fi + TCP) común.

Flujo (docs/PROTOCOL.md):
  Wi-Fi -> TCP -> `hello` (token) -> `hello_ack` en <= 5 s (hora, config, last_seq)
  -> envío del búfer offline en orden -> heartbeat cada 15 s -> comandos con `ack`.
  Si se cae: reconexión 1, 2, 4 ... 30 s. Los sensores siguen midiendo y guardando
  en el búfer mientras no hay conexión (limitación aceptada: la alarma la decide el servidor).

Nunca se imprime el token ni la contraseña del Wi-Fi.
"""

try:
    import uasyncio as asyncio  # type: ignore
except ImportError:
    import asyncio

from common.hal import IS_MICROPYTHON, WatchdogAdapter, ticks_diff, ticks_ms
from common.pins import WDT_TIMEOUT_MS
from common.protocol_and_buffer import (
    HEARTBEAT_INTERVAL_S,
    HELLO_TIMEOUT_S,
    OfflineBuffer,
    ServerClock,
    build_ack,
    build_envelope,
    decode_line,
    encode_line,
    parse_command,
    reconnect_backoff_s,
)

FW_VERSION = "1.0.0"
RESEND_AFTER_MS = 10000  # si no llega `ack`, se reenvía lo pendiente


class DeviceNode:
    """Lógica de un dispositivo: sensores/actuadores, reglas locales y comandos."""

    device_type = ""
    tick_interval_ms = 1000

    def __init__(self, buffer=None, clock=None):
        """Asocia búfer offline y reloj sincronizable del servidor."""
        self.buffer = buffer if buffer is not None else OfflineBuffer()
        self.clock = clock if clock is not None else ServerClock(ticks_ms)
        self.started_ms = ticks_ms()

    def ts(self):
        """Devuelve la marca de tiempo Unix actual del reloj sincronizado (o None)."""
        return self.clock.now()

    def emit_event(self, kind, **fields):
        """Encola un evento discreto en el búfer circular con `seq` monotónico."""
        data = {"kind": kind}
        data.update(fields)
        return self.buffer.enqueue("event", data, self.ts())

    def emit_telemetry(self, data):
        """Encola una muestra agregada de telemetría en el búfer circular."""
        return self.buffer.enqueue("telemetry", data, self.ts())

    def apply_config(self, config):
        """Parámetros recibidos en `hello_ack.config`."""

    def handle_command(self, cmd, params):
        """Procesa un comando recibido del servidor y devuelve `(ok, error)`."""
        return False, "unknown_cmd"

    def heartbeat_data(self):
        """Construye la carga útil periódica del mensaje `heartbeat`."""
        return {"uptime_s": ticks_diff(ticks_ms(), self.started_ms) // 1000}

    def tick(self, now_ms=None):
        """Se llama cada `tick_interval_ms` haya o no conexión."""


async def _connect_wifi(ssid, password, wdt, timeout_s=20):
    """Conecta la interfaz STA de la Pico W alimentando el watchdog durante la espera."""
    import network  # type: ignore

    wlan = network.WLAN(network.STA_IF)
    wlan.active(True)
    if wlan.isconnected():
        return True
    wlan.connect(ssid, password)
    start = ticks_ms()
    while not wlan.isconnected():
        wdt.feed()
        if ticks_diff(ticks_ms(), start) > timeout_s * 1000:
            return False
        await asyncio.sleep_ms(250)
    return True


class DeviceClient:
    """Sesión TCP de un nodo contra el servidor C#."""

    def __init__(self, node, host, port, device_id, device_token, wdt=None):
        """Prepara el cliente TCP para un `DeviceNode` con credenciales locales."""
        self.node = node
        self.host = host
        self.port = int(port)
        self.device_id = device_id
        self._token = device_token
        self.wdt = wdt if wdt is not None else WatchdogAdapter(enabled=False)
        self.connected = False
        self._sent_up_to = 0
        self._last_ack_ms = 0

    async def _send(self, writer, envelope):
        """Codifica un sobre JSON-línea (<= 4 KB) y vacía el socket TCP."""
        writer.write(encode_line(envelope))
        await writer.drain()

    async def _handshake(self, reader, writer):
        """Ejecuta `hello` y valida `hello_ack` en <= 5 s, sincronizando reloj y `seq`."""
        hello = build_envelope(
            "hello",
            self.node.buffer.next_seq(),
            self.node.ts(),
            {
                "device_id": self.device_id,
                "device_type": self.node.device_type,
                "fw": FW_VERSION,
                "token": self._token,
            },
        )
        await self._send(writer, hello)
        raw = await asyncio.wait_for(reader.readline(), HELLO_TIMEOUT_S)
        if not raw:
            raise OSError("conexion_cerrada")
        msg = decode_line(raw)
        data = msg.get("d", {})
        if msg["t"] != "hello_ack" or not data.get("session_ok"):
            raise OSError("hello_rechazado")
        if data.get("server_time") is not None:
            self.node.clock.sync(data["server_time"])
        self.node.apply_config(data.get("config") or {})
        self.node.buffer.sync_with_server(data.get("last_seq", 0))
        # Si aún quedan pendientes tras la sincronización, se reenvían desde el inicio
        self._sent_up_to = self.node.buffer.last_seq if self.node.buffer.pending_count == 0 else 0
        self._last_ack_ms = ticks_ms()

    async def _handle_command_msg(self, msg, writer):
        """Despacha un `command` al nodo y responde con el sobre `ack` correspondiente."""
        cmd_id, cmd, params = parse_command(msg)
        try:
            ok, error = self.node.handle_command(cmd, params)
        except (ValueError, TypeError, KeyError):
            ok, error = False, "params_invalidos"
        ack = build_ack(self.node.buffer.next_seq(), self.node.ts(), cmd_id, ok, error)
        await self._send(writer, ack)

    def _handle_ack_msg(self, msg):
        """Libera mensajes confirmados del búfer circular cuando llega un `ack` numérico."""
        ack_id = str(msg.get("d", {}).get("id", ""))
        if ack_id.isdigit():
            self.node.buffer.acknowledge(int(ack_id))
            self._last_ack_ms = ticks_ms()

    async def handle_incoming(self, msg, writer):
        """Enruta un mensaje recibido del servidor (`command`, `ack` o `error`)."""
        msg_type = msg.get("t")
        if msg_type == "command":
            await self._handle_command_msg(msg, writer)
        elif msg_type == "ack":
            self._handle_ack_msg(msg)
        elif msg_type == "error":
            code = msg.get("d", {}).get("code", "")
            print("[cuna] error del servidor:", code)

    async def _reader_loop(self, reader, writer):
        """Lee mensajes JSON-línea entrantes hasta que el servidor cierre la conexión."""
        while True:
            raw = await reader.readline()
            if not raw:
                raise OSError("conexion_cerrada")
            try:
                msg = decode_line(raw)
            except ValueError:
                continue
            await self.handle_incoming(msg, writer)

    async def _flush_pending_messages(self, writer):
        """Envía registros pendientes en orden y reintenta si venció `RESEND_AFTER_MS`."""
        buf = self.node.buffer
        if buf.pending_count and ticks_diff(ticks_ms(), self._last_ack_ms) > RESEND_AFTER_MS:
            self._sent_up_to = 0
            self._last_ack_ms = ticks_ms()
        for env in buf.pending():
            if env["seq"] > self._sent_up_to:
                await self._send(writer, env)
                self._sent_up_to = env["seq"]

    async def _maybe_send_heartbeat(self, writer, last_hb_ms):
        """Envía `heartbeat` cada `HEARTBEAT_INTERVAL_S` y devuelve el timestamp actualizado."""
        if ticks_diff(ticks_ms(), last_hb_ms) < HEARTBEAT_INTERVAL_S * 1000:
            return last_hb_ms
        hb = build_envelope(
            "heartbeat",
            self.node.buffer.next_seq(),
            self.node.ts(),
            self.node.heartbeat_data(),
        )
        await self._send(writer, hb)
        return ticks_ms()

    async def _sender_loop(self, writer):
        """Bucle saliente: vacía el búfer offline en orden y envía heartbeats periódicos."""
        last_hb_ms = ticks_ms()
        while True:
            await self._flush_pending_messages(writer)
            last_hb_ms = await self._maybe_send_heartbeat(writer, last_hb_ms)
            await asyncio.sleep(0.2)

    async def _close_writer_quietly(self, writer):
        """Cierra el socket saliente sin ocultar excepciones previas de la sesión."""
        try:
            writer.close()
            await writer.wait_closed()
        except Exception:  # noqa: BLE001 - cierre best-effort
            pass

    async def run_session(self):
        """Abre una sesión TCP, completa el handshake y mantiene lector/escritor."""
        reader, writer = await asyncio.wait_for(asyncio.open_connection(self.host, self.port), 10)
        try:
            await self._handshake(reader, writer)
            self.connected = True
            print("[cuna] sesion TCP activa:", self.node.device_type)
            reader_task = asyncio.create_task(self._reader_loop(reader, writer))
            try:
                await self._sender_loop_until(reader_task, writer)
            finally:
                reader_task.cancel()
        finally:
            self.connected = False
            await self._close_writer_quietly(writer)

    async def _sender_loop_until(self, reader_task, writer):
        """Ejecuta el bucle de envío hasta que falle la lectura o la escritura."""
        sender_task = asyncio.create_task(self._sender_loop(writer))
        try:
            while not reader_task.done() and not sender_task.done():
                await asyncio.sleep(0.2)
            for task in (reader_task, sender_task):
                if task.done():
                    task.result()  # propaga la excepción de desconexión
        finally:
            sender_task.cancel()


async def _sensor_loop(node, wdt):
    """Ejecuta `node.tick()` de forma periódica e invoca `gc.collect()` cada 30 ciclos."""
    ticks_count = 0
    while True:
        wdt.feed()
        node.tick()
        ticks_count += 1
        if ticks_count >= 30:
            import gc

            gc.collect()
            ticks_count = 0
        await asyncio.sleep(node.tick_interval_ms / 1000)


async def _network_loop(client, secrets_mod):
    """Mantiene la conexión Wi-Fi/TCP con backoff exponencial (1..30 s) ante caídas."""
    attempt = 0
    while True:
        try:
            if IS_MICROPYTHON:
                if not await _connect_wifi(secrets_mod.WIFI_SSID, secrets_mod.WIFI_PASSWORD, client.wdt):
                    raise OSError("wifi_no_disponible")
            await client.run_session()
            attempt = 0
        except Exception as exc:  # noqa: BLE001 - cualquier fallo de red -> reintento
            print("[cuna] desconectado:", type(exc).__name__)
            import gc

            gc.collect()
        await asyncio.sleep(reconnect_backoff_s(attempt))
        attempt += 1


def run(node, secrets_mod):
    """Punto de entrada en la Pico: sensores + red en paralelo con watchdog."""
    wdt = WatchdogAdapter(WDT_TIMEOUT_MS)
    client = DeviceClient(
        node,
        secrets_mod.SERVER_IP,
        getattr(secrets_mod, "SERVER_PORT", 9000),
        secrets_mod.DEVICE_ID,
        secrets_mod.DEVICE_TOKEN,
        wdt,
    )

    async def _main():
        asyncio.create_task(_sensor_loop(node, wdt))
        await _network_loop(client, secrets_mod)

    asyncio.run(_main())
