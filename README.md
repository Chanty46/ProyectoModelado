# Cuna Inteligente

**Ecosistema Ciberfísico para Supervisión Perimetral, Microclima y Nutrición Infantil** (proyecto universitario de 1.er año de Ingeniería en Computadores).

Integra **5 nodos IoT** en Raspberry Pi Pico W (MicroPython, con simuladores equivalentes en Python 3), un **backend central en C# (.NET 8)** con servidor TCP y bot de Telegram, una **aplicación web en React + TypeScript** y persistencia/autenticación en **Supabase (PostgreSQL + RLS + Realtime)**.

> Para reglas de desarrollo, seguridad y convenciones del repositorio consulta [AGENTS.md](AGENTS.md) y [docs/PROJECT_CONTEXT.md](docs/PROJECT_CONTEXT.md).

---

## 1. Resumen del ecosistema y cómo se conectan las piezas

```text
  ┌──────────────────────────┐        HTTPS / Realtime        ┌──────────────────────────────────┐
  │  Web React + TypeScript  │ ◀────────────────────────────▶ │  Supabase (Auth, Postgres+RLS)   │
  │  frontend/ (:5173)       │                                └──────────────────────────────────┘
  └─────────────┬────────────┘                                                 ▲
                │ HTTP (:5080, JWT)                                            │ sb_secret_ (solo backend)
                ▼                                                              │
  ┌────────────────────────────────────────────────────────────────────────────┴─────────────────┐
  │  Backend C# (.NET 8) — backend/CunaServer/                                                   │
  │   ├─ Api/ (:5080)       Endpoints autenticados (comandos, emparejamiento, CSV/PDF, familia)  │
  │   ├─ Services/          Máquina de estados de cuna, gracia 5 s, watchdog 60 s, recordatorios │
  │   ├─ Tcp/ (:9000)       Servidor línea-JSON <= 4 KB, auth hello, deduplicación por seq       │
  │   ├─ Telegram/          Sondeo getUpdates + alertas a chats vinculados (@Bot)                │
  │   └─ Data/              Repositorios PostgREST / InMemory                                    │
  └─────────────┬──────────────────────────────────────────────────────────────┬─────────────────┘
                │ TCP línea-JSON (Wi-Fi hotspot o 127.0.0.1:9000)              │ HTTPS getUpdates / sendMessage
                ▼                                                              ▼
  ┌──────────────────────────────────────────────────────┐          ┌────────────────────────────┐
  │  5 Nodos IoT (firmware/ en Pico W o simulators/)     │          │  Bot de Telegram           │
  │   1. central  -> T/H, luz PWM, RGB, botón, audio PCM │          │  @CunasInteligentes_...    │
  │   2. colchon  -> celda 1 kg + HX711 (retiro y 5 Hz)  │          └────────────────────────────┘
  │   3. movil    -> 9 LEDs (patrones y duración)        │
  │   4. biberon  -> HX711 + IR MLX90614 + batería       │
  │   5. juguete  -> 5 botones/LEDs + buzzer + batería   │
  └──────────────────────────────────────────────────────┘
```

**Flujo resumido de datos y control:**
1. Los nodos (reales en Pico W o simulados en Python) se conectan por TCP al puerto `:9000` y se autentican con `hello` en `<= 5 s`.
2. El servidor C# deduce `familia_id` y `cuna_id` desde la base de datos según el dispositivo/usuario autenticado (nunca confía en IDs enviados en el cuerpo).
3. El navegador lee telemetría y recibe actualizaciones en tiempo real desde Supabase (con RLS por membresía en `usuarios`), y envía acciones administrativas o comandos de control al backend en `:5080`.
4. Ante retiro súbito (`RF04`), desvíos ambientales (`RF07`), batería baja o desconexión (`RF20`), el backend actualiza la cuna, dispara la alarma en `central` y notifica a los chats vinculados en Telegram.

---

## 2. Mapa de carpetas y módulos clave

