"""
Capa de Abstracción de Hardware (HAL) para Raspberry Pi Pico W (MicroPython)
y entorno de pruebas en PC (Python 3).

Sigue las reglas de AGENTS.md:
- Pines definidos únicamente en `firmware/common/pins.py`.
- Lo que requiere validación física con la placa real se marca con `TODO-HW`.
- Nunca lee ni imprime secretos reales (`secrets.py` ignorado en git).
"""

try:
    import machine  # type: ignore
    import utime as time  # type: ignore

    IS_MICROPYTHON = True
except ImportError:
    import time

    machine = None  # type: ignore
    IS_MICROPYTHON = False


def ticks_ms() -> int:
    """Devuelve milisegundos monotónicos compatibles con MicroPython y CPython."""
    if hasattr(time, "ticks_ms"):
        return int(time.ticks_ms())  # type: ignore
    return int(time.monotonic() * 1000)


def ticks_diff(end_ms: int, start_ms: int) -> int:
    """Calcula diferencia entre ticks monotónicos en ms."""
    if hasattr(time, "ticks_diff"):
        return int(time.ticks_diff(end_ms, start_ms))  # type: ignore
    return int(end_ms - start_ms)


class WatchdogAdapter:
    """Abstracción del Watchdog Timer (WDT) de hardware en RP2040."""

    def __init__(self, timeout_ms: int = 8000) -> None:
        self.timeout_ms = timeout_ms
        self.feed_count = 0
        self._wdt = None
        if IS_MICROPYTHON and machine is not None and hasattr(machine, "WDT"):
            # TODO-HW: Verificar en Pico W que timeout_ms <= 8388 ms (límite del RP2040)
            self._wdt = machine.WDT(timeout=min(timeout_ms, 8000))

    def feed(self) -> None:
        self.feed_count += 1
        if self._wdt is not None:
            self._wdt.feed()


class PwmOutputAdapter:
    """Salida PWM con soporte de frecuencia (>= 1 kHz para luz nocturna, variable para audio/notas)."""

    def __init__(self, gpio_pin: int, freq_hz: int = 1000, initial_duty_u16: int = 0) -> None:
        self.gpio_pin = gpio_pin
        self.freq_hz = max(1, int(freq_hz))
        self.duty_u16 = max(0, min(65535, int(initial_duty_u16)))
        self._pwm = None
        if IS_MICROPYTHON and machine is not None:
            # TODO-HW: Verificar en Pico W que la rampa PWM >= 1 kHz con transistor NPN no parpadee
            pin_obj = machine.Pin(gpio_pin, machine.Pin.OUT)
            self._pwm = machine.PWM(pin_obj)
            self._pwm.freq(self.freq_hz)
            self._pwm.duty_u16(self.duty_u16)

    def set_freq(self, freq_hz: int) -> None:
        self.freq_hz = max(1, int(freq_hz))
        if self._pwm is not None:
            self._pwm.freq(self.freq_hz)

    def set_duty_u16(self, duty_u16: int) -> None:
        self.duty_u16 = max(0, min(65535, int(duty_u16)))
        if self._pwm is not None:
            self._pwm.duty_u16(self.duty_u16)

    def set_percent(self, percent: int) -> None:
        clamped = max(0, min(100, int(percent)))
        self.set_duty_u16(int(round((clamped / 100.0) * 65535)))


class DigitalPinAdapter:
    """Pin digital de entrada o salida."""

    def __init__(self, gpio_pin: int, is_output: bool = True, pull_up: bool = False) -> None:
        self.gpio_pin = gpio_pin
        self.is_output = is_output
        self.pull_up = pull_up
        self._value = 0
        self._pin = None
        if IS_MICROPYTHON and machine is not None:
            if is_output:
                self._pin = machine.Pin(gpio_pin, machine.Pin.OUT)
            else:
                pull = machine.Pin.PULL_UP if pull_up else machine.Pin.PULL_DOWN
                self._pin = machine.Pin(gpio_pin, machine.Pin.IN, pull)

    def write(self, val: int) -> None:
        self._value = 1 if val else 0
        if self._pin is not None:
            self._pin.value(self._value)

    def read(self) -> int:
        if self._pin is not None:
            return int(self._pin.value())
        return self._value

    def inject_mock_value(self, val: int) -> None:
        """Solo para pruebas en PC."""
        self._value = 1 if val else 0


