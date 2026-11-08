"""
Propuesta de asignación de pines GPIO para las 5 Raspberry Pi Pico W (RP2040).
Basado en el BOM real de la maqueta (ver docs/PROJECT_CONTEXT.md §3).
TODO-HW: confirmar cada pin en protoboard antes de energizar las Pico W reales.
"""

WDT_TIMEOUT_MS = 8000  # Watchdog del RP2040 (máx. ~8.3 s)

# 1. Unidad Central (Pico W #1)
# - Sensor T/H: AHT20 por I2C0 (IH-02) o DHT22 de respaldo
# - Luz nocturna: LED blanco con transistor NPN (PWM >= 1 kHz, rampa 1.5 s, RF09)
# - Audio/alarma/canciones: PWM -> filtro RC -> PAM8403 -> parlante 8 Ohm (RF04, RF11, RF12)
# - Botón táctil TTP223 de Modo Atención (RF05) y LED RGB de estado (IU-03)
CENTRAL_PINS = {
    "ENV_SENSOR": "AHT20",   # TODO-HW: "AHT20" o "DHT22" según el sensor entregado
    "I2C_ID": 0,
    "I2C_SDA": 4,            # GP4 - TODO-HW: verificar pull-ups 4.7k a 3.3 V
    "I2C_SCL": 5,            # GP5
    "DHT_FALLBACK": 6,       # GP6 (solo si se instala DHT22) - TODO-HW
    "LAMP_PWM": 15,          # GP15 -> base del NPN del LED blanco - TODO-HW
    "LAMP_PWM_HZ": 1000,     # >= 1 kHz (RF09)
    "AUDIO_PWM": 16,         # GP16 -> filtro RC -> PAM8403 - TODO-HW
    "AUDIO_CARRIER_HZ": 62500,  # portadora PWM muy por encima de la banda de audio (D5)
    "TOUCH_TTP223": 14,      # GP14 <- salida digital del TTP223 - TODO-HW
    "RGB_RED": 10,           # GP10
    "RGB_GREEN": 11,         # GP11
    "RGB_BLUE": 12,          # GP12
}

# 2. Nodo Colchón (Pico W #2) - celda de 1 kg + HX711 (IH-01, D14)
COLCHON_PINS = {
    "HX711_DT": 2,           # GP2 <- DOUT - TODO-HW: calibrar con muñeco de 0.5-0.8 kg
    "HX711_SCK": 3,          # GP3 -> PD_SCK
}

# 3. Nodo Móvil (Pico W #3) - 9 LED (RF13, RF33)
MOVIL_PINS = {
    "LED_PINS": (2, 3, 4, 5, 6, 7, 8, 9, 10),  # GP2..GP10 con resistencias 220-330 Ohm - TODO-HW
}

# 4. Nodo Biberón (Pico W #4) - HX711 + MLX90614 (GY-906) + batería 3xAA
BIBERON_PINS = {
    "HX711_DT": 2,           # GP2 - TODO-HW: calibrar gramos con biberón real
    "HX711_SCK": 3,          # GP3
    "I2C_ID": 0,
    "I2C_SDA": 4,            # GP4 - TODO-HW: confirmar GY-906 de 3.3 V
    "I2C_SCL": 5,            # GP5
    "MLX90614_ADDR": 0x5A,
    "BATTERY_ADC": 26,       # GP26 (ADC0) <- divisor resistivo - TODO-HW
}

# 5. Nodo Juguete (Pico W #5) - 5 pulsadores, 5 LED, buzzer pasivo, batería 3xAA
JUGUETE_PINS = {
    "BUTTON_PINS": (2, 3, 4, 5, 6),    # GP2..GP6 a GND con PULL_UP - TODO-HW
    "LED_PINS": (10, 11, 12, 13, 14),  # GP10..GP14 - TODO-HW
    "BUZZER_PWM": 15,                  # GP15 - TODO-HW
    "BATTERY_ADC": 26,                 # GP26 (ADC0) - TODO-HW
}
