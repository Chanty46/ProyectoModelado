#!/usr/bin/env python3
"""Configurador interactivo multiplataforma (Windows/Linux) de archivos locales ignorados por Git.

Reemplaza a los scripts heredados de PowerShell para guardar de forma oculta:
  1. `backend/.env` y `frontend/.env` (`--env`)
  2. `simulators/devices.local.json` (`--sim-tokens`)
"""

from __future__ import annotations

import argparse
import getpass
import json
import shutil
import urllib.error
import urllib.request
from pathlib import Path
from typing import Dict


REPO_ROOT = Path(__file__).resolve().parent.parent
BACKEND_ENV = REPO_ROOT / "backend" / ".env"
BACKEND_EXAMPLE = REPO_ROOT / "backend" / ".env.example"
FRONTEND_ENV = REPO_ROOT / "frontend" / ".env"
FRONTEND_EXAMPLE = REPO_ROOT / "frontend" / ".env.example"
SIM_LOCAL_FILE = REPO_ROOT / "simulators" / "devices.local.json"

DEMO_DEVICES = (
    ("central", "demo-central-01"),
    ("colchon", "demo-colchon-01"),
    ("movil", "demo-movil-01"),
    ("biberon", "demo-biberon-01"),
    ("juguete", "demo-juguete-01"),
)


def set_env_value(file_path: Path, key: str, value: str) -> None:
    """Actualiza o agrega una variable en un `.env` local sin imprimir su valor."""
    if not value.strip():
        return
    lines = file_path.read_text(encoding="utf-8").splitlines() if file_path.is_file() else []
    updated = False
    for idx, line in enumerate(lines):
        if line.lstrip().startswith(f"{key}="):
            lines[idx] = f"{key}={value.strip()}"
            updated = True
            break
    if not updated:
        lines.append(f"{key}={value.strip()}")
    file_path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def has_env_prefix(file_path: Path, key: str, prefix: str) -> bool:
    """Comprueba si una clave existe con el prefijo esperado sin exponer el secreto."""
    if not file_path.is_file():
        return False
    for line in file_path.read_text(encoding="utf-8").splitlines():
        if line.startswith(f"{key}={prefix}") and len(line.strip()) > len(f"{key}={prefix}"):
            return True
    return False


def read_env_map(file_path: Path) -> Dict[str, str]:
    """Carga un archivo `.env` como diccionario sin exponer secretos."""
    result: Dict[str, str] = {}
    if not file_path.is_file():
        return result
    for raw in file_path.read_text(encoding="utf-8-sig").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        result[key.strip()] = value.strip()
    return result


def verify_supabase_connection() -> bool:
    """Comprueba conectividad, clave secreta/pública, Email Auth y esquema RLS en Supabase."""
    back = read_env_map(BACKEND_ENV)
    front = read_env_map(FRONTEND_ENV)
    supa_url = (back.get("SUPABASE_URL") or front.get("VITE_SUPABASE_URL") or "").rstrip("/")
    secret_key = back.get("SUPABASE_SECRET_KEY", "")
    pub_key = front.get("VITE_SUPABASE_PUBLISHABLE_KEY", "")

    if not supa_url:
        print("[ERROR] Falta SUPABASE_URL en backend/.env o frontend/.env.")
        return False

    ok = True
    if not secret_key:
        print("[AVISO] backend/.env no tiene SUPABASE_SECRET_KEY (el backend seguirá en modo InMemory).")
        ok = False
    else:
        headers = {"apikey": secret_key}
        if not secret_key.startswith("sb_") and secret_key.count(".") == 2:
            headers["Authorization"] = f"Bearer {secret_key}"
        req = urllib.request.Request(
            f"{supa_url}/rest/v1/familias?select=id&limit=1",
            headers=headers,
        )
        try:
            with urllib.request.urlopen(req, timeout=10) as resp:
                if resp.status == 200:
                    print("[OK] Supabase PostgREST y esquema SQL (tabla familias) verificados.")
        except urllib.error.HTTPError as err:
            body = err.read().decode(errors="replace")
            if err.code == 404 or "PGRST205" in body:
                print(
                    "[AVISO] Supabase conectado, pero faltan las tablas: ejecuta "
                    "supabase/migrations/20261107200000_m1_esquema_inicial_rls.sql en el SQL Editor."
                )
            else:
                print(f"[ERROR] Supabase PostgREST devolvió HTTP {err.code} (revisa SUPABASE_SECRET_KEY).")
            ok = False
        except urllib.error.URLError as err:
            print(f"[ERROR] No se pudo conectar con Supabase ({err.reason}).")
            ok = False

    if not pub_key:
        print("[AVISO] frontend/.env no tiene VITE_SUPABASE_PUBLISHABLE_KEY (solo acceso local Padre/123 disponible).")
        ok = False
    else:
        req_auth = urllib.request.Request(
            f"{supa_url}/auth/v1/settings",
            headers={"apikey": pub_key},
        )
        try:
            with urllib.request.urlopen(req_auth, timeout=10) as resp:
                data = json.loads(resp.read().decode(errors="replace"))
                email_on = bool(data.get("external", {}).get("email", False))
                print(
                    f"[OK] Supabase Auth accesible (proveedor Email/Contraseña activo: {'sí' if email_on else 'no'})."
                )
        except urllib.error.HTTPError as err:
            print(f"[ERROR] Supabase Auth rechazó VITE_SUPABASE_PUBLISHABLE_KEY (HTTP {err.code}).")
            ok = False
        except urllib.error.URLError as err:
            print(f"[ERROR] No se pudo verificar Supabase Auth ({err.reason}).")
            ok = False

    return ok


