"""
Suite de pruebas unitarias y de integración para el firmware MicroPython (Hito M9).
Ejecutable en PC con Python 3 estándar (CPython).
"""

import os
import socket
import sys
import threading
import time
import unittest

# Asegura que firmware/ se encuentre en el path
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FIRMWARE_DIR = os.path.join(REPO_ROOT, "firmware")
if FIRMWARE_DIR not in sys.path:
    sys.path.insert(0, FIRMWARE_DIR)

from biberon.main import BiberonNode
from central.main import CentralNode
from colchon.main import ColchonNode
from common.pins import (
    BIBERON_PINS,
    CENTRAL_PINS,
    COLCHON_PINS,
    JUGUETE_PINS,
    MOVIL_PINS,
)
from common.protocol_and_buffer import (
    MAX_48H_RECORDS,
    OfflineBuffer,
    ServerClock,
    decode_line,
    encode_line,
    reconnect_backoff_s,
)
from juguete.main import JugueteNode
from movil.main import MovilNode


class FirmwareMicroPythonM9Tests(unittest.TestCase):
    def test_01_pins_defined_solely_in_pins_py_with_valid_rp2040_gpio(self):
        """Verifica que todos los GPIOs estén entre 0 y 28 (rango válido RP2040)."""
        all_configs = (CENTRAL_PINS, COLCHON_PINS, MOVIL_PINS, BIBERON_PINS, JUGUETE_PINS)
        for cfg in all_configs:
            for k, val in cfg.items():
                if isinstance(val, int) and "HZ" not in k and "ID" not in k and "ADDR" not in k:
                    self.assertTrue(0 <= val <= 28, f"Pin {k}={val} fuera de rango RP2040")
                elif isinstance(val, (tuple, list)):
                    for pin in val:
                        self.assertTrue(0 <= pin <= 28, f"Pin en {k} fuera de rango RP2040")
        self.assertEqual(len(MOVIL_PINS["LED_PINS"]), 9)
        self.assertEqual(len(JUGUETE_PINS["BUTTON_PINS"]), 5)
        self.assertEqual(len(JUGUETE_PINS["LED_PINS"]), 5)

    def test_02_circular_buffer_capacity_seq_and_reconnect_flush(self):
        """Capacidad 2880 registros (48 h), seq monótono, confirmación ack."""
        buf = OfflineBuffer(max_records=MAX_48H_RECORDS)
        for i in range(MAX_48H_RECORDS + 20):
            buf.enqueue("telemetry", {"mass_kg": 0.5, "i": i}, ts=1700000000 + i)

        self.assertEqual(buf.pending_count, MAX_48H_RECORDS)
        self.assertEqual(buf.dropped_oldest_count, 20)
        oldest = buf.peek_oldest()
        self.assertIsNotNone(oldest)
        self.assertEqual(oldest["seq"], 21)

        liberados = buf.acknowledge(30)
        self.assertEqual(liberados, 10)
        self.assertEqual(buf.pending_count, MAX_48H_RECORDS - 10)

        # Backoff exponencial 1..30 s
        self.assertEqual(reconnect_backoff_s(0), 1)
        self.assertEqual(reconnect_backoff_s(2), 4)
        self.assertEqual(reconnect_backoff_s(8), 30)

    def test_03_central_node_env_10s_ramp_intercom_and_touch(self):
        """RF07/08 (10 s fuera de rango), RF09 (rampa luz), RF11 (audio PCM) y RF05 (botón táctil)."""
        clock = ServerClock(lambda: 1000)
        clock.sync(1762560000)
        node = CentralNode(clock=clock)

        # Inyecta temperatura alta (30.0 °C > max 26.0 °C)
        node.env_sensor.inject_mock_reading(30.0, 50.0)

        # 9 segundos no deben emitir evento
        for s in range(9):
            node.tick(now_ms=1000 + s * 1000)
            self.assertEqual(node.buffer.pending_count, 0)

        # Segundo 10: emite env_out_of_range
        node.tick(now_ms=1000 + 10000)
        self.assertEqual(node.buffer.pending_count, 1)
        ev = node.buffer.peek_oldest()
        self.assertEqual(ev["t"], "event")
        self.assertEqual(ev["d"]["kind"], "env_out_of_range")
        self.assertEqual(ev["d"]["cause"], "temp_high")

        # Rampa de luz suave no bloqueante (RF09)
        ok, _ = node.handle_command("light_set", {"on": True, "level": 80, "ramp_ms": 1500}, now_ms=12000)
        self.assertTrue(ok)
        node.tick(now_ms=12750)  # mitad de la rampa
        self.assertAlmostEqual(node.lamp_level, 40.0, delta=1.0)
        node.tick(now_ms=13500)  # fin de la rampa
        self.assertEqual(node.lamp_level, 80.0)

        # Botón táctil TTP223 -> button_attention (RF05)
        node.touch_pin.inject_mock_value(1)
        node.tick(now_ms=14000)
        p = [e for e in node.buffer.pending() if e["t"] == "event" and e["d"].get("kind") == "button_attention"]
        self.assertEqual(len(p), 1)

    def test_04_colchon_node_removal_1s_and_movement_index_5hz(self):
        """D14/RF04 (retiro <= 1 s con mass_scale=0.2), RF06 (índice 5 Hz) y RF29 (ventana 30 s)."""
        node = ColchonNode(mass_scale=0.2)
        # 1. Colocar muñeco de 0.65 kg (presencia > 0.20 kg y >= 0.50 kg)
        node.load_cell.inject_mock_kg(0.65)
        for _ in range(5):
            node.tick()
        self.assertTrue(node._was_present)

        # 2. Retiro súbito a 0.05 kg en el siguiente tick (200 ms <= 1.0 s)
        node.load_cell.inject_mock_kg(0.05)
        node.tick()
        removals = [e for e in node.buffer.pending() if e["d"].get("kind") == "removal"]
        self.assertEqual(len(removals), 1)
        self.assertFalse(node._was_present)

        # 3. Movimiento sostenido durante 30 s (150 ticks a 5 Hz)
        node.load_cell.inject_mock_kg(0.60)
        node.tick()
        high_emitted = False
        for i in range(180):
            # Alterna pequeñas variaciones válidas por encima de noise_floor
            m = 0.62 if (i % 2 == 0) else 0.60
            node.load_cell.inject_mock_kg(m)
            node.tick()
            if any(e["d"].get("kind") == "movement_high" for e in node.buffer.pending()):
                high_emitted = True
                break
        self.assertTrue(high_emitted)

    def test_05_movil_biberon_and_juguete_nodes(self):
        """RF13/33 (móvil y session_end), RF15/37 (biberón) y RF34/35/36 (juguete)."""
        # Móvil
        movil = MovilNode()
        ok, _ = movil.handle_command("mobile_play", {"pattern": "ola_circular", "speed": "rapida", "duration_s": 2}, now_ms=1000)
        self.assertTrue(ok)
        self.assertTrue(movil.is_active)
        movil.tick(now_ms=1500)
        movil.tick(now_ms=3100)  # Expiró duración
        self.assertFalse(movil.is_active)
        end_ev = [e for e in movil.buffer.pending() if e["d"].get("kind") == "mobile_session_end"]
        self.assertEqual(len(end_ev), 1)

        # Biberón
        biberon = BiberonNode()
        biberon.load_cell.inject_mock_kg(0.150)  # 150 g >= 20 g
        biberon.ir_temp.inject_mock_temp_c(37.0)
        biberon.tick(now_ms=5000)
        placed_ev = [e for e in biberon.buffer.pending() if e["d"].get("kind") == "bottle_placed"]
        self.assertEqual(len(placed_ev), 1)
        self.assertEqual(placed_ev[0]["d"]["mass_g"], 150.0)

        # Juguete: mecánica RF34 (acierto), RF35 (fallo con game_end) y RF36 (batería < 10 %)
        juguete = JugueteNode()
        target = juguete.current_target
        is_hit, _ = juguete._handle_press(target, now_ms=6000)
        self.assertTrue(is_hit)
        self.assertEqual(juguete.hits, 1)

        # Presionar botón incorrecto (fallo)
        curr_target = juguete.current_target
        wrong = (curr_target + 1) % len(juguete.buttons)
        is_hit2, _ = juguete._handle_press(wrong, now_ms=7000)
        self.assertFalse(is_hit2)
        self.assertEqual(juguete.hits, 0)
        game_ends = [e for e in juguete.buffer.pending() if e["d"].get("kind") == "game_end"]
        self.assertEqual(len(game_ends), 1)
        self.assertEqual(game_ends[0]["d"]["hits"], 1)

        # Batería baja < 10 %
        juguete.battery_adc.inject_mock_pct(8)
        juguete.tick(now_ms=8000)
        low_bat = [e for e in juguete.buffer.pending() if e["d"].get("kind") == "battery_low"]
        self.assertEqual(len(low_bat), 1)
        self.assertEqual(low_bat[0]["d"]["battery_pct"], 8)

    def test_06_tcp_drain_with_server_ack(self):
        """Simula sesión TCP local donde el nodo vacía telemetría y recibe confirmaciones `ack`."""
        central = CentralNode()
        central.emit_telemetry({"temp_c": 22.4, "hum_pct": 52.1})
        central.emit_telemetry({"temp_c": 22.6, "hum_pct": 51.9})
        self.assertEqual(central.buffer.pending_count, 2)

        received_seqs = []

        def echo_server(sock):
            conn, _ = sock.accept()
            with conn:
                reader = conn.makefile("rb")
                for _ in range(2):
                    line = reader.readline()
                    env = decode_line(line)
                    received_seqs.append(env["seq"])
                    ack = encode_line({"v": 1, "t": "ack", "seq": env["seq"], "ts": 1762560000, "d": {"id": str(env["seq"]), "ok": True}})
                    conn.sendall(ack)

        srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        srv.bind(("127.0.0.1", 0))
        srv.listen(1)
        port = srv.getsockname()[1]

        t = threading.Thread(target=echo_server, args=(srv,), daemon=True)
        t.start()

        with socket.create_connection(("127.0.0.1", port), timeout=3.0) as cli:
            reader = cli.makefile("rb")
            for env in list(central.buffer.pending()):
                cli.sendall(encode_line(env))
                ack_line = reader.readline()
                ack_msg = decode_line(ack_line)
                ack_id = int(ack_msg["d"]["id"])
                central.buffer.acknowledge(ack_id)

        srv.close()
        t.join(timeout=1.0)

        self.assertEqual(received_seqs, [1, 2])
        self.assertEqual(central.buffer.pending_count, 0)

    def test_07_central_song_accepts_freq_and_freq_hz_keys(self):
        """Verifica compatibilidad de notas entre Postgres ('freq') y repositorio en memoria ('freq_hz')."""
        central = CentralNode()
        ok, err = central.handle_command(
            "song_play",
            {"action": "play", "song": "brahms", "duration_s": 30, "notes": [{"freq": 330, "ms": 300}, {"freq_hz": 392, "ms": 600}]},
            now_ms=1000,
        )
        self.assertTrue(ok)
        self.assertIsNone(err)
        self.assertEqual(central.song_notes, [(330, 300), (392, 600)])


if __name__ == "__main__":
    unittest.main()
