import unittest
try:
    from simulators.protocol_client import (
        CentralAudioPlayer,
        CentralEnvRuleEngine,
        MattressRuleEngine,
        OfflineRingBuffer,
        build_envelope,
        compute_backoff_seconds,
    )
except ImportError:
    from protocol_client import (
        CentralAudioPlayer,
        CentralEnvRuleEngine,
        MattressRuleEngine,
        OfflineRingBuffer,
        build_envelope,
        compute_backoff_seconds,
    )


class SimulatorProtocolAndRulesTests(unittest.TestCase):
    def test_exponential_backoff_caps_at_30_seconds(self) -> None:
        self.assertEqual(compute_backoff_seconds(1), 1)
        self.assertEqual(compute_backoff_seconds(2), 2)
        self.assertEqual(compute_backoff_seconds(3), 4)
        self.assertEqual(compute_backoff_seconds(4), 8)
        self.assertEqual(compute_backoff_seconds(5), 16)
        self.assertEqual(compute_backoff_seconds(6), 30)
        self.assertEqual(compute_backoff_seconds(10), 30)

    def test_offline_ring_buffer_preserves_seq_order_and_acknowledges(self) -> None:
        buf = OfflineRingBuffer(capacity=3)
        e1 = buf.enqueue("telemetry", {"temp_c": 21.0, "hum_pct": 45.0})
        e2 = buf.enqueue("telemetry", {"temp_c": 21.5, "hum_pct": 46.0})
        e3 = buf.enqueue("telemetry", {"temp_c": 22.0, "hum_pct": 47.0})
        e4 = buf.enqueue("telemetry", {"temp_c": 22.5, "hum_pct": 48.0})

        self.assertEqual([e["seq"] for e in buf.pending_messages()], [2, 3, 4])
        self.assertEqual(e1["seq"], 1)
        self.assertEqual(e4["seq"], 4)

        buf.ack_up_to(3)
        self.assertEqual([e["seq"] for e in buf.pending_messages()], [4])

    def test_d14_mattress_removal_detected_within_1_second(self) -> None:
        engine = MattressRuleEngine(mass_scale=0.20)
        # Bebé presente con 0.65 kg (>= 0.50 kg con escala 0.2)
        self.assertEqual(engine.process_sample(100.0, 0.650), [])
        # Cae a 0.05 kg (< 0.20 kg) en 0.4 s -> debe emitir evento 'removal' (RF04 + D14)
        events = engine.process_sample(100.4, 0.050)
        self.assertEqual(len(events), 1)
        self.assertEqual(events[0]["kind"], "removal")

    def test_rf06_and_rf29_movement_index_filters_noise_and_removal_jumps(self) -> None:
        engine = MattressRuleEngine(
            mass_scale=0.20,
            noise_floor_kg=0.010,
            movement_threshold_kg_per_min=0.100,
        )
        t = 0.0
        engine.process_sample(t, 0.600)

        # Ruido dentro del piso (0.005 kg <= 0.010 kg) se ignora
        t += 0.2
        engine.process_sample(t, 0.605)
        self.assertEqual(engine.pop_minute_telemetry(0.605)["movement_idx"], 0.0)

        # Movimiento real sostenido durante > 30 s supera umbral y dispara movement_high (RF29)
        emitted_high = False
        mass = 0.600
        for step in range(1, 170):  # 170 * 0.2 s = 34 s a 5 Hz
            t = step * 0.2
            mass = 0.630 if (step % 2 == 0) else 0.600  # |Δmasa| = 0.030 kg (> 0.010 piso)
            evts = engine.process_sample(t, mass)
            if any(e["kind"] == "movement_high" for e in evts):
                emitted_high = True

        self.assertTrue(emitted_high)

    def test_rf07_and_rf09_central_thresholds_and_light_ramp(self) -> None:
        central = CentralEnvRuleEngine(temp_min=18.0, temp_max=26.0, hum_min=40.0, hum_max=60.0)
        self.assertIsNone(central.evaluate_env(0.0, 29.0, 50.0))
        self.assertIsNone(central.evaluate_env(5.0, 29.0, 50.0))
        evt = central.evaluate_env(10.1, 29.0, 50.0)
        self.assertIsNotNone(evt)
        self.assertEqual(evt["kind"], "env_out_of_range")

        ramp = central.compute_light_ramp_steps(100, steps=5)
        self.assertEqual(ramp, [20, 40, 60, 80, 100])

    def test_envelope_rejects_lines_over_4kb(self) -> None:
        with self.assertRaises(ValueError):
            build_envelope("telemetry", 1, {"huge": "x" * 5000})

    def test_rf11_central_audio_player_decodes_pcm_and_computes_pwm_duty(self) -> None:
        import base64

        player = CentralAudioPlayer()
        pcm_samples = bytes([128, 160, 96, 180] * 100)  # 400 muestras a 8 kHz = 50 ms
        b64 = base64.b64encode(pcm_samples).decode("ascii")
        stats = player.process_audio_chunk({"rate": 8000, "seq": 1, "pcm_b64": b64})
        self.assertEqual(stats["samples"], 400)
        self.assertEqual(stats["duration_ms"], 50.0)
        self.assertEqual(player.chunks_received, 1)
        self.assertEqual(player.samples_played, 400)


if __name__ == "__main__":
    unittest.main()
