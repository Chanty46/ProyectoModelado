"""
Firmware MicroPython para Base Inteligente de Biberón (`biberon`) — Hito M9.

Responsabilidades:
- RF15 / RF37: Celda de carga HX711 (en gramos) + termómetro IR sin contacto MLX90614 (GY-906).
  Detecta colocación del biberón cuando la masa estabilizada es `>= 20.0 g` y emite `event: bottle_placed`
  con `mass_g`, `temp_c` y `battery_pct`.
- RF16 / RF17: El backend C# evalúa el rango seguro de temperatura (`35.0–38.5 °C`) y calcula el
  volumen ingerido (`ml = round((m_inicial - m_final) / 1.03, 1)`).
"""

from typing import Any

from firmware.common.hal import (
    BatteryAdcAdapter,
    Hx711LoadCellAdapter,
    Mlx90614IrThermometerAdapter,
    WatchdogAdapter,
)
from firmware.common.pins import BIBERON_PINS
from firmware.common.protocol_and_buffer import OfflineCircularBuffer48h

MIN_BOTTLE_PLACED_G = 20.0
HYSTERESIS_REMOVED_G = 5.0


class BiberonFirmwareNode:
    """Controlador principal del nodo `biberon` en Raspberry Pi Pico W."""

    def __init__(self, serial: str = "biberon-pico-01") -> None:
        self.serial = serial
        self.device_type = "biberon"
        self.buffer = OfflineCircularBuffer48h()
        self.wdt = WatchdogAdapter(timeout_ms=BIBERON_PINS["wdt_timeout_ms"])

        self.load_cell = Hx711LoadCellAdapter(
            dout_pin=BIBERON_PINS["hx711_dout_pin"],
            pd_sck_pin=BIBERON_PINS["hx711_pd_sck_pin"],
        )
        self.ir_temp = Mlx90614IrThermometerAdapter(
            sda_pin=BIBERON_PINS["i2c_sda_pin"],
            scl_pin=BIBERON_PINS["i2c_scl_pin"],
            i2c_address=BIBERON_PINS["mlx90614_i2c_addr"],
        )
        self.battery_adc = BatteryAdcAdapter(adc_pin=BIBERON_PINS["battery_adc_pin"])

        self.bottle_on_base = False
        self.last_stable_mass_g = 0.0

    def sample_sensors(self, ts: int) -> dict[str, Any] | None:
        """
        Lee masa en gramos, temperatura IR en °C y nivel de batería.
        Cuando se coloca un biberón (`>= 20.0 g`), encola y devuelve `event: bottle_placed` (RF37).
        """
        self.wdt.feed()
        mass_g = round(self.load_cell.read_kg() * 1000.0, 1)
        temp_c = self.ir_temp.read_object_temp_c()
        battery_pct = self.battery_adc.read_battery_pct()

        self.last_stable_mass_g = mass_g

        if not self.bottle_on_base and mass_g >= MIN_BOTTLE_PLACED_G:
            self.bottle_on_base = True
            return self.buffer.enqueue_envelope(
                "event",
                {
                    "name": "bottle_placed",
                    "mass_g": mass_g,
                    "temp_c": temp_c,
                    "battery_pct": battery_pct,
                },
                ts=ts,
            )

        if self.bottle_on_base and mass_g < HYSTERESIS_REMOVED_G:
            self.bottle_on_base = False

        return None

    def flush_minute_telemetry(self, ts: int) -> dict[str, Any]:
        """Persiste telemetría agregada por minuto del biberón en el búfer circular de 48 h."""
        return self.buffer.enqueue_envelope(
            "telemetry",
            {
                "mass_g": round(self.load_cell.read_kg() * 1000.0, 1),
                "temp_c": self.ir_temp.read_object_temp_c(),
                "battery_pct": self.battery_adc.read_battery_pct(),
            },
            ts=ts,
        )
