"""
Pruebas en PC de firmware/movil_supabase/main.py (móvil colgante que consulta Supabase).

Se carga el main.py real con imitaciones de machine, network, urequests y config.
Cada consulta HTTP tarda 1.5 s a propósito, como una conexión TLS nueva en la Pico W,
para comprobar que la animación (núcleo 1) mantiene su velocidad aunque la red bloquee.

Ejecutar:  python -m unittest discover -s firmware -p "test_*.py" -v
"""
from __future__ import annotations

import importlib.util
import json
import statistics
import sys
import threading
import time as real_time
import types
import unittest
from pathlib import Path

MAIN_PATH = Path(__file__).resolve().parent / "movil_supabase" / "main.py"
RED_LENTA_S = 1.5


def _fake_time_module() -> types.ModuleType:
    t = types.ModuleType("time")
    t.ticks_ms = lambda: int(real_time.monotonic() * 1000)
    t.ticks_add = lambda a, b: a + b
    t.ticks_diff = lambda a, b: a - b
    t.sleep_ms = lambda ms: real_time.sleep(ms / 1000)
    t.sleep = real_time.sleep
    return t


class _FakePin:
    OUT = 1

    def __init__(self, ident, mode=None):
        self.ident = ident
        self._v = 0

    def value(self, v=None):
        if v is None:
            return self._v
        self._v = 1 if v else 0

    def on(self):
        self._v = 1

    def off(self):
        self._v = 0

    def toggle(self):
        self._v ^= 1


class _FakeResponse:
    def __init__(self, payload):
        self.status_code = 200
        self.text = json.dumps(payload) if payload is not None else ""

    def json(self):
        return json.loads(self.text)

    def close(self):
        pass


def load_firmware(pending_commands: list[dict] | None = None):
    """Carga main.py con módulos falsos. Devuelve (módulo, registro de cuadros, llamadas RPC)."""
    machine = types.ModuleType("machine")
    machine.Pin = _FakePin

    network = types.ModuleType("network")
    network.STA_IF = 0

    class _WLAN:
        def __init__(self, *_):
            pass

        def active(self, *_):
            return True

        def isconnected(self):
            return True

        def connect(self, *_):
            pass

        def ifconfig(self):
            return ("192.168.0.50",)

    network.WLAN = _WLAN

    rpc_calls: list[tuple[str, dict]] = []
    queue = list(pending_commands or [])
    urequests = types.ModuleType("urequests")

    def post(url, headers=None, data=None):
        real_time.sleep(RED_LENTA_S)          # red lenta: bloquea como el TLS en la Pico W
        name = url.rsplit("/", 1)[-1]
        body = json.loads(data)
        rpc_calls.append((name, body))
        if name == "dispositivo_comandos_pendientes":
            out = list(queue)
            queue.clear()
            return _FakeResponse(out)
        return _FakeResponse(None)

    urequests.post = post

    config = types.ModuleType("config")
    config.WIFI_SSID = "red-prueba"
    config.WIFI_PASS = "x"
    config.SUPABASE_URL = "https://ejemplo.supabase.co"
    config.SUPABASE_ANON_KEY = "sb_publishable_prueba"
    config.CUNA_ID = "cuna-prueba"
    config.TOKEN_DISPOSITIVO = "token-prueba"
    config.CONSULTAR_CADA_MS = 1000

    fakes = {"machine": machine, "network": network, "urequests": urequests, "config": config,
             "time": _fake_time_module()}
    saved = {k: sys.modules.get(k) for k in fakes}
    sys.modules.update(fakes)
    try:
        spec = importlib.util.spec_from_file_location("movil_supabase_main", MAIN_PATH)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
    finally:
        for k, v in saved.items():
            if v is None:
                sys.modules.pop(k, None)
            else:
                sys.modules[k] = v

    frames: list[tuple[float, tuple[int, ...]]] = []
    original_prender = module.prender

    def prender_registrado(numeros):
        frames.append((real_time.monotonic(), tuple(numeros)))
        original_prender(numeros)

    module.prender = prender_registrado
    return module, frames, rpc_calls


