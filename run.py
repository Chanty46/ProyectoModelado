#!/usr/bin/env python3
"""Lanzador unificado multiplataforma (Windows y Linux) para Cuna Inteligente.

Uso rápido:
  python3 run.py              # Arranca Backend (.NET 8) + 5 Simuladores + Web (:5173) y abre el navegador
  python3 run.py --test       # Ejecuta toda la suite de pruebas (verify_environment.py)
  python3 run.py --no-simulators  # Arranca solo Backend + Web (cuando uses placas Pico W reales)
"""

from __future__ import annotations

import argparse
import os
import shutil
import signal
import socket
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
import webbrowser
from pathlib import Path
from typing import Dict, List, Optional


REPO_ROOT = Path(__file__).resolve().parent
FRONTEND_DIR = REPO_ROOT / "frontend"
BACKEND_CSPROJ = REPO_ROOT / "backend" / "CunaServer" / "CunaServer.csproj"


def build_runtime_env() -> Dict[str, str]:
    """Prepara PATH y DOTNET_ROOT detectando instalaciones locales de .NET 8 en Windows/Linux."""
    env = os.environ.copy()
    path_sep = ";" if os.name == "nt" else ":"
    extra_dirs: List[str] = []

    home = Path.home()
    dotnet_home = home / ".dotnet"
    if dotnet_home.is_dir():
        extra_dirs.append(str(dotnet_home))
        env.setdefault("DOTNET_ROOT", str(dotnet_home))

    local_bin = home / ".local" / "bin"
    if local_bin.is_dir():
        extra_dirs.append(str(local_bin))

    if os.name == "nt":
        program_files_dotnet = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "dotnet"
        if program_files_dotnet.is_dir():
            extra_dirs.append(str(program_files_dotnet))

    if extra_dirs:
        current_path = env.get("PATH", "")
        env["PATH"] = path_sep.join(extra_dirs + ([current_path] if current_path else []))

    return env


def resolve_executable(name: str, env: Dict[str, str]) -> Optional[str]:
    """Resuelve ejecutables en PATH incluyendo `.cmd`/`.exe` en Windows."""
    found = shutil.which(name, path=env.get("PATH"))
    if found:
        return found
    if os.name == "nt":
        for suffix in (".cmd", ".exe", ".bat"):
            found = shutil.which(f"{name}{suffix}", path=env.get("PATH"))
            if found:
                return found
    return None


def is_port_in_use(port: int) -> bool:
    """Comprueba si un puerto TCP local ya está escuchando en IPv4 o IPv6."""
    for family, host in ((socket.AF_INET, "127.0.0.1"), (socket.AF_INET6, "::1")):
        try:
            with socket.socket(family, socket.SOCK_STREAM) as sock:
                sock.settimeout(0.4)
                if sock.connect_ex((host, port)) == 0:
                    return True
        except OSError:
            pass
    return False


def wait_for_http(urls: List[str], proc: subprocess.Popen, timeout_s: float) -> bool:
    """Espera hasta que alguna URL HTTP responda (< 500) o el proceso termine."""
    deadline = time.monotonic() + timeout_s
    while time.monotonic() < deadline:
        if proc.poll() is not None:
            return False
        for url in urls:
            try:
                with urllib.request.urlopen(url, timeout=1.5) as resp:
                    if resp.status < 500:
                        return True
            except urllib.error.HTTPError as err:
                if err.code < 500:
                    return True
            except Exception:
                pass
        time.sleep(0.4)
    return False


def stream_output(prefix: str, proc: subprocess.Popen) -> None:
    """Reenvía la salida estándar de un subproceso con prefijo legible."""
    if proc.stdout is None:
        return
    try:
        for line in proc.stdout:
            clean = line.rstrip("\r\n")
            if clean:
                print(f"[{prefix}] {clean}", flush=True)
    except Exception:
        pass


