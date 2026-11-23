# ESTADO_SESION — traspaso para continuar en otra sesión (nube u otro equipo)

Última actualización: 2026-11-23. Léelo después de `AGENTS.md` y antes de tocar código.
Si algo aquí contradice a `docs/PROJECT_CONTEXT.md`, manda `PROJECT_CONTEXT.md`.

## 1. Cómo trabajar con el usuario
- Responder **en español, claro y sin jerga**: el usuario es estudiante de 1.er año. Explicar el *porqué* de cada problema en pocas líneas y dar pasos numerados cuando él deba hacer algo (Supabase, Thonny, GitHub).
- **Verificar antes de afirmar**: correr pruebas/build y revisar datos reales; decir lo que no se pudo comprobar.
- Cambios en una rama `feat/...` y **PR a `main`** (nunca push directo a `main`). Commit, push y PR **solo cuando el usuario lo pida**.
- Antes de cada commit: revisar que el diff no tenga claves ni tokens, ni `.env`, `config.py`, `secrets.py` o `devices.local.json`.
- Cambios de esquema: archivo SQL en `supabase/migrations/` que el usuario pega y ejecuta en el **SQL Editor** del proyecto `rjklzbarjuaxdhryklzw` (hay que pedirle que revise que la URL tenga ese ref: una vez lo corrió en otro proyecto). Los SQL deben ser idempotentes.
- Nunca pedir que pegue secretos en el chat; si hace falta una clave, que la ponga él en el archivo local.

## 2. Estado de GitHub (`Chanty46/ProyectoModelado`)
- **`main` (`18e2128`)** incluye:
  - PRs `#12` a `#16` (simulacro de demo, mensajes claros del bot de Telegram, reorganización modular y pruebas UI).
  - Commit `18e2128` (*Arreglo de conexion y mejoras de interfaz*): lanzador en 1 comando `run.py`, configurador `scripts/setup_local.py` (`--env`, `--sim-tokens`, reemplaza los `.ps1`), autenticación en la nube por **correo y contraseña** (`EmailPasswordAuthForm.tsx`, Google OAuth descartado), acceso temporal local `Padre` / `123` (`LocalDemoAuthService.cs`, `LocalDemoEndpoints.cs`, `DemoSimulatorCard.tsx`), manuales PDF en `docs/` (`Manual_Usuario_Cuna_Inteligente.pdf` y `Manual_Usuario_Bot_Telegram.pdf`) y bot `@CunaIntBot`.
- **Rama `feat/movil-comandos-realtime` (integrada en `main` vía PR)**: control del móvil colgante con la Pico real y versión definitiva de Supabase:
  - `supabase/migrations/20261119120000_comandos_movil_realtime_rls.sql` — Realtime para `comandos`/`cunas`, RLS de `comandos`. **Ya ejecutado en Supabase**.
  - `supabase/migrations/20261123120000_esquema_definitivo_movil_y_seguridad.sql` — **Migración definitiva idempotente para pegar y ejecutar en el SQL Editor de `rjklzbarjuaxdhryklzw`**: versiona en Git `public.comandos`, columnas `cunas.estado`, `cunas.ultimo_heartbeat`, `cunas.token_dispositivo` y `cunas.token_expira_en` (`NULL` = permanente sin caducidad), las RPC definitivas `dispositivo_comandos_pendientes` y `dispositivo_confirmar_comando` (ahora con registro automático en `uso_juguetes` para RF33), la nueva RPC `rotar_token_dispositivo_cuna` (solo Principal) y la protección por columnas para que `authenticated` nunca pueda leer `cunas.token_dispositivo`.
  - Vigencias ampliadas y configurables (D16): ventana de emparejamiento Pico W **60 min** (`PAIRING_WINDOW_MINUTES`), código de Telegram **60 min** (`TELEGRAM_LINK_CODE_MINUTES`), invitación familiar **72 h** (`INVITATION_EXPIRATION_HOURS`) y sesión local de pruebas **24 h** (`LOCAL_DEMO_TOKEN_HOURS`).
  - Web: `frontend/src/components/feeding/MobileControlCard.tsx`, `hooks/useMobileCommand.ts`, `hooks/useCribHeartbeat.ts`, `lib/mobileCommands.ts`. Inserta `movil_on` / `movil_off` en `public.comandos`, muestra "Enviando…", "Confirmado", el error o "La cuna no respondió" (10 s) escuchando la fila por Realtime, y En línea/Desconectada según `cunas.ultimo_heartbeat` (> 60 s = desconectada). Las canciones quedaron en `feeding/SongsCard.tsx`.
  - Firmware: `firmware/movil_supabase/main.py` (versión que usa la Pico real, ahora con `timeout=5` defensivo y `gc.collect()`) + `config.example.py`. La animación corre en el **núcleo 1** (`_thread`). Pruebas en PC: `firmware/test_movil_supabase.py`.
  - Decisiones registradas como **D15** y **D16** en `docs/PROJECT_CONTEXT.md`.
