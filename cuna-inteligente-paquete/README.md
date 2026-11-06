# Cuna Inteligente
Proyecto universitario. Documentación en `docs/`. Reglas para agentes de IA en `AGENTS.md`.

## Requisitos
.NET SDK (LTS), Node.js (LTS), Python 3.11+, Thonny o `mpremote` (para las Pico).

## Configuración local (una sola vez)
1. Copia `backend/.env.example` → `backend/.env` y `frontend/.env.example` → `frontend/.env`; rellena los valores (los privados los guarda quien ejecuta el backend). **Nunca subas esos archivos.**
2. Para Pico y simuladores: `firmware/secrets.example.py` → `secrets.py`.
3. Hotspot del teléfono en **2.4 GHz**; la laptop y las Pico en esa misma red. Las Pico usan la **IP privada de la laptop**, no `localhost`. Permite el puerto TCP 9000 en el firewall (solo red privada).

## Carpetas
`backend/` C# · `frontend/` React · `firmware/` MicroPython por dispositivo · `simulators/` Python · `supabase/migrations/` SQL · `docs/` documentación.
