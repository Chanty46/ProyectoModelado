"""
Capa de abstracción de hardware (HAL) para Raspberry Pi Pico W (MicroPython)
con modo simulado para pruebas en PC (CPython 3).

- Los pines vienen SOLO de `common/pins.py`.
- En la Pico se usan los drivers reales (HX711, AHT20/DHT22, MLX90614, ADC, PWM).
- En PC cada adaptador expone `inject_mock_*` para pruebas deterministas.
- Lo que solo puede validarse con la placa real está marcado con `TODO-HW`.

Compatible con MicroPython: sin `typing`, sin `dataclasses`, sin anotaciones.
"""

try:
    import machine  # type: ignore
    import utime as time  # type: ignore

    IS_MICROPYTHON = True
except ImportError:
    import time

    machine = None
    IS_MICROPYTHON = False

try:
    import ubinascii as _binascii  # type: ignore
except ImportError:
    import binascii as _binascii


def ticks_ms():
    """Devuelve un contador monotónico en milisegundos (MicroPython o CPython)."""
    if hasattr(time, "ticks_ms"):
        return time.ticks_ms()
    return int(time.monotonic() * 1000)


def ticks_diff(end_ms, start_ms):
    """Calcula `end_ms - start_ms` evitando desbordes en `utime.ticks_ms`."""
    if hasattr(time, "ticks_diff"):
        return time.ticks_diff(end_ms, start_ms)
    return end_ms - start_ms


def sleep_ms(ms):
    """Pausa bloqueante en milisegundos compatible con MicroPython y CPython."""
    if hasattr(time, "sleep_ms"):
        time.sleep_ms(ms)
    else:
        time.sleep(ms / 1000.0)


def b64decode(text):
    """Decodifica base64 en MicroPython (`ubinascii`) y CPython (`binascii`)."""
    if not text:
        return b""
    return _binascii.a2b_base64(text)


def _clamp(value, low, high):
    """Acota `value` al intervalo cerrado `[low, high]`."""
    return low if value < low else (high if value > high else value)


class WatchdogAdapter:
    """Watchdog de hardware del RP2040 (máx. ~8.3 s)."""

    def __init__(self, timeout_ms=8000, enabled=True):
        """Inicializa el WDT físico en RP2040 si `enabled` y `IS_MICROPYTHON` son True."""
        self.feed_count = 0
        self._wdt = None
        if IS_MICROPYTHON and enabled:
            self._wdt = machine.WDT(timeout=min(int(timeout_ms), 8000))

    def feed(self):
        """Alimenta el watchdog para evitar el reinicio del microcontrolador."""
        self.feed_count += 1
        if self._wdt is not None:
            self._wdt.feed()


class PwmOutputAdapter:
    """Salida PWM (luz nocturna >= 1 kHz, audio, buzzer)."""

    def __init__(self, gpio_pin, freq_hz=1000, initial_duty_u16=0):
        """Configura el canal PWM con frecuencia mínima de 8 Hz y duty de 16 bits."""
        self.gpio_pin = gpio_pin
        self.freq_hz = max(8, int(freq_hz))
        self.duty_u16 = int(_clamp(int(initial_duty_u16), 0, 65535))
        self._pwm = None
        if IS_MICROPYTHON:
            self._pwm = machine.PWM(machine.Pin(gpio_pin))
            self._pwm.freq(self.freq_hz)
            self._pwm.duty_u16(self.duty_u16)

    def set_freq(self, freq_hz):
        """Actualiza la frecuencia de la portadora PWM (mínimo 8 Hz en RP2040)."""
        self.freq_hz = max(8, int(freq_hz))
        if self._pwm is not None:
            self._pwm.freq(self.freq_hz)

    def set_duty_u16(self, duty_u16):
        """Actualiza el ciclo de trabajo en el rango `0..65535`."""
        self.duty_u16 = int(_clamp(int(duty_u16), 0, 65535))
        if self._pwm is not None:
            self._pwm.duty_u16(self.duty_u16)