def lit_leds(module) -> list[int]:
    return [n for n, led in module.leds.items() if led.value()]


class MovilSupabaseTests(unittest.TestCase):
    def _run_with_blocking_network(self, velocidad: str, seconds: float = 3.2):
        module, frames, _ = load_firmware()
        threading.Thread(target=module.bucle_animacion, daemon=True).start()
        module.iniciar("secuencia", velocidad, 1)
        end = real_time.monotonic() + seconds
        while real_time.monotonic() < end:     # el núcleo 0 queda bloqueado en la red
            module.rpc("dispositivo_comandos_pendientes", {"p_tipos": ["movil_on"]})
        module.detener()
        intervals_ms = [(b[0] - a[0]) * 1000 for a, b in zip(frames, frames[1:])]
        return module, frames, intervals_ms

    def test_each_speed_keeps_its_interval_while_network_blocks(self):
        expected = {"lenta": 600, "media": 300, "rapida": 120}
        medians = {}
        for velocidad, ms in expected.items():
            with self.subTest(velocidad=velocidad):
                _, frames, intervals = self._run_with_blocking_network(velocidad)
                self.assertGreaterEqual(len(intervals), 3, "la animación no avanzó mientras la red bloqueaba")
                medians[velocidad] = statistics.median(intervals)
                self.assertAlmostEqual(medians[velocidad], ms, delta=max(25, ms * 0.15))
        self.assertGreater(medians["lenta"], medians["media"] * 1.6)
        self.assertGreater(medians["media"], medians["rapida"] * 2.0)

    def test_frames_follow_the_pattern_order(self):
        module, frames, _ = load_firmware()
        threading.Thread(target=module.bucle_animacion, daemon=True).start()
        module.iniciar("espiral", "rapida", 1)
        real_time.sleep(1.3)
        module.detener()
        shown = [f[1] for f in frames[:9]]
        self.assertEqual(shown, [tuple(c) for c in module.PATRONES["espiral"]])

    def test_detener_turns_leds_off_quickly(self):
        module, _, _ = load_firmware()
        threading.Thread(target=module.bucle_animacion, daemon=True).start()
        module.iniciar("parpadeo", "lenta", 1)
        real_time.sleep(0.1)
        self.assertEqual(len(lit_leds(module)), 9)
        module.detener()
        real_time.sleep(0.05)
        self.assertEqual(lit_leds(module), [])

    def test_animation_stops_when_duration_ends(self):
        module, _, _ = load_firmware()
        threading.Thread(target=module.bucle_animacion, daemon=True).start()
        module.iniciar("filas", "rapida", 1)
        module.candado.acquire()
        module.fin_ms = module.time.ticks_ms() + 250    # acortar la duración para la prueba
        module.candado.release()
        real_time.sleep(0.5)
        self.assertFalse(module.activo)
        self.assertEqual(lit_leds(module), [])

    def test_new_command_restarts_pattern_from_first_frame(self):
        module, frames, _ = load_firmware()
        threading.Thread(target=module.bucle_animacion, daemon=True).start()
        module.iniciar("secuencia", "rapida", 1)
        real_time.sleep(0.5)
        frames.clear()
        module.iniciar("columnas", "rapida", 1)
        real_time.sleep(0.05)
        module.detener()
        self.assertEqual(frames[0][1], (1, 4, 7))

    def test_commands_are_confirmed_and_invalid_ones_marked_error(self):
        module, _, rpc_calls = load_firmware([
            {"id": "c1", "tipo": "movil_on", "payload": {"patron": "filas", "velocidad": "media", "duracion_min": 3}},
            {"id": "c2", "tipo": "movil_on", "payload": {"patron": "zigzag", "velocidad": "media", "duracion_min": 3}},
            {"id": "c3", "tipo": "movil_off", "payload": {}},
        ])
        module.revisar_comandos()
        confirms = [(b["p_id"], b["p_estado"]) for name, b in rpc_calls if name == "dispositivo_confirmar_comando"]
        self.assertEqual(confirms, [("c1", "confirmado"), ("c2", "error"), ("c3", "confirmado")])
        self.assertFalse(module.activo)


if __name__ == "__main__":
    unittest.main()
