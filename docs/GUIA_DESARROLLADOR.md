# Guía del Desarrollador — Mapa del Código y Flujos de Cuna Inteligente

Esta guía complementa al [README.md](../README.md) para que cualquier integrante del equipo entienda rápido dónde vive cada responsabilidad, cómo fluye un mensaje de extremo a extremo y cómo probar cada capa sin depender del hardware físico.

---

## 1. Principios de diseño obligatorios

Antes de modificar cualquier módulo, ten presentes estas invariantes definidas en [AGENTS.md](../AGENTS.md) y [PROJECT_CONTEXT.md](PROJECT_CONTEXT.md):

1. **Fuente de verdad en el servidor (`D10`)**: cada nodo Pico W evalúa las reglas rápidas de sus sensores (retiro `<= 1 s`, desvío T/H `>= 10 s`, antirrebote táctil), pero el backend C# decide el estado oficial (`monitoreo`, `atencion`, `alarma`), la ventana de gracia de `5 s`, el watchdog (`60 s`) y las alertas de Telegram.
2. **Aislamiento multi-familia (`AGENTS.md` #3 y #4)**: el backend deduce `familia_id` y `rol` desde la base de datos según el JWT o el `device_id` autenticado; jamás confía en un `family_id` enviado en JSON.
3. **Escala de masa en maqueta (`D14`)**: la celda de carga física es de `1 kg`, por lo que se aplica `mass_scale = 0.2` (presencia `> 0.20 kg`, retiro desde `>= 0.50 kg` a `< 0.20 kg` en `<= 1 s`). Nunca aplicar más de `1 kg`.
4. **Límite acústico (`RNF08` / `RF11`)**: el audio del intercomunicador (PCM 8-bit `8 kHz`) se limita digitalmente en backend (`MaxSafeGain = 0.65`) y se calibra en hardware con potenciómetro del PAM8403 (`<= 65 dBA`, marcado con `TODO-HW`).
5. **Compatibilidad MicroPython en `firmware/`**: los módulos bajo `firmware/` evitan `typing`, `dataclasses` y anotaciones de tipo modernas para ejecutarse sin cambios tanto en RP2040 (MicroPython) como en PC (CPython 3).

---

## 2. Mapa detallado por capa

### A. Firmware MicroPython (`firmware/`)
- **`firmware/common/pins.py`**: única fuente de verdad para asignaciones GPIO (`0..28`) de los 5 nodos (`CENTRAL_PINS`, `COLCHON_PINS`, `MOVIL_PINS`, `BIBERON_PINS`, `JUGUETE_PINS`).
- **`firmware/common/hal.py`**: adaptadores de hardware (`WatchdogAdapter`, `PwmOutputAdapter`, `DigitalPinAdapter`, `Hx711LoadCellAdapter`, `TemperatureHumiditySensorAdapter`, `Mlx90614IrThermometerAdapter`, `BatteryAdcAdapter`). En Pico usan `machine`/`utime`; en PC exponen métodos `inject_mock_*`.
- **`firmware/common/protocol_and_buffer.py`**: codificación JSON-línea (`<= 4096 B`), reloj sincronizable (`ServerClock`) y búfer circular offline `OfflineBuffer` de hasta `2880` registros (`48 h` a `1/min`) en RAM (PC) o archivo JSONL en flash (Pico W) con reserva por bloques del contador `seq`.
- **`firmware/common/device_runtime.py`**: clase base `DeviceNode` y cliente asíncrono `DeviceClient` (`hello` en `<= 5 s`, vaciado ordenado de búfer, `heartbeat` cada `15 s`, reconexión exponencial `1..30 s`).
- **Nodos (`main.py` de cada dispositivo)**:
  - `firmware/central/main.py`: `CentralNode` + `IntercomAudioPlayer` (T/H 10 s, rampa PWM luz nocturna 1.5 s, LED RGB, botón táctil TTP223, prioridad de audio `alarma > intercomunicador > canción`).
  - `firmware/colchon/main.py`: `ColchonNode` (muestreo 5 Hz, detección retiro `<= 1 s`, índice de movimiento filtrando ruido y saltos bruscos, alerta sostenida 30 s).
  - `firmware/movil/main.py`: `MovilNode` (patrones sobre 9 LEDs y evento `mobile_session_end`).
  - `firmware/biberon/main.py`: `BiberonNode` (HX711 en gramos, MLX90614 por I2C, batería ADC).
  - `firmware/juguete/main.py`: `JugueteNode` (juego de memoria/reacción con 5 botones/LEDs, buzzer, batería `< 10 %`).

### B. Simuladores Python 3 (`simulators/`)
- **`simulators/protocol_client.py`**: `OfflineRingBuffer`, `MattressRuleEngine`, `CentralEnvRuleEngine` y `CentralAudioPlayer` (implementación de referencia en CPython 3 con `typing` y `dataclasses`).
- **`simulators/run_simulators.py`** y **`simulators/run_all.py`**: ejecutor `asyncio` que levanta de 1 a 5 nodos simulados contra `:9000` (`DeviceSimulatorNode`), procesando `hello_ack`, comandos intercalados, vaciado de búfer y ciclo de telemetría.

### C. Scripts de verificación (`scripts/`)
- **`scripts/verify_environment.py`**: valida versiones (`.NET 8`, `Node`, `npm`, `Python 3`), escanea secretos en Git y ejecuta las suites de backend, simuladores, firmware, frontend y Telegram mock.
- **`scripts/test_telegram.py`**: pruebas seguras de la Bot API sin exponer `TELEGRAM_BOT_TOKEN` (`--mock` con servidor HTTP local, `--probe-network` TLS, `--delete-webhook` y envío opcional con `--chat-id`).

### D. Backend C# (`backend/CunaServer/`) y Frontend (`frontend/src/`)
- **Backend**: organizado en `Api/` (endpoints REST), `Services/` (reglas de cuna, alimentación, juguetes y exportación CSV/PDF), `Tcp/` (servidor TCP `:9000` y parser de sobres v1), `Telegram/` (bot por sondeo `getUpdates`) y `Data/` (repositorios PostgREST e InMemory).
- **Frontend**: organizado en `src/App.tsx`, `src/components/` (paneles por dominio: cuna en vivo, gestión de dispositivos, alimentación/juguetes/historiales, autenticación) y `src/lib/` (cliente HTTP tipado, cliente Supabase y formateadores).

---

## 3. Recorrido de un flujo completo (ejemplo: retiro súbito `RF04`)

1. **Sensor / Simulador (`colchon`)**: detecta caída de masa de `>= 0.50 kg` a `< 0.20 kg` en `<= 1.0 s` (`mass_scale = 0.2`) y encola `{"t":"event","d":{"kind":"removal",...}}`.
2. **Transporte TCP (`device_runtime.py` → `Tcp/`:9000)**: envía el sobre con `seq` monotónico. El servidor confirma con `ack` y deduce `familia_id` y `cuna_id` desde la BD.
3. **Lógica central (`Services/`)**: si la cuna está en `monitoreo`, abre una ventana de gracia de `5 s`. Si no se activa `atencion`, cambia el estado a `alarma`, registra el evento en Supabase, despacha `command: alarm_set (on=true)` hacia `central` y envía alerta crítica por `Telegram/`.
4. **Actuador (`central/main.py`)**: `CentralNode` activa el tono de alarma local y pone el LED RGB en rojo.
5. **Interfaz Web (`frontend/`)**: recibe el cambio por Supabase Realtime (`< 1 s`) y muestra la alerta activa en el panel en vivo.

---

## 4. Cómo probar cambios en PC sin hardware

```bash
# 1. Reglas de firmware y HAL simulado (7 pruebas)
python3 -m unittest discover -s firmware -p "test_*.py" -v

# 2. Protocolo TCP y reglas de simuladores (10 pruebas)
python3 -m unittest discover -s simulators -p "test_*.py" -v

# 3. Roundtrip local de Bot API Telegram sin red ni secretos
python3 scripts/test_telegram.py --mock

# 4. Suite integral de todo el repositorio
python3 scripts/verify_environment.py
```

Para pruebas que requieren placas físicas Pico W y sensores reales, sigue el checklist de [docs/HARDWARE_PENDING_TESTS.md](HARDWARE_PENDING_TESTS.md).
