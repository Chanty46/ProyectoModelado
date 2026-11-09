# Pruebas Físicas Pendientes en Hardware Real (`TODO-HW`) — Hito M9

Según las reglas de `AGENTS.md` y el Plan de Desarrollo (`docs/PLAN_APROBADO.md`), toda la lógica de protocolo, máquina de estados, umbrales, cálculo de índice de movimiento a 5 Hz, búfer circular de 48 horas y controladores de los 5 nodos está implementada y verificada mediante pruebas unitarias en `firmware/test_firmware.py`.

Los siguientes puntos requieren las placas físicas **Raspberry Pi Pico W** y sensores reales para su calibración final y están marcados con `# TODO-HW` en `firmware/common/pins.py`, `firmware/common/hal.py` y `firmware/central/main.py`:

## 1. Calibración de Celdas de Carga HX711 (`colchon` y `biberon`)
- **Archivos**: `firmware/common/pins.py`, `firmware/common/hal.py` (`Hx711LoadCellAdapter`)
- **Procedimiento físico**:
  1. Con la celda sin carga, leer el promedio de 20 muestras de 24 bits y fijar `offset_raw`.
  2. Colocar una masa patrón conocida (p. ej., `500 g` en el colchón de 1 kg y `200 g` en la base del biberón) y calcular `counts_per_kg`.
  3. Verificar que los pulsos de reloj en `PD_SCK` duren `< 50 µs` en alto para no inducir el modo *power-down* del HX711.

## 2. Confirmación del Sensor Ambiental de la Central (`AHT10/AHT20` vs `DHT22`)
- **Archivos**: `firmware/common/pins.py` (`CENTRAL_PINS["env_sensor_type"]`), `firmware/common/hal.py` (`TemperatureHumiditySensorAdapter`)
- **Procedimiento físico**:
  1. Si se entrega el módulo I2C **AHT10/AHT20** (dirección `0x38` en `GP4`/`GP5`), usar `"AHT20"` para cumplir holgadamente con `IH-02` (`±0.3 °C`, `±3 % HR`).
  2. Si se utiliza **DHT22** en `GP6`, cambiar `env_sensor_type = "DHT22"`.

## 3. Verificación Acústica (`≤ 65 dBA`) y Filtro RC + PAM8403 (`central`)
- **Archivos**: `firmware/central/main.py`, `firmware/common/pins.py`
- **Procedimiento físico**:
  1. Conectar la salida PWM `GP16` al filtro pasa-bajos RC (`R = 1.8 kΩ`, `C = 10 nF`, $f_c \approx 8.8\text{ kHz}$) y a la entrada del amplificador **PAM8403** con parlante de `8 Ω`.
  2. Medir con sonómetro a la distancia de la cuna y ajustar el potenciómetro físico del PAM8403 para que ni las melodías (`song_play`) ni el intercomunicador (`audio_chunk`) ni la alarma local superen **65 dBA** (`RNF08`).

## 4. Verificación de Alimentación 3.3 V en Termómetro IR `MLX90614` (`biberon`)
- **Archivos**: `firmware/common/pins.py` (`BIBERON_PINS`), `firmware/common/hal.py` (`Mlx90614IrThermometerAdapter`)
- **Procedimiento físico**:
  1. Confirmar con multímetro que el módulo **GY-906 (MLX90614)** incorpore regulador LDO en placa o sea versión de `3.3 V` antes de conectarlo a los pines I2C (`GP4`/`GP5`) de la Pico W (el RP2040 no tolera `5 V` en sus GPIOs).

## 5. Divisor Resistivo ADC para Batería de 3 Pilas AA (`biberon` y `juguete`)
- **Archivos**: `firmware/common/hal.py` (`BatteryAdcAdapter`)
- **Procedimiento físico**:
  1. Conectar el paquete de 3 pilas AA (`3.0 V` descargadas a `4.5 V` nuevas) a `GP26 (ADC0)` mediante un divisor resistivo `2:1` (p. ej., `10 kΩ + 10 kΩ`) para que el voltaje en el pin ADC nunca supere `3.3 V`.
