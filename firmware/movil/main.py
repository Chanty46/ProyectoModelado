"""
Firmware MicroPython para Móvil de Cuna de 9 LEDs (`movil`) — Hito M9.

Responsabilidades:
- RF13: Controlar 9 LEDs normales mediante patrones visuales y velocidades (`lenta`, `media`, `rapida`).
- RF33: Registrar la duración efectiva de cada sesión y emitir `event: mobile_session_end` al detenerse.
"""

from typing import Any

from firmware.common.hal import DigitalPinAdapter, WatchdogAdapter
from firmware.common.pins import MOVIL_PINS
from firmware.common.protocol_and_buffer import OfflineCircularBuffer48h

SPEED_STEP_MS: dict[str, int] = {
    "lenta": 600,
    "media": 300,
    "rapida": 120,
}


class MovilFirmwareNode:
    """Controlador principal del nodo `movil` (9 LEDs) en Raspberry Pi Pico W."""

    def __init__(self, serial: str = "movil-pico-01") -> None:
        self.serial = serial
        self.device_type = "movil"
        self.buffer = OfflineCircularBuffer48h()
        self.wdt = WatchdogAdapter(timeout_ms=MOVIL_PINS["wdt_timeout_ms"])

        self.leds = [DigitalPinAdapter(pin, is_output=True) for pin in MOVIL_PINS["led_pins"]]
        self.is_on = False
        self.speed = "media"
        self.active_seconds = 0
        self._step_index = 0

    def advance_pattern_step(self) -> list[int]:
        """Avanza un paso de la secuencia circular de 9 LEDs si el móvil está encendido."""
        self.wdt.feed()
        if not self.is_on:
            for led in self.leds:
                led.write(0)
            return [0] * len(self.leds)

        active_idx = self._step_index % len(self.leds)
        states: list[int] = []
        for idx, led in enumerate(self.leds):
            val = 1 if idx == active_idx else 0
            led.write(val)
            states.append(val)
        self._step_index += 1
        return states

    def tick_second(self, ts: int) -> None:
        if self.is_on:
            self.active_seconds += 1

    def handle_command(self, envelope: dict[str, Any], ts: int) -> tuple[dict[str, Any], dict[str, Any] | None]:
        """
        Procesa `mobile_set` (`on`, `speed`).
        Si pasa de encendido a apagado, genera además el evento `mobile_session_end` (RF33).
        """
        self.wdt.feed()
        cmd_seq = int(envelope.get("seq", 0))
        data = envelope.get("d", {})
        cmd_name = data.get("cmd", "")

        session_end_event: dict[str, Any] | None = None
        if cmd_name == "mobile_set":
            requested_on = bool(data.get("on", False))
            requested_speed = str(data.get("speed", "media"))
            if requested_speed in SPEED_STEP_MS:
                self.speed = requested_speed

            if self.is_on and not requested_on:
                # Emitir duración efectiva de la sesión al apagarse (RF33)
                session_end_event = self.buffer.enqueue_envelope(
                    "event",
                    {"name": "mobile_session_end", "duration_s": max(1, self.active_seconds)},
                    ts=ts,
                )
                self.active_seconds = 0

            self.is_on = requested_on
            self.advance_pattern_step()

        ack = {
            "v": 1,
            "t": "ack",
            "seq": self.buffer.next_seq(),
            "ts": ts,
            "d": {"ack_seq": cmd_seq, "ok": True},
        }
        return ack, session_end_event
