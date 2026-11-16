# Cuna Inteligente

Proyecto de Modelado y Programación (Ingeniería en Computadores, 1.er año).
Es un sistema de supervisión para cuna con 5 nodos en Raspberry Pi Pico W (MicroPython), un servidor central en C# (.NET 8), una aplicación web en React y almacenamiento en Supabase.

Las reglas del equipo y decisiones técnicas están en [AGENTS.md](AGENTS.md) y [docs/PROJECT_CONTEXT.md](docs/PROJECT_CONTEXT.md).

## Cómo funciona

```text
  Web React (:5173)  <---- HTTPS / Realtime ---->  Supabase (Auth, PostgreSQL + RLS)
         |                                                        ^
         | HTTP (:5080, JWT)                                      | sb_secret_ (solo backend)
         v                                                        |
  Backend .NET 8 (:5080 API / :9000 TCP) <------------------------+
         |                                      |
         | TCP por líneas JSON (:9000)          | HTTPS Bot API
         v                                      v
  5 nodos Pico W o simuladores Python      Bot de Telegram
```

- Los dispositivos (físicos o simulados) se conectan por TCP al puerto `9000` y se autentican con `hello` en menos de 5 s.
- El backend deduce `familia_id` y `cuna_id` desde la base de datos según el usuario o dispositivo autenticado, sin confiar en los IDs que vengan en el cuerpo del mensaje.
- La web muestra las lecturas en tiempo real desde Supabase y manda acciones de control al backend (`:5080`).
- Si ocurre una alerta (retiro súbito del colchón, temperatura o humedad fuera de rango, desconexión o batería baja), el servidor cambia el estado de la cuna y avisa por Telegram.

## Estructura del proyecto

- `backend/CunaServer/`: API REST (`Api/`), lógica de cuna, alimentación y exportación CSV/PDF (`Services/`), servidor TCP (`Tcp/`), bot de Telegram (`Telegram/`) y repositorios InMemory/PostgREST (`Data/`).
- `backend/CunaServer.Tests/`: 54 pruebas unitarias y de integración en xUnit.
- `frontend/`: aplicación web en React, TypeScript, Vite y Tailwind CSS (`src/components/`, `src/hooks/`, `src/lib/api/`).
- `firmware/`: código MicroPython para las 5 Pico W (`central`, `colchon`, `movil`, `biberon`, `juguete`), abstracción de pines/HAL en `common/` y pruebas en PC (`test_firmware.py`).
- `simulators/`: simuladores en Python 3 para probar todo sin placas físicas (`run_simulators.py`, `protocol_client.py`, `test_simulators.py`).
- `scripts/`: scripts de ayuda (`verify_environment.py`, `test_telegram.py`, `provision_demo_devices.py` y utilidades PowerShell).
- `supabase/`: migración inicial con 18 tablas y políticas RLS (`migrations/`), datos de prueba (`seed.sql`) y pruebas de aislamiento (`tests/isolation_test.sql`).
- `docs/`: documentación de arquitectura, protocolo, seguridad, pruebas físicas pendientes y presentaciones en HTML.

Para ver el detalle interno por módulos revisa [docs/GUIA_DESARROLLADOR.md](docs/GUIA_DESARROLLADOR.md).

## Requisitos e instalación

Necesitas tener instalado:
- .NET SDK 8.0+
- Node.js 18+ y `npm`
- Python 3.10+
- Thonny o `mpremote` (solo si vas a cargar el firmware en las Pico W)

Antes de correr el proyecto con credenciales reales, copia las plantillas de configuración (nunca subas los archivos reales a Git):
- `backend/.env.example` -> `backend/.env` (si falta, el backend arranca en modo `InMemory` para pruebas locales)
- `frontend/.env.example` -> `frontend/.env`
- `firmware/secrets.example.py` -> `firmware/secrets.py` (para Pico W física: usa Wi-Fi de 2.4 GHz, la IP privada de la laptop y abre el puerto TCP `9000` en red privada)

## Correr en local

**Terminal 1 — Backend (.NET 8):**
```bash
dotnet run --project backend/CunaServer/CunaServer.csproj
```
Puedes comprobar que levantó en `http://localhost:5080/health`.

**Terminal 2 — Web (React + Vite):**
```bash
cd frontend
npm install
npm run dev
```
La interfaz abre en `http://localhost:5173`.

**Terminal 3 — Simuladores (sin hardware físico):**
```bash
python3 -m simulators.run_all --host 127.0.0.1 --port 9000
```
También puedes pasar `--device colchon` para correr un solo nodo, `--once` para un único ciclo, o usar `simulators/devices.local.json` si emparejaste dispositivos en la web.

## Pruebas

Para verificar todo el repositorio en un solo paso:
```bash
python3 scripts/verify_environment.py
```

O por separado:
```bash
dotnet test backend/CunaServer.sln
python3 -m unittest discover -s simulators -p "test_*.py" -v
python3 -m unittest discover -s firmware -p "test_*.py" -v
python3 scripts/test_telegram.py --mock
cd frontend && npm test && npm run lint && npm run build
```

## Notas rápidas de depuración

- **La Pico W no entra a `:9000`**: revisa que `secrets.py` tenga la IP privada de la laptop (no `localhost`), que el hotspot sea de 2.4 GHz sin aislamiento de clientes y que el firewall permita tráfico en el puerto `9000`.
- **Cierre inmediato por `hello_rechazado`**: el token cambió, el dispositivo fue revocado o el handshake tardó más de 5 s; vuelve a emparejar desde la web para obtener un token nuevo.
- **Bot de Telegram no contesta**: revisa `TELEGRAM_BOT_TOKEN` en `backend/.env` y corre `python3 scripts/test_telegram.py --probe-network` y `--delete-webhook`. La persona debe enviar `/start` al bot antes de recibir mensajes.
- **No dispara retiro del muñeco en maqueta**: la celda de carga de 1 kg usa `mass_scale=0.2`; espera caída de `>= 0.50 kg` a `< 0.20 kg` en `<= 1 s` y la cuna debe estar en modo `monitoreo` (no en `atencion`).
- **Calibración en hardware real**: revisa las marcas `TODO-HW` detalladas en [docs/HARDWARE_PENDING_TESTS.md](docs/HARDWARE_PENDING_TESTS.md).

## Enlaces útiles

- Guion de demo: [docs/DEMO_GUIDE.md](docs/DEMO_GUIDE.md)
- Guía interna del código: [docs/GUIA_DESARROLLADOR.md](docs/GUIA_DESARROLLADOR.md)
- Reporte técnico: [docs/REPORTE_TECNICO.md](docs/REPORTE_TECNICO.md)
- Pruebas físicas pendientes: [docs/HARDWARE_PENDING_TESTS.md](docs/HARDWARE_PENDING_TESTS.md)
- Checklist de seguridad: [docs/SECURITY.md](docs/SECURITY.md)
- Guías visuales HTML: [usuario](docs/presentaciones/guia_usuario_cuna_inteligente.html) · [operador](docs/presentaciones/guia_visual_operador_cuna_inteligente.html)