| Carpeta / Archivo | Qué vive aquí | Detalles principales |
|---|---|---|
| `backend/CunaServer/` | Servidor central .NET 8 (`:5080` HTTP, `:9000` TCP) | `Api/` (endpoints), `Services/` (estado de cuna, exportación CSV/PDF, watchdog, recordatorios), `Tcp/` (servidor y parser JSON-línea), `Telegram/` (bot por sondeo), `Data/` (acceso PostgREST e InMemory). |
| `backend/CunaServer.Tests/` | Pruebas unitarias e integración xUnit | 54 pruebas cubriendo seguridad, TCP, Telegram, alimentación, reportes y flujo E2E. |
| `frontend/` | SPA web React 18 + Vite + Tailwind + shadcn/ui + Recharts | `src/App.tsx`, `src/components/` (paneles en vivo, gestión de cunas, alimentación/juguetes/historial), `src/lib/` (cliente API, formateadores, Supabase). |
| `firmware/` | Programas MicroPython para las 5 Raspberry Pi Pico W | `central/`, `colchon/`, `movil/`, `biberon/`, `juguete/`, más `common/` (`pins.py`, `hal.py`, `device_runtime.py`, `protocol_and_buffer.py`) y `test_firmware.py` (ejecutable en PC). |
| `simulators/` | Simuladores Python 3 para probar sin placas físicas | `protocol_client.py` (reglas puras y búfer 48 h), `run_simulators.py`, `run_all.py` y `test_simulators.py`. |
| `scripts/` | Herramientas CLI de diagnóstico y verificación | `verify_environment.py` (suite integral previa a demo) y `test_telegram.py` (mock local y diagnóstico HTTPS de Telegram). |
| `supabase/` | Esquema PostgreSQL y aislamiento multi-familia | `migrations/` (14 tablas con RLS forzado), `seed.sql` y `tests/isolation_test.sql`. |
| `docs/` | Documentación técnica, guion de demo y guías | Arquitectura, protocolo TCP, seguridad, pruebas físicas pendientes (`TODO-HW`) y presentaciones HTML interactivas. |

> Para profundizar en cómo se comunican los módulos paso a paso, revisa la [Guía del Desarrollador](docs/GUIA_DESARROLLADOR.md).

---

## 3. Requisitos y configuración local (una sola vez)

### Herramientas necesarias
- **.NET SDK 8.0+** (LTS)
- **Node.js 18/20/22+** y `npm`
- **Python 3.10+** (recomendado 3.11+)
- **Thonny** o `mpremote` (solo si se cargará firmware a las Raspberry Pi Pico W reales)

### Configuración inicial
1. Copia las plantillas de variables de entorno y rellénalas localmente (**nunca subas estos archivos a Git**):
   - `backend/.env.example` → `backend/.env` (incluye `sb_secret_...` y `TELEGRAM_BOT_TOKEN`; si se omiten, el backend puede correr en modo demostración InMemory).
   - `frontend/.env.example` → `frontend/.env` (solo `VITE_SUPABASE_URL`, `VITE_SUPABASE_PUBLISHABLE_KEY` y `VITE_API_BASE_URL`).
   - `firmware/secrets.example.py` → `firmware/secrets.py` (SSID Wi-Fi, IP privada de la laptop, `DEVICE_ID` y `DEVICE_TOKEN`).
2. Si usas Pico W reales en maqueta:
   - Usa un hotspot Wi-Fi de **2.4 GHz** con comunicación entre clientes habilitada.
   - Configura en `secrets.py` la **IP privada de la laptop** (nunca `localhost`).
   - Permite el puerto TCP `9000` en el firewall únicamente para red privada.

---

## 4. Arranque rápido y pruebas

### Verificación integral en un comando (recomendado antes de cada demo)
```bash
python3 scripts/verify_environment.py
```
Comprueba versiones de herramientas, escaneo de secretos en Git, pruebas .NET, pruebas de simuladores, pruebas de firmware, tests/typecheck/build de React y simulación HTTP de Telegram.

### Arranque del sistema completo en local (3 terminales)

1. **Terminal 1 — Backend C# + Servidor TCP (`:5080` y `:9000`)**:
   ```bash
   dotnet run --project backend/CunaServer/CunaServer.csproj
   ```
   Comprueba salud en `http://localhost:5080/health`.

2. **Terminal 2 — Aplicación Web React + Vite (`:5173`)**:
   ```bash
   cd frontend
   npm install
   npm run dev
   ```
   Abre `http://localhost:5173`.

3. **Terminal 3 — Simuladores de los 5 nodos IoT (sin hardware)**:
   ```bash
   python3 -m simulators.run_all --host 127.0.0.1 --port 9000
   ```
   *(Opciones útiles: `--device colchon` para correr un solo nodo, `--once` para un ciclo único, `--telemetry-interval 10` para telemetría más frecuente en demo).*

### Pruebas automatizadas por capa
```bash
# Backend .NET (54 pruebas)
dotnet test backend/CunaServer.sln

# Simuladores Python (10 pruebas)
python3 -m unittest discover -s simulators -p "test_*.py" -v

# Firmware MicroPython en modo HAL simulado (7 pruebas)
python3 -m unittest discover -s firmware -p "test_*.py" -v

# Cliente Telegram Bot API en modo mock HTTP local (sin token real)
python3 scripts/test_telegram.py --mock

# Frontend React (43 pruebas + verificación TypeScript + compilación Vite)
cd frontend && npm test && npm run lint && npm run build
```

