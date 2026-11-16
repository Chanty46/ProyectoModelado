#!/usr/bin/env python3
"""Herramienta segura de diagnóstico y prueba de la API de Telegram Bot (RF19 / RF38 / RF39).

Nota arquitectónica importante (Telegram Bot API):
Los bots de Telegram NO pueden iniciar conversaciones ni enviar mensajes directamente a un
número telefónico (por ejemplo +506 8000-0000) por restricciones de privacidad de Telegram.
Para recibir notificaciones, el propietario del número debe abrir
https://t.me/CunasInteligentes_Modelado_Bot, enviar /start (o abrir el enlace profundo) y
vincular su Chat ID numérico con /vincular CODIGO.

Uso:
  python3 scripts/test_telegram.py --mock
  python3 scripts/test_telegram.py --probe-network
  python3 scripts/test_telegram.py --chat-id 123456789 --send
"""

from __future__ import annotations

import argparse
import json
import os
import threading
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def _normalize_bot_token(raw: str) -> str:
    """Limpia comillas y el prefijo opcional `bot` si fue copiado desde una URL."""
    token = raw.strip().strip('"').strip("'")
    if token.lower().startswith("bot") and len(token) > 4 and token[3].isdigit() and ":" in token:
        token = token[3:]
    return token


def load_bot_token_from_env_files(repo_root: Path) -> str | None:
    """Lee TELEGRAM_BOT_TOKEN del entorno o de `backend/.env` / `.env` sin imprimirlo jamás."""
    token = os.environ.get("TELEGRAM_BOT_TOKEN", "").strip()
    if token:
        return _normalize_bot_token(token) or None

    for env_path in (repo_root / "backend" / ".env", repo_root / ".env"):
        if not env_path.is_file():
            continue

        for raw_line in env_path.read_text(encoding="utf-8", errors="ignore").splitlines():
            line = raw_line.strip()
            if not line or line.startswith("#"):
                continue
            if line.startswith("export "):
                line = line[len("export "):].lstrip()
            if "=" not in line:
                continue
            key, val = line.split("=", 1)
            if key.strip() == "TELEGRAM_BOT_TOKEN":
                cleaned = _normalize_bot_token(val)
                if cleaned:
                    return cleaned
    return None


def telegram_request(
    base_url: str,
    token: str,
    method: str,
    payload: dict | None = None,
    timeout: float = 8.0,
) -> tuple[int, dict]:
    """Ejecuta una llamada GET/POST JSON hacia la Telegram Bot API sin exponer el token en errores."""
    url = f"{base_url.rstrip('/')}/bot{token}/{method}"
    data = None
    headers = {"Accept": "application/json"}
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"

    req = urllib.request.Request(url, data=data, headers=headers, method="POST" if data else "GET")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            body = json.loads(resp.read().decode("utf-8"))
            return resp.status, body
    except urllib.error.HTTPError as err:
        raw = err.read().decode("utf-8", errors="ignore")
        try:
            body = json.loads(raw)
        except json.JSONDecodeError:
            body = {"ok": False, "description": raw[:200]}
        return err.code, body


def probe_telegram_https_endpoint(timeout: float = 6.0) -> tuple[bool, int | None, str]:
    """Verifica conectividad TLS/HTTPS hacia api.telegram.org sin usar secretos."""
    req = urllib.request.Request("https://api.telegram.org/bot000000:PROBE/getMe", method="GET")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return True, resp.status, "Endpoint HTTPS alcanzable."
    except urllib.error.HTTPError as err:
        # 401/404 confirma que api.telegram.org respondió por TLS correctamente
        return True, err.code, f"Endpoint https://api.telegram.org alcanzable (HTTP {err.code} esperado sin token válido)."
    except Exception as exc:  # noqa: BLE001
        return False, None, f"No se pudo conectar a api.telegram.org: {type(exc).__name__}"


