"""
Propuesta de asignación de pines GPIO para las 5 Raspberry Pi Pico W (RP2040).
Basado en el BOM real de la maqueta (ver docs/PROJECT_CONTEXT.md §3).
TODO-HW: Confirmar cada pin en protoboard antes de energizar las Pico W reales.
"""

# 1. Unidad Central (Pico W #1)
# Sensores y actuadores:
# - Sensor T/H: AHT10/AHT20 por I2C0 (IH-02) o DHT22 de respaldo
# - Iluminación nocturna: LED blanco con transistor NPN (PWM >= 1 kHz, rampa 1.5 s, RF09)
# - Audio / Alarma / Canciones: PWM + filtro RC pasa-bajos -> PAM8403 -> parlante 8 Ohm (RF04, RF11, RF12)
# - Botón táctil Modo Atención: TTP223 (RF05, IU-03)
# - Indicador visual de estado: LED RGB cátodo común (IU-03)
CENTRAL_PINS = {
    "I2C_ID": 0,
    "I2C_SDA": 4,
    "I2C_SCL": 5,
    "DHT_FALLBACK": 6,
    "LAMP_PWM": 15,
    "AUDIO_PWM": 16,
    "TOUCH_TTP223": 14,
    "RGB_RED": 10,
    "RGB_GREEN": 11,
    "RGB_BLUE": 12,
    "env_sensor_type": "AHT20",        # TODO-HW: Confirmar AHT10/AHT20 I2C (0x38) o cambiar a "DHT22"
    "i2c_sda_pin": 4,                  # GP4 (I2C0 SDA - AHT10/AHT20) - TODO-HW: verificar pull-ups 4.7k a 3.3V
    "i2c_scl_pin": 5,                  # GP5 (I2C0 SCL - AHT10/AHT20)
    "dht22_data_pin": 6,               # GP6 (si se instala DHT22 en vez de AHT10/AHT20) - TODO-HW
    "night_lamp_pwm_pin": 15,          # GP15 -> Base transistor NPN para LED blanco (PWM >= 1 kHz) - TODO-HW
    "night_lamp_pwm_freq_hz": 1200,    # >= 1000 Hz según RF09
    "audio_pwm_pin": 16,               # GP16 -> Filtro RC (R=1.8k, C=10nF) -> Entrada PAM8403 - TODO-HW
    "audio_carrier_freq_hz": 32000,    # Portadora PWM ultrasónica de 32 kHz para audio PCM 8 kHz (D5)
    "touch_attention_pin": 14,         # GP14 <- Salida digital sensor táctil TTP223 (Modo Atención RF05) - TODO-HW
    "rgb_red_pin": 10,                 # GP10 -> LED RGB Rojo (Alarma RF04)
    "rgb_green_pin": 11,               # GP11 -> LED RGB Verde (Monitoreo)
    "rgb_blue_pin": 12,                # GP12 -> LED RGB Azul / combinación Amarillo (R+G para Modo Atención)
    "wdt_timeout_ms": 8000,            # Watchdog Timer RP2040 (<= 8388 ms)
}

# 2. Nodo Colchón (Pico W #2)
# - Celda de carga de 1 kg + módulo amplificador HX711 (IH-01, D14)
COLCHON_PINS = {
    "HX711_DT": 2,
    "HX711_SCK": 3,
    "hx711_dout_pin": 2,               # GP2 <- DOUT del HX711 - TODO-HW: calibrar factor de escala y tara con muñeco 0.5-0.8 kg
    "hx711_pd_sck_pin": 3,             # GP3 -> PD_SCK del HX711
    "wdt_timeout_ms": 8000,
}

# 3. Nodo Móvil Colgante (Pico W #3)
# - 9 LEDs normales controlados individualmente (RF13, RF33)
MOVIL_PINS = {
    "LED_PINS": (2, 3, 4, 5, 6, 7, 8, 9, 10),
    "led_pins": (2, 3, 4, 5, 6, 7, 8, 9, 10),  # GP2..GP10 -> 9 LEDs con resistencias de 220-330 Ohm - TODO-HW
    "wdt_timeout_ms": 8000,
}

# 4. Nodo Base de Biberón (Pico W #4)
# - Celda de carga 500 g / 1 kg + HX711 (masa en gramos, RF15/RF17)
# - Sensor infrarrojo sin contacto MLX90614 (GY-906) por I2C (temperatura °C, IH-04, RF15/RF16)
# - Lectura de voltaje de batería (3 pilas AA) por ADC
BIBERON_PINS = {
    "HX711_DT": 2,
    "HX711_SCK": 3,
    "I2C_ID": 0,
    "I2C_SDA": 4,
    "I2C_SCL": 5,
    "BATTERY_ADC": 26,
    "hx711_dout_pin": 2,               # GP2 <- DOUT del HX711 - TODO-HW: calibrar gramos con vaso/biberón real
    "hx711_pd_sck_pin": 3,             # GP3 -> PD_SCK del HX711
    "i2c_sda_pin": 4,                  # GP4 (I2C0 SDA - MLX90614 GY-906) - TODO-HW: verificar versión de 3.3V del GY-906
    "i2c_scl_pin": 5,                  # GP5 (I2C0 SCL - MLX90614 GY-906)
    "mlx90614_i2c_addr": 0x5A,
    "battery_adc_pin": 26,             # GP26 (ADC0) <- Divisor resistivo de las 3 pilas AA - TODO-HW
    "wdt_timeout_ms": 8000,
}

# 5. Nodo Juguete de 5 Botones (Pico W #5)
# - 5 pulsadores con pull-up interno + 5 LEDs indicadores + 1 buzzer pasivo PWM (RF14, RF34-RF36)
# - Lectura de batería (3 pilas AA) por ADC0 (RF36)
JUGUETE_PINS = {
    "BUTTON_PINS": (2, 3, 4, 5, 6),
    "LED_PINS": (10, 11, 12, 13, 14),
    "BUZZER_PWM": 15,
    "BATTERY_ADC": 26,
    "button_pins": (2, 3, 4, 5, 6),    # GP2..GP6 <- 5 pulsadores a GND (PULL_UP) - TODO-HW
    "led_pins": (10, 11, 12, 13, 14),  # GP10..GP14 -> 5 LEDs indicadores - TODO-HW
    "buzzer_pwm_pin": 15,              # GP15 -> Buzzer pasivo PWM - TODO-HW
    "battery_adc_pin": 26,             # GP26 (ADC0) <- Divisor resistivo 3 pilas AA - TODO-HW
    "wdt_timeout_ms": 8000,
}
