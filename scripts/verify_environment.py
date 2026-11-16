#!/usr/bin/env python3
"""Revisa herramientas instaladas, secretos en Git y todas las pruebas del repositorio."""

from __future__ import annotations

import os
import re
import subprocess
import sys
from pathlib import Path

# Códigos ANSI para salida en terminal
GREEN = "\033[92m"
RED = "\033[91m"
YELLOW = "\033[93m"
CYAN = "\033[96m"
BOLD = "\033[1m"
RESET = "\033[0m"

REQUIRED_TOOLS = [
    (["dotnet", "--version"], r"^8\.", ".NET 8 SDK"),
    (["node", "--version"], r"^v(18|20|22|24)", "Node.js (>= 18)"),
    (["npm", "--version"], r"^[0-9]+", "npm"),
    (["python3", "--version"], r"^Python 3\.(1[0-9]|[2-9][0-9])", "Python 3.10+"),
]

# Patrón de detección de secretos reales en el árbol rastreado por Git
FORBIDDEN_SECRET_PATTERN = (
    "(sb_secret_[A-Za-z0-9]{10}|GOCSPX-[A-Za-z0-9]{5}|" + "AAGel" + "Nrz|" + "Pr0y" + "3ct0)"
)


def print_step(title: str) -> None:
    """Imprime el encabezado visual de cada etapa de verificación."""
    print(f"\n{BOLD}{CYAN}==> {title}{RESET}")


def run_command(cmd: list[str], cwd: Path | None = None) -> tuple[int, str]:
    """Ejecuta un subproceso capturando stdout/stderr combinados sin lanzar excepción."""
    try:
        proc = subprocess.run(
            cmd,
            cwd=cwd,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            check=False,
        )
        return proc.returncode, proc.stdout
    except Exception as exc:  # noqa: BLE001 - reporta el fallo al resumen final
        return 1, str(exc)


def _check_system_tools(failures: list[str]) -> None:
    """Verifica que .NET 8, Node.js, npm y Python 3 estén instalados en versiones válidas."""
    print_step("1. Verificando herramientas y versiones del sistema")
    for cmd, pattern, label in REQUIRED_TOOLS:
        code, out = run_command(cmd)
        version_str = out.strip()
        if code == 0 and re.search(pattern, version_str):
            print(f"  [{GREEN}OK{RESET}] {label}: {version_str}")
        else:
            print(f"  [{RED}FALLO{RESET}] {label} no cumple los requisitos (salida: {version_str})")
            failures.append(f"Herramienta {label}")


def _check_security_rules(repo_root: Path, failures: list[str]) -> None:
    """Valida que no existan archivos `.env`/`secrets.py` ni patrones reales en Git."""
    print_step("2. Verificando reglas de seguridad (SECURITY.md)")
    untracked_secret_files = [
        repo_root / "backend" / ".env",
        repo_root / "frontend" / ".env",
        repo_root / "firmware" / "secrets.py",
    ]
    for sec_file in untracked_secret_files:
        if sec_file.exists():
            print(
                f"  [{YELLOW}AVISO{RESET}] Archivo local de secretos presente: "
                f"{sec_file.name} (verificar que no esté en Git)"
            )

    code, git_tracked = run_command(["git", "ls-files", "*.env", "*secrets.py"])
    if code == 0 and git_tracked.strip():
        print(f"  [{RED}CRÍTICO{RESET}] Archivos de secretos rastreados en Git:\n{git_tracked}")
        failures.append("Secretos rastreados en git")
    else:
        print(f"  [{GREEN}OK{RESET}] Ningún secreto rastreado en Git.")

    code, grep_out = run_command([
        "git",
        "grep",
        "-iE",
        FORBIDDEN_SECRET_PATTERN,
        "--",
        ":^scripts/verify_environment.py",
        ":^backend/CunaServer.Tests/EndToEndAndSecurityChecklistTests.cs",
    ])
    if code == 0 and grep_out.strip():
        print(f"  [{RED}CRÍTICO{RESET}] Posibles credenciales reales detectadas en el código:\n{grep_out}")
        failures.append("Patrones de secretos en código")
    else:
        print(f"  [{GREEN}OK{RESET}] Escaneo de código libre de patrones de secretos reales.")