def spawn_service(cmd: List[str], cwd: Path, env: Dict[str, str], prefix: str) -> subprocess.Popen:
    """Inicia un subproceso en su propio grupo de procesos para cierre limpio en Windows/Linux."""
    kwargs: Dict[str, object] = {
        "cwd": str(cwd),
        "env": env,
        "stdout": subprocess.PIPE,
        "stderr": subprocess.STDOUT,
        "text": True,
        "encoding": "utf-8",
        "errors": "replace",
        "bufsize": 1,
    }
    if os.name == "nt":
        kwargs["creationflags"] = subprocess.CREATE_NEW_PROCESS_GROUP
    else:
        kwargs["start_new_session"] = True

    proc = subprocess.Popen(cmd, **kwargs)  # type: ignore[arg-type]
    thread = threading.Thread(target=stream_output, args=(prefix, proc), daemon=True)
    thread.start()
    return proc


def stop_processes(procs: List[subprocess.Popen]) -> None:
    """Detiene de forma ordenada todos los subprocesos y árboles hijos en Windows o Linux."""
    for proc in reversed(procs):
        if proc.poll() is not None:
            continue
        try:
            if os.name == "nt":
                subprocess.run(
                    ["taskkill", "/PID", str(proc.pid), "/T", "/F"],
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                    check=False,
                )
            else:
                os.killpg(proc.pid, signal.SIGTERM)
        except Exception:
            try:
                proc.terminate()
            except Exception:
                pass

    deadline = time.monotonic() + 3.0
    for proc in procs:
        if proc.poll() is not None:
            continue
        remaining = max(0.1, deadline - time.monotonic())
        try:
            proc.wait(timeout=remaining)
        except Exception:
            try:
                if os.name != "nt":
                    os.killpg(proc.pid, signal.SIGKILL)
                else:
                    proc.kill()
            except Exception:
                pass


def read_env_hint(env_path: Path, key: str) -> str:
    """Lee un valor no secreto (como TELEGRAM_BOT_USERNAME) o si un campo tiene texto."""
    if not env_path.is_file():
        return ""
    for raw in env_path.read_text(encoding="utf-8", errors="ignore").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        k, v = line.split("=", 1)
        if k.strip() == key:
            return v.strip().strip('"').strip("'")
    return ""