class DigitalPinAdapter:
    """Pin digital de entrada o salida."""

    def __init__(self, gpio_pin, is_output=True, pull_up=False):
        """Configura un GPIO como salida o entrada digital con resistencia pull."""
        self.gpio_pin = gpio_pin
        self._value = 1 if (not is_output and pull_up) else 0
        self._pin = None
        if IS_MICROPYTHON:
            if is_output:
                self._pin = machine.Pin(gpio_pin, machine.Pin.OUT, value=0)
            else:
                pull = machine.Pin.PULL_UP if pull_up else machine.Pin.PULL_DOWN
                self._pin = machine.Pin(gpio_pin, machine.Pin.IN, pull)

    def write(self, val):
        """Escribe `0` o `1` en el pin de salida (y en el estado mock)."""
        self._value = 1 if val else 0
        if self._pin is not None:
            self._pin.value(self._value)

    def read(self):
        """Lee el estado lógico actual (`0` o `1`) del pin físico o simulado."""
        if self._pin is not None:
            return self._pin.value()
        return self._value

    def inject_mock_value(self, val):
        """Inyecta un valor lógico (`0` o `1`) para pruebas unitarias en PC."""
        self._value = 1 if val else 0


class Hx711LoadCellAdapter:
    """
    Driver bit-bang del HX711 (canal A, ganancia 128).
    TODO-HW: calibrar `offset_raw` (tara) y `counts_per_kg` con una masa patrón.
    """

    def __init__(self, dout_pin, pd_sck_pin, offset_raw=0, counts_per_kg=420000.0):
        """Inicializa pines de datos/reloj del HX711 y los factores de calibración."""
        self.offset_raw = int(offset_raw)
        self.counts_per_kg = float(counts_per_kg) or 1.0
        self._mock_kg = 0.0
        self._dout = None
        self._sck = None
        if IS_MICROPYTHON:
            self._dout = machine.Pin(dout_pin, machine.Pin.IN)
            self._sck = machine.Pin(pd_sck_pin, machine.Pin.OUT, value=0)

    def inject_mock_kg(self, mass_kg):
        """Fija la masa simulada en kilogramos para pruebas en PC."""
        self._mock_kg = max(0.0, float(mass_kg))

    def _wait_dout_ready(self, timeout_ms):
        """Espera a que `DOUT` baje a 0 indicando conversión lista; False si expira."""
        start = ticks_ms()
        while self._dout.value() == 1:
            if ticks_diff(ticks_ms(), start) > timeout_ms:
                return False
        return True

    def _pulse_read_24bits(self):
        """Lee 24 bits MSB-first y envía el pulso 25 (canal A, ganancia 128)."""
        # Pulsos cortos (< 50 us en alto) sin interrupciones para no entrar en power-down
        state = machine.disable_irq()
        try:
            raw = 0
            for _ in range(24):
                self._sck.value(1)
                self._sck.value(0)
                raw = (raw << 1) | self._dout.value()
            self._sck.value(1)
            self._sck.value(0)
            return raw
        finally:
            machine.enable_irq(state)

    def _read_raw(self, timeout_ms=150):
        """Obtiene una muestra cruda con signo de 24 bits o None si hubo timeout."""
        if not self._wait_dout_ready(timeout_ms):
            return None
        raw = self._pulse_read_24bits()
        if raw & 0x800000:
            raw -= 0x1000000
        return raw

    def tare(self, samples=10):
        """Promedia `samples` lecturas sin carga para ajustar `offset_raw` en hardware."""
        if not IS_MICROPYTHON:
            return
        values = [v for v in (self._read_raw() for _ in range(samples)) if v is not None]
        if values:
            self.offset_raw = sum(values) // len(values)

    def read_kg(self):
        """Devuelve la masa en kg (>= 0) o None si falla la lectura del HX711."""
        if not IS_MICROPYTHON:
            return self._mock_kg
        raw = self._read_raw()
        if raw is None:
            return None
        return max(0.0, (raw - self.offset_raw) / self.counts_per_kg)