def _build_mock_handler(sent_messages: list[dict]) -> type[BaseHTTPRequestHandler]:
    """Construye el manejador HTTP local que simula `getMe`, `getUpdates` y `sendMessage`."""

    class MockHandler(BaseHTTPRequestHandler):
        def log_message(self, format: str, *args: object) -> None:  # noqa: A002
            return

        def do_GET(self) -> None:  # noqa: N802
            if self.path.endswith("/getMe"):
                self._write_json(
                    200,
                    {"ok": True, "result": {"id": 999001, "is_bot": True, "username": "CunasInteligentes_Modelado_Bot"}},
                )
            elif "/getUpdates" in self.path:
                self._write_json(
                    200,
                    {
                        "ok": True,
                        "result": [
                            {
                                "update_id": 1001,
                                "message": {"chat": {"id": 990001234}, "text": "/start TG-DEMO12345"},
                            }
                        ],
                    },
                )
            else:
                self._write_json(404, {"ok": False, "description": "Not found"})

        def do_POST(self) -> None:  # noqa: N802
            if self.path.endswith("/sendMessage"):
                length = int(self.headers.get("Content-Length", "0"))
                payload = json.loads(self.rfile.read(length).decode("utf-8"))
                sent_messages.append(payload)
                self._write_json(
                    200,
                    {"ok": True, "result": {"message_id": len(sent_messages), "chat": {"id": payload.get("chat_id")}}},
                )
            else:
                self._write_json(404, {"ok": False})

        def _write_json(self, code: int, body: dict) -> None:
            raw = json.dumps(body).encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            self.wfile.write(raw)

    return MockHandler


def run_mock_roundtrip() -> int:
    """Prueba end-to-end contra servidor HTTP mock local de la Bot API (sin token real)."""
    sent_messages: list[dict] = []
    server = ThreadingHTTPServer(("127.0.0.1", 0), _build_mock_handler(sent_messages))
    port = server.server_address[1]
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()

    try:
        base_url = f"http://127.0.0.1:{port}"
        st_me, body_me = telegram_request(base_url, "MOCK-TOKEN", "getMe")
        st_upd, body_upd = telegram_request(base_url, "MOCK-TOKEN", "getUpdates?offset=1&timeout=1")
        chat_id = body_upd["result"][0]["message"]["chat"]["id"]
        st_send, body_send = telegram_request(
            base_url,
            "MOCK-TOKEN",
            "sendMessage",
            {"chat_id": chat_id, "text": "[PRUEBA MOCK] Cuna Inteligente operativa."},
        )
        assert st_me == 200 and body_me["ok"] is True
        assert st_upd == 200 and len(body_upd["result"]) == 1
        assert st_send == 200 and body_send["ok"] is True and len(sent_messages) == 1
        print(f"[OK] Mock Telegram API roundtrip: getMe=@{body_me['result']['username']}, sendMessage chat_id={chat_id}")
        return 0
    finally:
        server.shutdown()
        server.server_close()


def _parse_args() -> argparse.Namespace:
    """Define y analiza los argumentos CLI de diagnóstico de Telegram."""
    parser = argparse.ArgumentParser(description="Diagnóstico seguro de Telegram Bot API para Cuna Inteligente.")
    parser.add_argument("--mock", action="store_true", help="Ejecutar prueba completa contra mock HTTP local.")
    parser.add_argument("--probe-network", action="store_true", help="Verificar alcance HTTPS hacia api.telegram.org.")
    parser.add_argument("--chat-id", type=int, help="Chat ID numérico de Telegram obtenido tras /start o /mi_chat.")
    parser.add_argument("--send", action="store_true", help="Enviar mensaje de prueba al --chat-id usando TELEGRAM_BOT_TOKEN local.")
    parser.add_argument("--delete-webhook", action="store_true", help="Eliminar webhook activo para permitir sondeo getUpdates.")
    parser.add_argument("--message", default="[PRUEBA] Cuna Inteligente: verificación de Bot API.", help="Texto del mensaje de prueba.")
    return parser.parse_args()


