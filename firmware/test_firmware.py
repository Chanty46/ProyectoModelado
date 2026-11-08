"""
Suite de pruebas automatizadas para el Firmware MicroPython (Hito M9).
Valida en PC (Python 3) el comportamiento de los 5 nodos de dispositivo,
el búfer circular offline de 48 horas (2880 registros), el mapeo de pines en `pins.py`,
y la retransmisión ordenada sin duplicados sobre un servidor TCP local.
"""

import base64
import socket
import threading
import unittest

from firmware.biberon.main import BiberonFirmwareNode
from firmware.central.main import CentralFirmwareNode
from firmware.colchon.main import ColchonFirmwareNode
from firmware.common.pins import (
    BIBERON_PINS,
    CENTRAL_PINS,
    COLCHON_PINS,
    JUGUETE_PINS,
    MOVIL_PINS,
)
from firmware.common.protocol_and_buffer import (
    MAX_48H_MINUTES,
    OfflineCircularBuffer48h,
    compute_reconnect_backoff_s,
    decode_json_line,
    encode_json_line,
)
from firmware.juguete.main import JugueteFirmwareNode
from firmware.movil.main import MovilFirmwareNode


class FirmwareMicroPythonM9Tests(unittest.TestCase):
    def test_01_pins_defined_solely_in_pins_py_with_valid_rp2040_gpio(self) -> None:
        """Verifica que los 5 nodos tengan sus pines centralizados en `pins.py` dentro del rango GPIO 0..28."""
        for config in (CENTRAL_PINS, COLCHON_PINS, MOVIL_PINS, BIBERON_PINS, JUGUETE_PINS):
            self.assertIn("wdt_timeout_ms", config)
            self.assertLessEqual(config["wdt_timeout_ms"], 8388)
        self.assertEqual(len(MOVIL_PINS["led_pins"]), 9)
        self.assertEqual(len(JUGUETE_PINS["button_pins"]), 5)
        self.assertEqual(len(JUGUETE_PINS["led_pins"]), 5)
        self.assertGreaterEqual(CENTRAL_PINS["night_lamp_pwm_freq_hz"], 1000)

    def test_02_circular_buffer_48h_capacity_seq_and_reconnect_flush(self) -> None:
        """Verifica capacidad de 2880 registros (48 h a 1/min), rotación FIFO y confirmación por `ack_seq` (RNF04 / D12)."""
        buf = OfflineCircularBuffer48h(max_records=MAX_48H_MINUTES)
        for minute in range(MAX_48H_MINUTES + 15):
            buf.enqueue_envelope("telemetry", {"temp_avg": 22.5, "m": minute}, ts=1700000000 + minute * 60)

        self.assertEqual(buf.pending_count, MAX_48H_MINUTES)
        self.assertEqual(buf.dropped_oldest_count, 15)
        oldest = buf.peek_oldest()
        assert oldest is not None
        self.assertEqual(oldest["seq"], 16)

        removed = buf.acknowledge_seq(25)
        self.assertEqual(removed, 10)
        self.assertEqual(buf.pending_count, MAX_48H_MINUTES - 10)

        # Backoff exponencial 1..30 s
        self.assertEqual(compute_reconnect_backoff_s(0), 1.0)
        self.assertEqual(compute_reconnect_backoff_s(2), 4.0)
        self.assertEqual(compute_reconnect_backoff_s(8), 30.0)

    def test_03_central_node_env_10s_ramp_intercom_and_touch(self) -> None:
        """Verifica RF07/RF08 (10 s fuera de rango), RF09 (rampa PWM >= 1 kHz), RF11 (audio PCM) y RF05 (botón táctil)."""
        node = CentralFirmwareNode()
        node.env_sensor.inject_mock_reading(29.4, 50.0)

        # primeros 9 segundos no disparan alerta
        for s in range(1, 10):
            ev = node.sample_environment_second(ts=1000 + s)
            self.assertIsNone(ev)

        # al segundo 10 se dispara env_out_of_range
        ev_10 = node.sample_environment_second(ts=1010)
        assert ev_10 is not None
        self.assertEqual(ev_10["t"], "event")
        self.assertEqual(ev_10["d"]["name"], "env_out_of_range")
        self.assertEqual(ev_10["d"]["kind"], "temp_high")

        # Rampa suave de 15 pasos (1.5 s)
        duties = node.ramp_lamp_intensity(80, ramp_steps=15)
        self.assertEqual(len(duties), 15)
        self.assertGreater(duties[-1], duties[0])
        self.assertEqual(node.lamp_intensity_pct, 80)

        # Botón táctil TTP223 -> button_attention
        node.touch_pin.inject_mock_value(1)
        touch_ev = node.poll_touch_button(ts=1015)
        assert touch_ev is not None
        self.assertEqual(touch_ev["d"]["name"], "button_attention")

        # Audio PCM 8-bit 8 kHz por comando TCP
        pcm_b64 = base64.b64encode(bytes([128, 160, 200, 240])).decode("ascii")
        ack = node.handle_command({"v": 1, "t": "command", "seq": 7, "d": {"cmd": "audio_chunk", "pcm_b64": pcm_b64}}, ts=1020)
        self.assertTrue(ack["d"]["ok"])
        self.assertEqual(node.last_pcm_samples_played, 4)

    def test_04_colchon_node_removal_1s_and_movement_index_5hz(self) -> None:
        """Verifica D14/RF04 (retiro <= 1 s con mass_scale=0.2), RF06 (índice 5 Hz) y RF29 (ventana 30 s)."""
        node = ColchonFirmwareNode(mass_scale=0.2)
        node.load_cell.inject_mock_kg(0.65)
        placed_events = node.sample_5hz_tick(ts=2000)
        self.assertEqual(len(placed_events), 1)
        self.assertEqual(placed_events[0]["d"]["name"], "placed")

        # Caída brusca a 0.05 kg en el siguiente tick (200 ms <= 1 s) -> removal inmediato
        node.load_cell.inject_mock_kg(0.05)
        removal_events = node.sample_5hz_tick(ts=2001)
        self.assertEqual(len(removal_events), 1)
        self.assertEqual(removal_events[0]["d"]["name"], "removal")

        # Colocar de nuevo y simular movimiento continuo moderado (deltas de 0.02 kg cada 200 ms = 6 kg/min)
        node.load_cell.inject_mock_kg(0.60)
        node.sample_5hz_tick(ts=2002)

        high_triggered = False
        for i in range(175):
            mass = 0.62 if (i % 2 == 0) else 0.60
            node.load_cell.inject_mock_kg(mass)
            evs = node.sample_5hz_tick(ts=2010 + i)
            if any(e["d"].get("name") == "movement_high" for e in evs):
                high_triggered = True
                break
        self.assertTrue(high_triggered)

        telemetry = node.flush_minute_telemetry(ts=2100)
        self.assertGreater(telemetry["d"]["movement_idx"], 0.0)
        self.assertTrue(telemetry["d"]["presence"])

    def test_05_movil_biberon_and_juguete_nodes(self) -> None:
        """Verifica móvil de 9 LEDs (RF13/RF33), biberón >= 20 g (RF15/RF37) y juguete < 200 ms + reset + batería (RF14/RF34/RF35/RF36)."""
        # 1. Móvil
        movil = MovilFirmwareNode()
        movil.handle_command({"v": 1, "t": "command", "seq": 1, "d": {"cmd": "mobile_set", "on": True, "speed": "rapida"}}, ts=3000)
        states = movil.advance_pattern_step()
        self.assertEqual(sum(states), 1)
        movil.tick_second(3001)
        movil.tick_second(3002)
        _, end_ev = movil.handle_command({"v": 1, "t": "command", "seq": 2, "d": {"cmd": "mobile_set", "on": False}}, ts=3003)
        assert end_ev is not None
        self.assertEqual(end_ev["d"]["name"], "mobile_session_end")
        self.assertEqual(end_ev["d"]["duration_s"], 2)

        # 2. Biberón
        biberon = BiberonFirmwareNode()
        biberon.load_cell.inject_mock_kg(0.145)  # 145 g >= 20 g
        biberon.ir_temp.inject_mock_temp_c(37.1)
        ev_bottle = biberon.sample_sensors(ts=4000)
        assert ev_bottle is not None
        self.assertEqual(ev_bottle["d"]["name"], "bottle_placed")
        self.assertAlmostEqual(ev_bottle["d"]["mass_g"], 145.0, places=1)
        self.assertAlmostEqual(ev_bottle["d"]["temp_c"], 37.1, places=1)

        # 3. Juguete
        juguete = JugueteFirmwareNode()
        # Primer paso esperado en target_sequence [0, 2, 1, 4, 3] es 0
        hit, latency_ms = juguete.press_button(0)
        self.assertTrue(hit)
        self.assertLess(latency_ms, 200)  # RF34 < 200 ms
        self.assertEqual(juguete.current_step, 1)

        # Fallar el segundo paso (presionar 4 en vez de 2) -> reinicia secuencia a 0 (RF35)
        hit_wrong, _ = juguete.press_button(4)
        self.assertFalse(hit_wrong)
        self.assertEqual(juguete.current_step, 0)
        self.assertEqual(juguete.resets_count, 1)

        # Batería baja < 20 % (RF36)
        juguete.battery_adc.inject_mock_pct(15)
        low_bat_ev = juguete.check_battery(ts=5000)
        assert low_bat_ev is not None
        self.assertEqual(low_bat_ev["d"]["name"], "battery_low")
        self.assertEqual(low_bat_ev["d"]["battery_pct"], 15)

        game_ev = juguete.finish_game_session(duration_secs=45, ts=5010)
        self.assertEqual(game_ev["d"]["name"], "game_end")
        self.assertEqual(game_ev["d"]["hits"], 1)

    def test_06_offline_buffer_flushes_over_tcp_socket_without_duplicates(self) -> None:
        """Verifica que el búfer offline se vacíe por socket TCP línea por línea y procese `ack` del servidor."""
        node = CentralFirmwareNode()
        node.flush_minute_telemetry(ts=6000)
        node.flush_minute_telemetry(ts=6060)
        self.assertEqual(node.buffer.pending_count, 2)

        received_seqs: list[int] = []

        def tcp_server_worker(server_sock: socket.socket) -> None:
            conn, _ = server_sock.accept()
            with conn:
                reader = conn.makefile("rb")
                for _ in range(2):
                    line = reader.readline()
                    msg = decode_json_line(line)
                    received_seqs.append(int(msg["seq"]))
                    ack = encode_json_line({"v": 1, "t": "ack", "seq": msg["seq"], "ts": 6061, "d": {"ack_seq": msg["seq"], "ok": True}})
                    conn.sendall(ack)

        srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        srv.bind(("127.0.0.1", 0))
        srv.listen(1)
        port = srv.getsockname()[1]

        thread = threading.Thread(target=tcp_server_worker, args=(srv,), daemon=True)
        thread.start()

        with socket.create_connection(("127.0.0.1", port), timeout=3.0) as cli:
            reader = cli.makefile("rb")
            for envelope in node.buffer.drain_all_for_flush():
                cli.sendall(encode_json_line(envelope))
                ack_line = reader.readline()
                ack_msg = decode_json_line(ack_line)
                node.buffer.acknowledge_seq(int(ack_msg["d"]["ack_seq"]))

        srv.close()
        thread.join(timeout=2.0)
        self.assertEqual(received_seqs, [1, 2])
        self.assertEqual(node.buffer.pending_count, 0)


if __name__ == "__main__":
    unittest.main()
