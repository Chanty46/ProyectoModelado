"""
Alta de dispositivos simulados "de fábrica" (sin dueño) para la demostración.

Simula lo que haría el fabricante: registra 5 dispositivos (uno por tipo) en
`public.dispositivos` con estado `sin_dueno` y un código de reclamo nuevo, que se
imprime para escribirlo en la web (RF24/RF25). Solo datos de prueba.

Lee SUPABASE_URL y SUPABASE_SECRET_KEY de backend/.env; nunca imprime la clave.

Uso:
    python -I scripts/provision_demo_devices.py            # crea o renueva códigos de los no reclamados
    python -I scripts/provision_demo_devices.py --status   # solo muestra el estado
"""
from __future__ import annotations

import argparse
import hashlib
import json
import secrets
import string
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEMO_DEVICES = [
    ("demo-central-01", "central"),
    ("demo-colchon-01", "colchon"),
    ("demo-movil-01", "movil"),
    ("demo-biberon-01", "biberon"),
    ("demo-juguete-01", "juguete"),
]
CLAIM_ALPHABET = string.ascii_uppercase + string.digits


def load_env(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8-sig").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        values[key.strip()] = value.strip()
    return values


class Rest:
    def __init__(self, base_url: str, secret_key: str) -> None:
        self.base = base_url.rstrip("/") + "/rest/v1/"
        self.headers = {
            "apikey": secret_key,
            "Authorization": f"Bearer {secret_key}",
            "Content-Type": "application/json",
            "Prefer": "return=representation",
        }

    def call(self, method: str, path: str, body: object | None = None) -> list[dict]:
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(self.base + path, data=data, method=method, headers=self.headers)
        try:
            with urllib.request.urlopen(req, timeout=15) as resp:
                text = resp.read().decode() or "[]"
                return json.loads(text)
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode(errors="replace")[:300]
            raise SystemExit(f"Error {exc.code} en {method} {path.split('?')[0]}: {detail}") from None


def new_claim_code() -> str:
    raw = "".join(secrets.choice(CLAIM_ALPHABET) for _ in range(10))
    return f"{raw[:5]}-{raw[5:]}"


def main() -> None:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("--status", action="store_true", help="Solo mostrar el estado")
    args = parser.parse_args()

    env = load_env(ROOT / "backend" / ".env")
    url, key = env.get("SUPABASE_URL", ""), env.get("SUPABASE_SECRET_KEY", "")
    if not url or not key:
        raise SystemExit("Faltan SUPABASE_URL o SUPABASE_SECRET_KEY en backend/.env")
    rest = Rest(url, key)

    familias = rest.call("GET", "familias?select=id,nombre")
    usuarios = rest.call("GET", "usuarios?select=family_id,rol")
    print(f"Base de datos OK: {len(familias)} familias, {len(usuarios)} usuarios con membresía.")

    ids = ",".join(d for d, _ in DEMO_DEVICES)
    existing = {
        row["device_id"]: row
        for row in rest.call(
            "GET",
            f"dispositivos?device_id=in.({urllib.parse.quote(ids)})&select=id,device_id,tipo,cuna_id,estado,reclamo_usado",
        )
    }

    if args.status:
        for device_id, tipo in DEMO_DEVICES:
            row = existing.get(device_id)
            print(f"  {device_id:<17} {tipo:<8} {row['estado'] if row else 'no existe'}")
        return

    print("\nCódigos de reclamo (un solo uso, máx. 5 intentos; escríbelos en 'Agregar dispositivo'):")
    for device_id, tipo in DEMO_DEVICES:
        row = existing.get(device_id)
        if row and (row.get("cuna_id") or row.get("reclamo_usado")):
            print(f"  {device_id:<17} {tipo:<8} ya está vinculado a una cuna (se deja igual)")
            continue
        code = new_claim_code()
        fields = {
            "hash_reclamo": hashlib.sha256(code.encode()).hexdigest(),
            "reclamo_expira_en": None,
            "reclamo_usado": False,
            "intentos_reclamo": 0,
            "hash_token": None,
            "estado": "sin_dueno",
            "fw_version": "1.0.0-sim",
        }
        if row:
            rest.call("PATCH", f"dispositivos?id=eq.{row['id']}", fields)
        else:
            rest.call("POST", "dispositivos", {"device_id": device_id, "tipo": tipo, **fields})
        print(f"  {device_id:<17} {tipo:<8} código: {code}")


if __name__ == "__main__":
    try:
        main()
    except urllib.error.URLError as exc:
        sys.exit(f"No se pudo conectar con Supabase: {exc.reason}")