---

## 5. Guía de depuración rápida ("cómo entender y arreglar un problema")

| Síntoma | Causa probable | Dónde revisar | Comando / acción rápida |
|---|---|---|---|
| La Pico W no conecta al backend TCP `:9000` | Usó `localhost`, hotspot en 5 GHz, aislamiento de clientes o firewall bloqueando `:9000`. | `firmware/secrets.py` (local) y firewall de red privada. | Verificar IP privada de la laptop y probar desde la misma red con `python3 -m simulators.run_all --host <IP> --port 9000 --once`. |
| El servidor cierra la conexión TCP al instante (`hello_rechazado`) | `DEVICE_ID` / `DEVICE_TOKEN` incorrecto, token revocado o `hello` tardó más de `5 s`. | `docs/PROTOCOL.md`, `backend/CunaServer/Tcp/` y `firmware/common/device_runtime.py`. | Reemparejar el dispositivo desde la web para obtener token nuevo (se muestra una sola vez) y validar en `scripts/verify_environment.py`. |
| El botón/endpoint responde `403 Forbidden` | Usuario pertenece a otra familia, rol Secundario intentó acción de Principal, o membresía revocada. | `supabase/migrations/` (políticas RLS) y `backend/CunaServer/Api/`. | Verificar rol en tabla `usuarios`; recordar que el backend ignora cualquier `family_id` enviado por el cliente. |
| El bot de Telegram no responde a `/estado` o `/vincular` | Falta `TELEGRAM_BOT_TOKEN` en `backend/.env`, hay un webhook activo o el usuario no envió `/start` primero. | `scripts/test_telegram.py` y `backend/CunaServer/Telegram/`. | Ejecutar `python3 scripts/test_telegram.py --probe-network` y `python3 scripts/test_telegram.py --delete-webhook`. Telegram no permite enviar por número telefónico sin `/start` previo. |
| La web no muestra datos o falla `npm run lint` | `frontend/.env` ausente, proyecto Supabase pausado por inactividad, o error de tipos TS. | `frontend/.env.example` y panel de Supabase (`rjklzbarjuaxdhryklzw`). | Entrar al panel de Supabase si estuvo inactivo > 7 días; correr `cd frontend && npm test && npm run lint`. |
| Alarma por retiro no suena en maqueta con muñeco | Masa del muñeco fuera de escala `mass_scale=0.2` (debe ser `0.50–0.80 kg` cayendo a `< 0.20 kg` en `<= 1 s`) o Modo Atención activo. | `firmware/colchon/main.py`, `simulators/protocol_client.py` y `docs/PROJECT_CONTEXT.md` (§5b). | Confirmar que la cuna esté en modo `monitoreo` y nunca superar `1.0 kg` en la celda física. |
| Fallo de hardware físico pendiente de calibrar | Parámetro marcado con `TODO-HW` (tara HX711, sensor AHT20 vs DHT22, tope 65 dBA, 3.3 V en GY-906). | `firmware/common/pins.py`, `firmware/common/hal.py` y `firmware/central/main.py`. | Consultar el procedimiento exacto en `docs/HARDWARE_PENDING_TESTS.md`. |

---

## 6. Documentación y presentaciones

- **Guion de demostración en vivo**: [docs/DEMO_GUIDE.md](docs/DEMO_GUIDE.md)
- **Guía técnica del desarrollador (mapa detallado y flujos)**: [docs/GUIA_DESARROLLADOR.md](docs/GUIA_DESARROLLADOR.md)
- **Reporte técnico completo**: [docs/REPORTE_TECNICO.md](docs/REPORTE_TECNICO.md)
- **Pruebas físicas pendientes en hardware (`TODO-HW`)**: [docs/HARDWARE_PENDING_TESTS.md](docs/HARDWARE_PENDING_TESTS.md)
- **Reglas y checklist de seguridad**: [docs/SECURITY.md](docs/SECURITY.md)
- **Contexto, protocolo y arquitectura**: [docs/PROJECT_CONTEXT.md](docs/PROJECT_CONTEXT.md) · [docs/PROTOCOL.md](docs/PROTOCOL.md) · [docs/IMPLEMENTATION_ARCHITECTURE.md](docs/IMPLEMENTATION_ARCHITECTURE.md)
- **Presentaciones visuales interactivas (HTML)**:
  - [Guía del Usuario — Cuna Inteligente](docs/presentaciones/guia_usuario_cuna_inteligente.html)
  - [Guía Visual del Operador — Cuna Inteligente](docs/presentaciones/guia_visual_operador_cuna_inteligente.html)