def ensure_frontend_dependencies(npm_bin: str, env: Dict[str, str]) -> int:
    """Instala dependencias npm de `frontend/` automáticamente si falta `node_modules`."""
    if (FRONTEND_DIR / "node_modules").is_dir():
        return 0
    print("[SETUP] Instalando dependencias del frontend (npm install)...")
    res = subprocess.run([npm_bin, "install"], cwd=str(FRONTEND_DIR), env=env, check=False)
    return res.returncode


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Lanzador simple (Windows/Linux) para Cuna Inteligente.")
    parser.add_argument("--test", action="store_true", help="Ejecuta todas las pruebas del repositorio.")
    parser.add_argument("--no-simulators", action="store_true", help="No iniciar los simuladores Python (para usar hardware real).")
    parser.add_argument("--no-browser", action="store_true", help="No abrir automáticamente el navegador.")
    parser.add_argument("--telemetry-interval", type=float, default=4.0, help="Intervalo en segundos entre envíos de telemetría simulada.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    env = build_runtime_env()

    if args.test:
        return subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts" / "verify_environment.py")],
            cwd=str(REPO_ROOT),
            env=env,
            check=False,
        ).returncode

    dotnet_bin = resolve_executable("dotnet", env)
    npm_bin = resolve_executable("npm", env)
    if not dotnet_bin:
        print("[ERROR] No se encontró el SDK de .NET 8 (`dotnet`). Instálalo y vuelve a intentar.", file=sys.stderr)
        return 1
    if not npm_bin:
        print("[ERROR] No se encontró `npm` / Node.js (>= 18). Instálalo y vuelve a intentar.", file=sys.stderr)
        return 1

    if ensure_frontend_dependencies(npm_bin, env) != 0:
        print("[ERROR] Falló `npm install` en frontend/.", file=sys.stderr)
        return 1

    busy_ports = [p for p in (5080, 9000, 5173) if is_port_in_use(p)]
    if busy_ports:
        print(f"[ERROR] Los puertos {busy_ports} ya están ocupados. Cierra instancias previas antes de arrancar.", file=sys.stderr)
        return 1

    procs: List[subprocess.Popen] = []

    def handle_sig(_signum: int, _frame: object) -> None:
        raise KeyboardInterrupt

    signal.signal(signal.SIGINT, handle_sig)
    if hasattr(signal, "SIGTERM"):
        signal.signal(signal.SIGTERM, handle_sig)

    try:
        print("==> Iniciando Backend C# (.NET 8) en :5080 y TCP :9000...")
        backend_proc = spawn_service(
            [dotnet_bin, "run", "--project", str(BACKEND_CSPROJ)],
            cwd=REPO_ROOT,
            env=env,
            prefix="BACKEND",
        )
        procs.append(backend_proc)

        if not wait_for_http(["http://127.0.0.1:5080/health", "http://localhost:5080/health"], backend_proc, timeout_s=60.0):
            print("[ERROR] El backend no respondió en http://localhost:5080/health.", file=sys.stderr)
            stop_processes(procs)
            return 1

        if not args.no_simulators:
            print(f"==> Iniciando 5 Simuladores IoT (telemetría cada {args.telemetry_interval:.0f} s)...")
            sim_proc = spawn_service(
                [
                    sys.executable,
                    "-m",
                    "simulators.run_all",
                    "--host",
                    "127.0.0.1",
                    "--port",
                    "9000",
                    "--telemetry-interval",
                    str(args.telemetry_interval),
                ],
                cwd=REPO_ROOT,
                env=env,
                prefix="SIM",
            )
            procs.append(sim_proc)

        print("==> Iniciando Frontend React + Vite en http://localhost:5173...")
        frontend_proc = spawn_service(
            [npm_bin, "run", "dev"],
            cwd=FRONTEND_DIR,
            env=env,
            prefix="WEB",
        )
        procs.append(frontend_proc)

        if not wait_for_http(["http://localhost:5173", "http://127.0.0.1:5173", "http://[::1]:5173"], frontend_proc, timeout_s=30.0):
            print("[ERROR] El servidor web no respondió en http://localhost:5173.", file=sys.stderr)
            stop_processes(procs)
            return 1

        backend_env_file = REPO_ROOT / "backend" / ".env"
        has_tg_token = bool(os.environ.get("TELEGRAM_BOT_TOKEN") or read_env_hint(backend_env_file, "TELEGRAM_BOT_TOKEN"))
        tg_username = read_env_hint(backend_env_file, "TELEGRAM_BOT_USERNAME") or "CunaIntBot"

        print("\n" + "=" * 68)
        print("CUNA INTELIGENTE — LISTO PARA PRUEBAS Y PRESENTACIÓN")
        print("  Web UI:        http://localhost:5173")
        print("  API Health:    http://localhost:5080/health")
        print("  Usuario demo:  Padre  |  Contraseña: 123")
        print(f"  Telegram Bot:  @{tg_username.lstrip('@')} ({'HTTP API activo' if has_tg_token else 'Modo simulación InMemory sin token'})")
        print("  Presiona Ctrl+C para detener todos los servicios.")
        print("=" * 68 + "\n", flush=True)

        if not args.no_browser:
            webbrowser.open("http://localhost:5173")

        while True:
            if backend_proc.poll() is not None:
                print("[ERROR] El proceso del backend se detuvo inesperadamente.", file=sys.stderr)
                return 1
            if frontend_proc.poll() is not None:
                print("[ERROR] El servidor web Vite se detuvo inesperadamente.", file=sys.stderr)
                return 1
            time.sleep(1.0)

    except KeyboardInterrupt:
        print("\n==> Deteniendo servicios de Cuna Inteligente...")
        return 0
    finally:
        stop_processes(procs)


if __name__ == "__main__":
    raise SystemExit(main())