def configure_env_files() -> None:
    """Solicita claves ocultas y las escribe en `backend/.env` y `frontend/.env`."""
    for target, template in ((BACKEND_ENV, BACKEND_EXAMPLE), (FRONTEND_ENV, FRONTEND_EXAMPLE)):
        if not target.exists() and template.exists():
            shutil.copyfile(template, target)

    secret = getpass.getpass("SUPABASE_SECRET_KEY (sb_secret_... / service_role, Enter = omitir): ")
    telegram = getpass.getpass("TELEGRAM_BOT_TOKEN (Enter = omitir): ")
    publishable = getpass.getpass("VITE_SUPABASE_PUBLISHABLE_KEY (sb_publishable_... / anon, Enter = omitir): ")

    set_env_value(BACKEND_ENV, "SUPABASE_SECRET_KEY", secret)
    set_env_value(BACKEND_ENV, "TELEGRAM_BOT_TOKEN", telegram)
    set_env_value(FRONTEND_ENV, "VITE_SUPABASE_PUBLISHABLE_KEY", publishable)

    print(f"backend secret key guardada: {bool(read_env_map(BACKEND_ENV).get('SUPABASE_SECRET_KEY'))}")
    print(f"frontend publishable key guardada: {bool(read_env_map(FRONTEND_ENV).get('VITE_SUPABASE_PUBLISHABLE_KEY'))}")


def configure_simulator_tokens() -> None:
    """Guarda en `simulators/devices.local.json` los tokens mostrados una sola vez en la web."""
    current: Dict[str, Dict[str, str]] = {}
    if SIM_LOCAL_FILE.is_file():
        current = json.loads(SIM_LOCAL_FILE.read_text(encoding="utf-8-sig"))

    for kind, device_id in DEMO_DEVICES:
        token = getpass.getpass(f"Token de {device_id} ({kind}) [Enter = conservar/omitir]: ").strip()
        if token:
            current[kind] = {"device_id": device_id, "token": token}

    SIM_LOCAL_FILE.write_text(json.dumps(current, indent=2) + "\n", encoding="utf-8")
    print(f"Guardados {len(current)} dispositivo(s) en {SIM_LOCAL_FILE.name}.")


def main() -> int:
    parser = argparse.ArgumentParser(description="Configuración local segura (Windows y Linux).")
    parser.add_argument("--env", action="store_true", help="Configurar claves en backend/.env y frontend/.env")
    parser.add_argument("--sim-tokens", action="store_true", help="Configurar tokens en simulators/devices.local.json")
    parser.add_argument("--verify-supabase", action="store_true", help="Verificar conexión, Email Auth y tablas en Supabase")
    args = parser.parse_args()

    if not args.env and not args.sim_tokens and not args.verify_supabase:
        configure_env_files()
        verify_supabase_connection()
        return 0
    if args.env:
        configure_env_files()
    if args.sim_tokens:
        configure_simulator_tokens()
    if args.verify_supabase:
        return 0 if verify_supabase_connection() else 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