def _inspect_bot_and_updates(token: str, delete_webhook: bool) -> tuple[int, list[int]]:
    """Valida `getMe`, revisa webhooks que bloqueen `getUpdates` y lista chats recientes."""
    st_me, me_body = telegram_request("https://api.telegram.org", token, "getMe")
    if st_me != 200 or not me_body.get("ok"):
        print(f"[ERROR] getMe falló con HTTP {st_me} (revisa TELEGRAM_BOT_TOKEN en backend/.env).")
        return 1, []

    bot_username = me_body.get("result", {}).get("username", "desconocido")
    print(f"[OK] Bot autenticado correctamente: @{bot_username}")

    st_wh, wh_body = telegram_request("https://api.telegram.org", token, "getWebhookInfo")
    webhook_url = (wh_body.get("result") or {}).get("url", "") if st_wh == 200 and wh_body.get("ok") else ""
    if webhook_url:
        print(f"[WARN] Webhook activo detectado (bloquea getUpdates): {webhook_url}")
        if delete_webhook:
            st_del, del_body = telegram_request("https://api.telegram.org", token, "deleteWebhook?drop_pending_updates=false")
            print(f"[{'OK' if st_del == 200 and del_body.get('ok') else 'ERROR'}] deleteWebhook ejecutado.")
    elif delete_webhook:
        telegram_request("https://api.telegram.org", token, "deleteWebhook?drop_pending_updates=false")
        print("[OK] Sin webhook activo (deleteWebhook verificado).")

    st_upd, upd_body = telegram_request("https://api.telegram.org", token, "getUpdates?limit=10")
    if st_upd != 200 or not upd_body.get("ok"):
        print(f"[WARN] getUpdates respondió HTTP {st_upd}: {upd_body.get('description', 'sin detalle')}")
    updates = upd_body.get("result", []) if st_upd == 200 and upd_body.get("ok") else []
    known_chats = sorted({
        u["message"]["chat"]["id"]
        for u in updates
        if isinstance(u.get("message"), dict) and isinstance(u["message"].get("chat"), dict)
    })
    print(f"[INFO] Chats recientes visibles en getUpdates: {known_chats if known_chats else 'ninguno'}")
    return 0, known_chats


def _send_test_message(token: str, requested_chat_id: int | None, known_chats: list[int], message: str) -> int:
    """Envía un mensaje de prueba al `chat_id` indicado o al único chat reciente visible."""
    target_chat = requested_chat_id or (known_chats[0] if len(known_chats) == 1 else None)
    if target_chat is None:
        print("[ERROR] Especifica --chat-id numérico (recuerda: Bot API no acepta +506 8000-0000 como destino).")
        return 2

    st_send, send_body = telegram_request(
        "https://api.telegram.org",
        token,
        "sendMessage",
        {"chat_id": target_chat, "text": message},
    )
    if st_send == 200 and send_body.get("ok"):
        print(f"[OK] Mensaje enviado a chat_id={target_chat}.")
        return 0
    print(f"[ERROR] sendMessage falló HTTP {st_send}: {send_body.get('description', 'sin detalle')}")
    return 1


def main() -> int:
    """Punto de entrada CLI para pruebas mock o diagnóstico seguro de Telegram Bot API."""
    args = _parse_args()
    if args.mock:
        return run_mock_roundtrip()

    repo_root = Path(__file__).resolve().parent.parent
    reachable, _, detail = probe_telegram_https_endpoint()
    print(f"[{'OK' if reachable else 'ERROR'}] Red HTTPS api.telegram.org: {detail}")

    token = load_bot_token_from_env_files(repo_root)
    if args.probe_network and not token:
        return 0 if reachable else 1

    if not token:
        print("[INFO] TELEGRAM_BOT_TOKEN no está configurado en entorno ni en backend/.env (modo demo InMemory).")
        print("[INFO] Telegram Bot API NO permite enviar directo a +506 8000-0000 por número telefónico;")
        print("       el destinatario debe abrir @CunasInteligentes_Modelado_Bot, enviar /start y vincular su Chat ID.")
        return 0

    status, known_chats = _inspect_bot_and_updates(token, args.delete_webhook)
    if status != 0:
        return status

    if args.send:
        return _send_test_message(token, args.chat_id, known_chats, args.message)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