class TemperatureHumiditySensorAdapter:
    """
    Sensor ambiental de la central: AHT20 (I2C 0x38, cumple IH-02) o DHT22.
    TODO-HW: confirmar el modelo entregado y los pull-ups del bus I2C.
    """

    AHT_ADDR = 0x38

    def __init__(self, sensor_type, i2c_id, sda_pin, scl_pin, dht_pin):
        """Inicializa bus I2C para AHT20 o pin digital para DHT22 según `sensor_type`."""
        self.sensor_type = sensor_type
        self._mock = (22.0, 50.0)
        self._i2c = None
        self._dht = None
        if IS_MICROPYTHON:
            if sensor_type == "DHT22":
                import dht  # type: ignore

                self._dht = dht.DHT22(machine.Pin(dht_pin))
            else:
                self._i2c = machine.I2C(i2c_id, sda=machine.Pin(sda_pin), scl=machine.Pin(scl_pin), freq=100000)
                self._i2c.writeto(self.AHT_ADDR, b"\xbe\x08\x00")  # calibración inicial AHT20
                sleep_ms(20)

    def inject_mock_reading(self, temp_c, hum_pct):
        """Fija temperatura (°C) y humedad relativa (%) simuladas para pruebas en PC."""
        self._mock = (float(temp_c), float(hum_pct))

    def _read_dht22(self):
        """Lee temperatura y humedad desde un sensor DHT22 físico."""
        self._dht.measure()
        return (round(self._dht.temperature(), 2), round(self._dht.humidity(), 2))

    def _read_aht20(self):
        """Dispara medición en AHT20 por I2C y decodifica 20 bits de humedad y temperatura."""
        self._i2c.writeto(self.AHT_ADDR, b"\xac\x33\x00")
        sleep_ms(80)
        d = self._i2c.readfrom(self.AHT_ADDR, 6)
        if d[0] & 0x80:  # bit 7 en 1 indica sensor ocupado
            return None
        hum = ((d[1] << 12) | (d[2] << 4) | (d[3] >> 4)) * 100.0 / 1048576
        temp = (((d[3] & 0x0F) << 16) | (d[4] << 8) | d[5]) * 200.0 / 1048576 - 50
        return (round(temp, 2), round(hum, 2))

    def read(self):
        """Devuelve `(temp_c, hum_pct)` o `None` si la lectura física falla."""
        if not IS_MICROPYTHON:
            return (round(self._mock[0], 2), round(self._mock[1], 2))
        try:
            if self._dht is not None:
                return self._read_dht22()
            return self._read_aht20()
        except OSError:
            return None


class Mlx90614IrThermometerAdapter:
    """
    Termómetro IR MLX90614 (GY-906), registro RAM 0x07 (temperatura del objeto).
    TODO-HW: verificar que el módulo sea de 3.3 V antes de conectarlo a la Pico.
    """

    def __init__(self, i2c_id, sda_pin, scl_pin, i2c_address=0x5A):
        """Configura el bus I2C a 100 kHz para leer el termómetro infrarrojo."""
        self.i2c_address = i2c_address
        self._mock_temp_c = 36.8
        self._i2c = None
        if IS_MICROPYTHON:
            self._i2c = machine.I2C(i2c_id, sda=machine.Pin(sda_pin), scl=machine.Pin(scl_pin), freq=100000)

    def inject_mock_temp_c(self, temp_c):
        """Fija la temperatura simulada del objeto (°C) para pruebas en PC."""
        self._mock_temp_c = float(temp_c)

    def read_object_temp_c(self):
        """Lee el registro 0x07 (0.02 K/LSB) y lo convierte a grados Celsius."""
        if not IS_MICROPYTHON:
            return round(self._mock_temp_c, 2)
        try:
            d = self._i2c.readfrom_mem(self.i2c_address, 0x07, 3)
            return round((d[0] | (d[1] << 8)) * 0.02 - 273.15, 2)
        except OSError:
            return None


class BatteryAdcAdapter:
    """
    Batería de 3 pilas AA leída por ADC con divisor resistivo.
    TODO-HW: confirmar divisor (2:1 propuesto) y voltajes de corte (3.0 V = 0 %, 4.5 V = 100 %).
    """

    def __init__(self, adc_pin, divider_ratio=2.0, v_empty=3.0, v_full=4.5):
        """Inicializa el canal ADC y la curva lineal voltaje -> porcentaje."""
        self.divider_ratio = divider_ratio
        self.v_empty = v_empty
        self.v_full = v_full
        self._mock_pct = 95
        self._adc = machine.ADC(adc_pin) if IS_MICROPYTHON else None

    def inject_mock_pct(self, pct):
        """Fija el porcentaje de batería simulado (`0..100`) para pruebas en PC."""
        self._mock_pct = int(_clamp(int(pct), 0, 100))

    def read_battery_pct(self):
        """Convierte la lectura ADC de 16 bits a porcentaje de carga (`0..100`)."""
        if self._adc is None:
            return self._mock_pct
        volts = self._adc.read_u16() * 3.3 / 65535 * self.divider_ratio
        pct = (volts - self.v_empty) / (self.v_full - self.v_empty) * 100
        return int(_clamp(round(pct), 0, 100))