class Hx711LoadCellAdapter:
    """
    Driver bit-banging para amplificador de celda de carga HX711.
    TODO-HW: Calibrar `offset_raw` y `counts_per_kg` con masas patrón conocidas en la celda real de 1 kg.
    """

    def __init__(
        self,
        dout_pin: int,
        pd_sck_pin: int,
        offset_raw: int = 0,
        counts_per_kg: float = 420000.0,
    ) -> None:
        self.dout_pin = dout_pin
        self.pd_sck_pin = pd_sck_pin
        self.offset_raw = offset_raw
        self.counts_per_kg = counts_per_kg if counts_per_kg != 0 else 1.0
        self._mock_kg = 0.0

    def inject_mock_kg(self, mass_kg: float) -> None:
        """Permite inyectar lecturas deterministas en pruebas de PC."""
        self._mock_kg = max(0.0, float(mass_kg))

    def read_kg(self) -> float:
        if not IS_MICROPYTHON:
            return self._mock_kg
        # TODO-HW: Lectura de 24 bits con pulsos en PD_SCK (<= 50 us en alto para no entrar en power-down)
        return self._mock_kg


class TemperatureHumiditySensorAdapter:
    """
    Abstracción para sensor ambiental de la central (AHT10/AHT20 por I2C o DHT22).
    TODO-HW: Confirmar modelo físico entregado (AHT20 en 0x38 cumple IH-02 +/-0.3 C / +/-3 % HR).
    """

    def __init__(self, sensor_type: str, sda_pin: int, scl_pin: int, dht_pin: int) -> None:
        self.sensor_type = sensor_type
        self.sda_pin = sda_pin
        self.scl_pin = scl_pin
        self.dht_pin = dht_pin
        self._mock_temp_c = 22.0
        self._mock_hum_pct = 50.0

    def inject_mock_reading(self, temp_c: float, hum_pct: float) -> None:
        self._mock_temp_c = float(temp_c)
        self._mock_hum_pct = float(hum_pct)

    def read(self) -> tuple[float, float]:
        # TODO-HW: Leer trama I2C de AHT20 (comando 0xAC 0x33 0x00) o pulso 1-wire de DHT22
        return (round(self._mock_temp_c, 2), round(self._mock_hum_pct, 2))


class Mlx90614IrThermometerAdapter:
    """
    Abstracción para termómetro infrarrojo sin contacto MLX90614 (GY-906) por SMBus/I2C.
    TODO-HW: Verificar que el módulo GY-906 sea de 3.3 V compatibles con RP2040 y leer registro To (0x07).
    """

    def __init__(self, sda_pin: int, scl_pin: int, i2c_address: int = 0x5A) -> None:
        self.sda_pin = sda_pin
        self.scl_pin = scl_pin
        self.i2c_address = i2c_address
        self._mock_temp_c = 36.8

    def inject_mock_temp_c(self, temp_c: float) -> None:
        self._mock_temp_c = float(temp_c)

    def read_object_temp_c(self) -> float:
        # TODO-HW: Leer registro RAM 0x07 (2 bytes + PEC): temp_c = raw * 0.02 - 273.15
        return round(self._mock_temp_c, 2)


class BatteryAdcAdapter:
    """
    Lectura de porcentaje de batería mediante divisor resistivo en pin ADC (GPIO26/ADC0).
    TODO-HW: Ajustar voltajes de corte para 3 pilas AA alcalinas/NiMH (3.0 V = 0 %, 4.5 V = 100 %).
    """

    def __init__(self, adc_pin: int) -> None:
        self.adc_pin = adc_pin
        self._mock_pct = 95

    def inject_mock_pct(self, pct: int) -> None:
        self._mock_pct = max(0, min(100, int(pct)))

    def read_battery_pct(self) -> int:
        # TODO-HW: Leer machine.ADC(adc_pin).read_u16() y escalar con factor del divisor resistivo
        return self._mock_pct