- **Suite de pruebas verificada (147 pruebas pasando)**:
  - Backend `.NET 8`: **60 / 60** (`dotnet test backend/CunaServer.sln`; se corrigió la sobrecarga ambigua `Split(new[] { '\r', '\n' }, ...)` en `EndToEndAndSecurityChecklistTests.cs:72` para .NET SDK 8.0.100+).
  - Simuladores Python: **11 / 11** (`test_simulators.py`).
  - Firmware MicroPython: **13 / 13** (`7` en `test_firmware.py` + `6` en `test_movil_supabase.py`).
  - Frontend React + TS: **63 / 63** (`12` en `formatters.test.ts` + `51` en 7 archivos Vitest), `tsc --noEmit` (0 errores) y `vite build` exitoso.
- Pendiente físico: confirmar en la Pico real que `lenta`/`media`/`rapida` se distinguen con la versión actualizada de `firmware/movil_supabase/main.py`.

## 3. Arquitectura real hoy (resumen)
- Simuladores y la mayoría de nodos: protocolo TCP con el backend C# (`docs/PROTOCOL.md`).
- **Móvil colgante real (D15):** la Pico W consulta Supabase cada 1 s con las RPC `dispositivo_comandos_pendientes(p_cuna, p_token, p_tipos)` y `dispositivo_confirmar_comando(p_cuna, p_token, p_id, p_estado)`, y actualiza `cunas.ultimo_heartbeat` / `cunas.estado`. **No cambiar esas funciones ni los nombres de columnas, tipos, patrones o velocidades.**
  - Patrones: `secuencia`, `parpadeo`, `columnas`, `filas`, `espiral`. Velocidades: `lenta` 600 ms, `media` 300 ms, `rapida` 120 ms. Duración: entero 1–30 min. LEDs en matriz 3×3 (pines en el `main.py`).
- **Esquema en Git:** la tabla `comandos`, las columnas `cunas.estado`/`ultimo_heartbeat`/`token_dispositivo`/`token_expira_en` y las RPC ya están versionadas de forma definitiva e idempotente en `supabase/migrations/20261123120000_esquema_definitivo_movil_y_seguridad.sql`.

## 4. Estado actual de Supabase (`rjklzbarjuaxdhryklzw`)
- **Conectividad verificada (2026-11-21):** el proyecto `https://rjklzbarjuaxdhryklzw.supabase.co` está **activo y en línea** (no pausado: `GET /auth/v1/.well-known/jwks.json` responde `HTTP 200`, `Auth` y `PostgREST /rest/v1/` responden `HTTP 401` exigiendo `apikey` como corresponde).
- **Datos actuales de prueba:**
  - Una familia ("Familia Prueba") con una cuna ("Cuna Principal", id `6d154f20-09ad-4be4-90d7-30d1bd4d873e`) y un usuario principal. Hay otras 2 cuentas creadas que aún no pertenecen a ninguna familia (necesitan invitación).
  - 5 dispositivos simulados `demo-*-01` emparejados a esa cuna (`scripts/provision_demo_devices.py`).

## 5. Pendientes y riesgos conocidos
1. Ejecutar [`supabase/migrations/20261123120000_esquema_definitivo_movil_y_seguridad.sql`](../supabase/migrations/20261123120000_esquema_definitivo_movil_y_seguridad.sql) en el SQL Editor del proyecto `rjklzbarjuaxdhryklzw` para dejar la versión definitiva en Supabase (protección de `token_dispositivo`, RPC versionadas e historial RF33 automático).
2. Probar el firmware actualizado del móvil (`firmware/movil_supabase/main.py`) en la Pico real.
3. Antes de la demo: entrar al panel de Supabase para evitar que el plan gratuito se pause por inactividad.
4. Proceso del curso (no software): Product Backlog con puntos, minutas, retrospectiva (ver PDF de la especificación del curso).

## 6. Entorno
- **Local (Windows del usuario):** repo en `C:\dev\ProyectoModelado-repo` (fuera de `Documentos` porque Defender "Acceso controlado a carpetas" bloquea npm/dotnet ahí). Claves en `backend/.env`, `frontend/.env`, `simulators/devices.local.json`, `firmware/movil_supabase/config.py` (todo ignorado). En esa PC `python3` apunta al alias de la Microsoft Store: usar `python`.
- **En la nube / Linux de desarrollo:** no hay `.env`, ni Pico, ni `localhost` del usuario. Se puede leer y cambiar código, correr pruebas (`python3 scripts/verify_environment.py`, `dotnet test backend/CunaServer.sln`, `cd frontend && npm run lint && npm test && npm run build`, `python3 -m unittest discover -s firmware -p "test_*.py"`, simuladores) y abrir PRs. Para probar contra Supabase hay que pedirle al usuario que configure las variables en el entorno, nunca en el repo.
- Comandos útiles: `python3 run.py` (`python run.py` en Windows), `README.md` y `docs/DEMO_GUIDE.md` (sección 1.1: simulacro con cuenta real).
