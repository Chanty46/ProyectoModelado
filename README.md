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

### Backend (C# / ASP.NET Core en `:5080`)
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
npm run dev
```

## Estructura de carpetas
- `backend/CunaServer`: API Minimal (`Api/`), servidor TCP (`Tcp/`), lógica y servicios (`Services/`), bot de Telegram (`Telegram/`), acceso a datos (`Data/`).
- `backend/CunaServer.Tests`: pruebas automatizadas con xUnit.
- `frontend/`: aplicación web React + TypeScript + Tailwind CSS + shadcn/ui + Recharts.
- `firmware/`: programas MicroPython por dispositivo + `common/`.
- `simulators/`: simuladores en Python 3.
- `supabase/migrations/`: migraciones SQL y políticas RLS.
- `docs/`: contexto (`PROJECT_CONTEXT.md`), plan aprobado (`PLAN_APROBADO.md`), arquitectura, protocolo y seguridad.
