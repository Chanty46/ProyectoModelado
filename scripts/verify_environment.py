#!/usr/bin/env python3
"""
Script de verificación y preparación del entorno para Cuna Inteligente (Hito M10).
Ejecuta la batería completa de comprobaciones de herramientas, seguridad y pruebas
antes de la defensa o demostración del proyecto.
"""

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


def print_step(title: str) -> None:
    print(f"\n{BOLD}{CYAN}==> {title}{RESET}")


def run_command(cmd: list[str], cwd: Path | None = None) -> tuple[int, str]:
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
    except Exception as exc:
        return 1, str(exc)


def main() -> int:
    repo_root = Path(__file__).resolve().parent.parent
    os.chdir(repo_root)

    print(f"{BOLD}Verificador Integral de Entorno — Cuna Inteligente (M10){RESET}")
    print(f"Directorio raíz: {repo_root}")

    failures: list[str] = []

    # 1. Comprobar herramientas requeridas
    print_step("1. Verificando herramientas y versiones del sistema")

    tools = [
        (["dotnet", "--version"], r"^8\.", ".NET 8 SDK"),
        (["node", "--version"], r"^v(18|20|22|24)", "Node.js (>= 18)"),
        (["npm", "--version"], r"^[0-9]+", "npm"),
        (["python3", "--version"], r"^Python 3\.(1[0-9]|[2-9][0-9])", "Python 3.10+"),
    ]

    for cmd, pattern, label in tools:
        code, out = run_command(cmd)
        version_str = out.strip()
        if code == 0 and re.search(pattern, version_str):
            print(f"  [{GREEN}OK{RESET}] {label}: {version_str}")
        else:
            print(f"  [{RED}FALLO{RESET}] {label} no cumple los requisitos (salida: {version_str})")
            failures.append(f"Herramienta {label}")

    # 2. Verificación estricta de seguridad contra secretos
    print_step("2. Verificando reglas de seguridad (SECURITY.md)")

    untracked_secret_files = [
        repo_root / "backend" / ".env",
        repo_root / "frontend" / ".env",
        repo_root / "firmware" / "secrets.py",
    ]
    for sec_file in untracked_secret_files:
        if sec_file.exists():
            print(f"  [{YELLOW}AVISO{RESET}] Archivo local de secretos presente: {sec_file.name} (verificar que no esté en Git)")

    code, git_tracked = run_command(["git", "ls-files", "*.env", "*secrets.py"])
    if code == 0 and git_tracked.strip():
        print(f"  [{RED}CRÍTICO{RESET}] Archivos de secretos rastreados en Git:\n{git_tracked}")
        failures.append("Secretos rastreados en git")
    else:
        print(f"  [{GREEN}OK{RESET}] Ningún secreto rastreado en Git.")

    # Escaneo de patrones de tokens
    forbidden_pattern = "(sb_secret_[A-Za-z0-9]{10}|GOCSPX-[A-Za-z0-9]{5}|" + "AAGel" + "Nrz|" + "Pr0y" + "3ct0)"
    code, grep_out = run_command([
        "git", "grep", "-iE", forbidden_pattern, "--",
        ":^scripts/verify_environment.py",
        ":^backend/CunaServer.Tests/EndToEndAndSecurityChecklistTests.cs"
    ])
    if code == 0 and grep_out.strip():
        print(f"  [{RED}CRÍTICO{RESET}] Posibles credenciales reales detectadas en el código:\n{grep_out}")
        failures.append("Patrones de secretos en código")
    else:
        print(f"  [{GREEN}OK{RESET}] Escaneo de código libre de patrones de secretos reales.")

    # 3. Pruebas Backend .NET
    print_step("3. Ejecutando pruebas unitarias y de integración de Backend (.NET 8)")
    code, out = run_command(["dotnet", "test", "backend/CunaServer.sln", "--verbosity", "minimal"])
    if code == 0:
        print(f"  [{GREEN}OK{RESET}] Todas las pruebas de CunaServer pasaron con éxito.")
    else:
        print(f"  [{RED}FALLO{RESET}] Fallaron pruebas en backend:\n{out[-800:]}")
        failures.append("Pruebas .NET backend")

    # 4. Pruebas de Simuladores Python
    print_step("4. Ejecutando pruebas de Simuladores TCP (Python 3)")
    code, out = run_command(["python3", "-m", "unittest", "discover", "-s", "simulators", "-p", "test_*.py"])
    if code == 0:
        print(f"  [{GREEN}OK{RESET}] Pruebas de protocolo y reglas de simuladores pasaron.")
    else:
        print(f"  [{RED}FALLO{RESET}] Fallaron pruebas de simuladores:\n{out}")
        failures.append("Pruebas simuladores")

    # 5. Pruebas de Firmware MicroPython
    print_step("5. Ejecutando pruebas de Firmware MicroPython RP2040")
    code, out = run_command(["python3", "-m", "unittest", "discover", "-s", "firmware", "-p", "test_*.py"])
    if code == 0:
        print(f"  [{GREEN}OK{RESET}] Pruebas de HAL, pines y lógica de firmware pasaron.")
    else:
        print(f"  [{RED}FALLO{RESET}] Fallaron pruebas de firmware")
        failures.append("Pruebas firmware")

    # 6. Typecheck y Build de Frontend React
    print_step("6. Verificando frontend React (TypeScript + Vite)")
    code, out = run_command(["npm", "run", "lint"], cwd=repo_root / "frontend")
    if code == 0:
        print(f"  [{GREEN}OK{RESET}] Verificación estricta de tipos TypeScript completada.")
    else:
        print(f"  [{RED}FALLO{RESET}] Error en typecheck frontend:\n{out}")
        failures.append("Lint frontend")

    code, out = run_command(["npm", "run", "build"], cwd=repo_root / "frontend")
    if code == 0:
        print(f"  [{GREEN}OK{RESET}] Empaquetado de producción de Vite exitoso.")
    else:
        print(f"  [{RED}FALLO{RESET}] Error al construir paquete de frontend:\n{out}")
        failures.append("Build frontend")

    # Resumen final
    print("\n" + "=" * 65)
    if not failures:
        print(f"{BOLD}{GREEN}✓ TODOS LOS SISTEMAS LISTOS PARA DEMOSTRACIÓN (Hito M10){RESET}")
        print("Puedes arrancar la demostración siguiendo docs/DEMO_GUIDE.md")
        print("=" * 65)
        return 0
    else:
        print(f"{BOLD}{RED}✗ SE ENCONTRARON {len(failures)} PROBLEMA(S):{RESET}")
        for f in failures:
            print(f"  - {f}")
        print("=" * 65)
        return 1


if __name__ == "__main__":
    sys.exit(main())
