# AGENTS.md — Cuna Inteligente

Proyecto universitario (ingeniería en computadores, 1.er año). Sistema: "Ecosistema Ciberfísico para Supervisión Perimetral, Microclima y Nutrición Infantil".
Web (React) + backend C# (.NET) con servidor TCP + Supabase (PostgreSQL/Auth/Realtime) + bot de Telegram + 5 Raspberry Pi Pico W con MicroPython.
Demostración local en la laptop del responsable; no se publica en Internet.

## Orden de lectura (solo lo que la tarea necesite)
1. `docs/PROJECT_CONTEXT.md` — decisiones vigentes (anulan al ERS/diagramas donde difieran).
2. `docs/IMPLEMENTATION_ARCHITECTURE.md` y `docs/PROTOCOL.md` — cómo se construye.
3. `docs/FUNCTIONAL_REQUIREMENTS.md` — índice de RF/RNF con módulo y estado. Detalle: `docs/source/ERS_v1_0.md` (abrir solo el RF necesario).
4. `docs/DEVELOPMENT_PLAN.md` — hitos y criterios de aceptación.
5. `docs/SECURITY.md` — checklist antes de cerrar un hito.

## Reglas de seguridad (inquebrantables)
1. Nunca escribas, pidas, imprimas ni leas secretos reales (`.env`, `secrets.py`, tokens, claves). Usa variables de entorno; documenta solo en `.env.example` sin valores.
2. La clave `sb_secret_...` de Supabase vive SOLO en `backend/.env`. El frontend usa únicamente la URL y la clave `sb_publishable_...`.
3. Toda tabla de `public` con RLS activado y políticas explícitas. Prohibido `using (true)`. El acceso se decide consultando la tabla de membresía (`usuarios`), no claims del JWT.
4. El backend deduce `family_id` desde la base de datos a partir del usuario o dispositivo autenticado. Nunca confía en el `family_id` que venga en el cuerpo de una petición o mensaje.
5. Valida toda entrada (tipo, rango, tamaño máximo de línea TCP). Rechaza y registra el intento; no registres tokens ni datos del bebé en logs.
6. Códigos y tokens: aleatorios criptográficos, se guardan solo como hash, con caducidad, un solo uso y límite de intentos.
7. El servidor TCP escucha solo en la red local; nunca se expone a Internet.
8. MCP de Supabase en solo lectura y limitado al proyecto. Los cambios de esquema son archivos en `supabase/migrations/` revisados en Pull Request. Solo datos de prueba.
9. No agregues dependencias sin justificarlas; fija versiones.

## Reglas de trabajo
- No inventes requisitos, componentes ni protocolos. No elimines funcionalidades del ERS. Si falta información o hay contradicción, pregunta antes de decidir.
- Firmware: capa de abstracción de hardware, pines en un archivo de configuración. Marca con `TODO-HW` lo que solo se puede probar con la Pico real. No dependas de conexiones no documentadas.
- Idioma: interfaz, documentación y mensajes de commit en español; identificadores de código en inglés; nombres de tablas y columnas en español (`snake_case`), según el Diagrama 1b.
- Cambios pequeños. Una rama por hito: `feat/mN-nombre`. Nunca push directo a `main`; todo entra por Pull Request.
- Antes de cerrar un hito: pruebas pasando + checklist de `docs/SECURITY.md`.
- Ahorra créditos: no releas documentos completos sin necesidad, no regeneres archivos que no cambian, respuestas concisas.

## Varios agentes de IA
- Cursor es el único que escribe código en las ramas de hito.
- Claude y Gemini revisan Pull Requests, documentación y diseño; proponen cambios como comentarios o issues, sin editar la misma rama a la vez.
- Cualquier cambio a decisiones se registra primero en `docs/PROJECT_CONTEXT.md`.

## Stack
- Backend: C# / .NET (última LTS instalada), ASP.NET Core minimal API + servicios en segundo plano (servidor TCP, watchdog, recordatorios, bot Telegram con sondeo).
- Frontend: React + TypeScript + Vite + Tailwind CSS + shadcn/ui + Recharts + `@supabase/supabase-js`.
- Firmware y simuladores: MicroPython (Pico W) y Python 3 en PC.
- Puertos de desarrollo: web 5173, API 5080, TCP de dispositivos 9000 (configurables).
