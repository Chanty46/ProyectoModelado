# Guía del desarrollador

Notas rápidas para ubicarse en el código de **Cuna Inteligente** sin perder tiempo buscando dónde está cada cosa.

## Reglas base

1. **El servidor decide el estado**: los nodos Pico W evalúan reglas rápidas locales (retiro `<= 1 s`, desvío ambiental `>= 10 s`, botón táctil), pero el backend C# decide el estado oficial de la cuna (`monitoreo`, `atencion`, `alarma`), la ventana de gracia de 5 s, el watchdog de 60 s y las alertas a Telegram.
2. **Aislamiento por familia**: el backend obtiene `familia_id` y `rol` desde la base de datos usando el JWT o el `device_id` autenticado; nunca toma el `family_id` que venga en un JSON del cliente.
3. **Escala de masa de la maqueta**: la celda física es de 1 kg (`mass_scale = 0.2`). Presencia `> 0.20 kg`; retiro de `>= 0.50 kg` a `< 0.20 kg` en `<= 1 s`.
4. **Límite de audio**: el intercomunicador (PCM 8-bit a 8 kHz) aplica `MaxSafeGain = 0.65` en el backend y se calibra en físico con el potenciómetro del PAM8403 (`<= 65 dBA`, marcado como `TODO-HW`).
5. **MicroPython**: los archivos en `firmware/` no usan `typing` ni `dataclasses` para poder correr tanto en la RP2040 como en Python 3 en PC.

## Dónde está cada parte

### Backend (`backend/CunaServer/`)
- `Api/`: endpoints REST agrupados por módulo (`AccountAndFamilyEndpoints`, `PairingAndCribEndpoints`, `CribLiveControlEndpoints`, `FeedingHistoryAndToysEndpoints`, `TelegramAndRemindersEndpoints`, `IntercomEndpoints`, `HealthEndpoints`).
- `Data/`:
  - Contratos en `IFamilyAccountRepository.cs` y `IDeviceTelemetryRepository.cs`.
  - Implementaciones en memoria: `InMemoryFamilyAccountRepository.cs` e `InMemoryDeviceTelemetryRepository*.cs`.
  - Implementaciones PostgREST para Supabase: `PostgrestClientHelper.cs`, `PostgrestFamilyAccountRepository.cs` y `PostgrestDeviceTelemetryRepository*.cs`.
- `Services/`: máquina de estados y cuna en vivo (`CribLiveControlService.cs`), alimentación y juguetes (`FeedingHistoryAndToysService.cs`), exportación CSV/PDF (`HistoryReportExporter.cs`), audio (`IntercomAudioService.cs`), validación JWT (`JwtTokenValidator.cs`), cuentas y emparejamiento.
- `Tcp/`: servidor local en `:9000` (`DeviceTcpServer.cs`) y parser de sobres JSON por línea `<= 4096 B` (`TcpProtocolParser.cs`).
- `Telegram/`: lógica de comandos y alertas (`TelegramBotService.cs`), textos del bot (`TelegramMessages.cs`) y transporte HTTP/InMemory (`TelegramBotTransport.cs`).

### Frontend (`frontend/src/`)
- `App.tsx`: rutas (`/` y `/invitacion/:token`), guardia de sesión y pestañas principales.
- `hooks/`: `useAuthSession.ts`, `useBackendHealth.ts`, `useLiveCribTelemetry.ts`.
- `lib/api/`: cliente HTTP base (`client.ts`) y llamadas por módulo (`account.ts`, `cribs.ts`, `telemetry.ts`, `feeding.ts`, `telegram.ts`), re-exportadas desde `src/lib/api.ts`.
- `components/`: paneles principales y subcomponentes organizados en `auth/`, `family/`, `cribs/`, `live/` y `feeding/`.

### Firmware y simuladores (`firmware/` y `simulators/`)
- `firmware/common/pins.py`: todos los GPIO (`0..28`) en un solo archivo.
- `firmware/common/hal.py`: adaptadores para hardware real en Pico W o datos simulados en PC (`inject_mock_*`).
- `firmware/common/protocol_and_buffer.py`: protocolo JSON-línea y búfer circular de hasta `2880` registros (48 h).
- `firmware/common/device_runtime.py`: conexión TCP, `hello`, vaciado del búfer y reconexión con espera exponencial (`1..30 s`).
- `firmware/<nodo>/main.py`: lógica de `central`, `colchon`, `movil`, `biberon` y `juguete`.
- `simulators/protocol_client.py`, `run_simulators.py`, `run_all.py`: simuladores en Python 3 (aceptan `devices.local.json` con dispositivos reales emparejados desde la web).

### Scripts (`scripts/`)
- `verify_environment.py`: corre chequeos de herramientas, escaneo de secretos y todas las suites de prueba.
- `test_telegram.py`: prueba mock HTTP (`--mock`), conectividad HTTPS (`--probe-network`), `--delete-webhook` y envío manual con `--chat-id`.
- `provision_demo_devices.py`, `setup_local.py`: utilidades multiplataforma (Windows/Linux) para configurar `.env` y tokens de demo con Supabase real.

## Ejemplo rápido: flujo de una alerta de retiro

1. `firmware/colchon/main.py` detecta caída de masa de `>= 0.50 kg` a `< 0.20 kg` en `<= 1 s` y envía evento `removal`.
2. `DeviceTcpServer.cs` confirma con `ack` y deduce `familia_id` y `cuna_id` desde BD.
3. `CribLiveControlService.cs` abre la ventana de gracia de `5 s`; si no se activa Modo Atención, pasa la cuna a `alarma`, guarda el evento, ordena `alarm_set` al nodo `central` y dispara aviso en `TelegramBotService.cs`.
4. `frontend/src/hooks/useLiveCribTelemetry.ts` actualiza la pantalla en vivo por Realtime.

## Pruebas en PC sin hardware

```bash
python3 -m unittest discover -s firmware -p "test_*.py" -v
python3 -m unittest discover -s simulators -p "test_*.py" -v
python3 scripts/test_telegram.py --mock
python3 scripts/verify_environment.py
```

Los ajustes pendientes en maqueta física están listados en [docs/HARDWARE_PENDING_TESTS.md](HARDWARE_PENDING_TESTS.md).
