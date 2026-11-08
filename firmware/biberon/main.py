"""
Base de Biberón (Pico W #4) — MicroPython.

RF15 / RF37  Celda HX711 (en gramos) + termómetro IR MLX90614 (GY-906) + nivel batería.
             Detecta colocación del biberón cuando la masa estable es >= 20.0 g y
             emite `event: bottle_placed` (con `mass_g`, `temp_c`, `battery_pct`).
RF16 / RF17  Evaluación de temperatura segura (35.0 - 38.5 °C) y cálculo de volumen ingerido
             (ml = (m_inicial - m_final) / 1.03) delegados al backend C#.
Telemetría por minuto: `mass_g`, `temp_c`, `battery_pct`.
"""

from common.device_runtime import DeviceNode, run
from common.hal import (
    IS_MICROPYTHON,
    BatteryAdcAdapter,
    Hx711LoadCellAdapter,
    Mlx90614IrThermometerAdapter,
    ticks_diff,
    ticks_ms,
)
from common.pins import BIBERON_PINS

MIN_BOTTLE_PLACED_G = 20.0
HYSTERESIS_REMOVED_G = 8.0


class BiberonNode(DeviceNode):
    device_type = "biberon"
    tick_interval_ms = 500  # 2 Hz

    def __init__(self, buffer=None, clock=None):
        DeviceNode.__init__(self, buffer, clock)
        p = BIBERON_PINS
        self.load_cell = Hx711LoadCellAdapter(p["HX711_DT"], p["HX711_SCK"])
        self.ir_temp = Mlx90614IrThermometerAdapter(
            p["I2C_ID"], p["I2C_SDA"], p["I2C_SCL"], p["MLX90614_ADDR"]
        )
        self.battery_adc = BatteryAdcAdapter(p["BATTERY_ADC"])

        self.bottle_present = False
        self._minute_start_ms = None
        self._mass_samples = []
        self._temp_samples = []

    def heartbeat_data(self):
        data = DeviceNode.heartbeat_data(self)
        data["battery_pct"] = self.battery_adc.read_battery_pct()
        return data

    def tick(self, now_ms=None):
        now_ms = ticks_ms() if now_ms is None else now_ms
        if self._minute_start_ms is None:
            self._minute_start_ms = now_ms

        kg = self.load_cell.read_kg()
        mass_g = round(kg * 1000.0, 1) if kg is not None else 0.0
        temp_c = self.ir_temp.read_object_temp_c()
        temp_c = round(temp_c, 1) if temp_c is not None else 25.0
        battery = self.battery_adc.read_battery_pct()

        self._mass_samples.append(mass_g)
        self._temp_samples.append(temp_c)

        # Detección de colocación de biberón (RF37: >= 20.0 g)
        if not self.bottle_present and mass_g >= MIN_BOTTLE_PLACED_G:
            self.bottle_present = True
            self.emit_event(
                "bottle_placed",
                mass_g=mass_g,
                temp_c=temp_c,
                battery_pct=battery,
            )
        elif self.bottle_present and mass_g < HYSTERESIS_REMOVED_G:
            self.bottle_present = False

        # Telemetría cada minuto
        if ticks_diff(now_ms, self._minute_start_ms) >= 60000:
            self._minute_start_ms = now_ms
            avg_m = round(sum(self._mass_samples) / len(self._mass_samples), 1) if self._mass_samples else mass_g
            avg_t = round(sum(self._temp_samples) / len(self._temp_samples), 1) if self._temp_samples else temp_c
            self.emit_telemetry({
                "mass_g": avg_m,
                "temp_c": avg_t,
                "battery_pct": battery,
            })
            self._mass_samples = []
            self._temp_samples = []


if IS_MICROPYTHON:
    import secrets  # type: ignore  # noqa: E402

    run(BiberonNode(), secrets)
