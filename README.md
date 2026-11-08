# Cuna Inteligente

Proyecto universitario — Ecosistema Ciberfísico para Supervisión Perimetral, Microclima y Nutrición Infantil.
Documentación en `docs/`. Reglas para agentes de IA en `AGENTS.md` (apuntado por `CLAUDE.md` y `GEMINI.md`).

## Requisitos
- .NET SDK 8.0+ (LTS)
- Node.js 20/22+ (LTS)
- Python 3.11+
- Thonny o `mpremote` (para las Raspberry Pi Pico W)

## Configuración local (una sola vez)
1. Copia `backend/.env.example` → `backend/.env` y `frontend/.env.example` → `frontend/.env`; rellena los valores localmente. **Nunca subas esos archivos a Git.**
2. Para Pico y simuladores: `firmware/secrets.example.py` → `firmware/secrets.py`.
3. Hotspot del teléfono en **2.4 GHz**; la laptop y las Pico en esa misma red. Las Pico usan la **IP privada de la laptop**, no `localhost`. Permite el puerto TCP `9000` en el firewall (solo red privada).

## Ejecución y pruebas

### Backend (C# / ASP.NET Core en `:5080` y TCP en `:9000`)
```bash
dotnet build backend/CunaServer.sln
dotnet test backend/CunaServer.sln
dotnet run --project backend/CunaServer
```
Health check: `GET http://localhost:5080/health`

### Frontend (React + TypeScript + Vite en `:5173`)
```bash
cd frontend
npm install
npm run lint
npm run build
npm run dev
```

### Simuladores (Python 3) y Firmware (MicroPython / Python 3)
```bash
python3 -m unittest discover -s simulators -p "test_*.py" -v
python3 -m unittest discover -s firmware -p "test_*.py" -v
python3 -m simulators.run_all --host 127.0.0.1 --port 9000
```

## Estructura de carpetas
- `backend/CunaServer`: API Minimal (`Api/`), servidor TCP (`Tcp/`), lógica y servicios (`Services/`), bot de Telegram (`Telegram/`), acceso a datos (`Data/`).
- `backend/CunaServer.Tests`: suite de pruebas automatizadas con xUnit (M0 a M10).
- `frontend/`: aplicación web React + TypeScript + Tailwind CSS + shadcn/ui + Recharts.
- `firmware/`: programas MicroPython por dispositivo (`central`, `colchon`, `movil`, `biberon`, `juguete`) + `common/` (`pins.py`, `hal.py`, `protocol_and_buffer.py`).
- `simulators/`: simuladores en Python 3 para los 5 nodos.
- `supabase/`: migraciones SQL con políticas RLS (`migrations/`), semilla (`seed.sql`) y pruebas de aislamiento (`tests/isolation_test.sql`).
- `docs/`: contexto (`PROJECT_CONTEXT.md`), plan aprobado (`PLAN_APROBADO.md`), arquitectura, protocolo, pruebas de hardware (`HARDWARE_PENDING_TESTS.md`), guion de demostración (`DEMO_GUIDE.md`) y seguridad (`SECURITY.md`).