def _run_step_check(
    step_title: str | None,
    cmd: list[str],
    ok_msg: str,
    fail_msg: str,
    fail_label: str,
    failures: list[str],
    cwd: Path | None = None,
    tail_chars: int | None = None,
) -> None:
    """Ejecuta un comando de prueba/compilación e informa su estado en consola."""
    if step_title:
        print_step(step_title)
    code, out = run_command(cmd, cwd=cwd)
    if code == 0:
        print(f"  [{GREEN}OK{RESET}] {ok_msg}")
        return
    detail = out[-tail_chars:] if tail_chars else out
    print(f"  [{RED}FALLO{RESET}] {fail_msg}" + (f":\n{detail}" if detail else ""))
    failures.append(fail_label)


def _print_summary(failures: list[str]) -> int:
    """Muestra el resumen final y devuelve 0 si todo pasó o 1 si hubo fallos."""
    print("\n" + "=" * 65)
    if not failures:
        print(f"{BOLD}{GREEN}✓ TODOS LOS SISTEMAS LISTOS PARA DEMOSTRACIÓN (Hito M11){RESET}")
        print("Puedes arrancar la demostración siguiendo docs/DEMO_GUIDE.md")
        print("=" * 65)
        return 0
    print(f"{BOLD}{RED}✗ SE ENCONTRARON {len(failures)} PROBLEMA(S):{RESET}")
    for failure in failures:
        print(f"  - {failure}")
    print("=" * 65)
    return 1


def main() -> int:
    """Orquesta la verificación integral de herramientas, seguridad, backend, IoT y frontend."""
    repo_root = Path(__file__).resolve().parent.parent
    os.chdir(repo_root)

    print(f"{BOLD}Verificador Integral de Entorno — Cuna Inteligente (M10){RESET}")
    print(f"Directorio raíz: {repo_root}")

    failures: list[str] = []
    _check_system_tools(failures)
    _check_security_rules(repo_root, failures)

    _run_step_check(
        "3. Ejecutando pruebas unitarias y de integración de Backend (.NET 8)",
        ["dotnet", "test", "backend/CunaServer.sln", "--verbosity", "minimal"],
        "Todas las pruebas de CunaServer pasaron con éxito.",
        "Fallaron pruebas en backend",
        "Pruebas .NET backend",
        failures,
        tail_chars=800,
    )
    _run_step_check(
        "4. Ejecutando pruebas de Simuladores TCP (Python 3)",
        ["python3", "-m", "unittest", "discover", "-s", "simulators", "-p", "test_*.py"],
        "Pruebas de protocolo y reglas de simuladores pasaron.",
        "Fallaron pruebas de simuladores",
        "Pruebas simuladores",
        failures,
    )
    _run_step_check(
        "5. Ejecutando pruebas de Firmware MicroPython RP2040",
        ["python3", "-m", "unittest", "discover", "-s", "firmware", "-p", "test_*.py"],
        "Pruebas de HAL, pines y lógica de firmware pasaron.",
        "Fallaron pruebas de firmware",
        "Pruebas firmware",
        failures,
        tail_chars=800,
    )

    frontend_dir = repo_root / "frontend"
    _run_step_check(
        "6. Verificando frontend React (TypeScript + Tests + Vite)",
        ["npm", "test"],
        "Pruebas unitarias de helpers frontend pasaron.",
        "Fallaron pruebas unitarias frontend",
        "Tests frontend",
        failures,
        cwd=frontend_dir,
    )
    _run_step_check(
        None,
        ["npm", "run", "lint"],
        "Verificación estricta de tipos TypeScript completada.",
        "Error en typecheck frontend",
        "Lint frontend",
        failures,
        cwd=frontend_dir,
    )
    _run_step_check(
        None,
        ["npm", "run", "build"],
        "Empaquetado de producción de Vite exitoso.",
        "Error al construir paquete de frontend",
        "Build frontend",
        failures,
        cwd=frontend_dir,
    )
    _run_step_check(
        "7. Verificando cliente Telegram Bot API (simulación HTTP)",
        ["python3", "scripts/test_telegram.py", "--mock"],
        "Prueba mock HTTP Bot API de Telegram completada.",
        "Falló prueba mock de Telegram",
        "Mock Telegram API",
        failures,
    )

    return _print_summary(failures)


if __name__ == "__main__":
    sys.exit(main())
