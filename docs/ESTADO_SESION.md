# ESTADO_SESION — traspaso para continuar en otra sesión (nube u otro equipo)

Última actualización: 2026-11-21. Léelo después de `AGENTS.md` y antes de tocar código.
Si algo aquí contradice a `docs/PROJECT_CONTEXT.md`, manda `PROJECT_CONTEXT.md`.

## 1. Cómo trabajar con el usuario
- Responder **en español, claro y sin jerga**: el usuario es estudiante de 1.er año. Explicar el *porqué* de cada problema en pocas líneas y dar pasos numerados cuando él deba hacer algo (Supabase, Thonny, GitHub).
- **Verificar antes de afirmar**: correr pruebas/build y revisar datos reales; decir lo que no se pudo comprobar.
- Cambios en una rama `feat/...` y **PR a `main`** (nunca push directo a `main`). Commit, push y PR **solo cuando el usuario lo pida**.
- Antes de cada commit: revisar que el diff no tenga claves ni tokens, ni `.env`, `config.py`, `secrets.py` o `devices.local.json`.
- Cambios de esquema: archivo SQL en `supabase/migrations/` que el usuario pega y ejecuta en el **SQL Editor** del proyecto `rjklzbarjuaxdhryklzw` (hay que pedirle que revise que la URL tenga ese ref: una vez lo corrió en otro proyecto). Los SQL deben ser idempotentes.
- Nunca pedir que pegue secretos en el chat; si hace falta una clave, que la ponga él en el archivo local.

## 2. Estado de GitHub (`Chanty46/ProyectoModelado`)
- `main` incluye los PR #12 a #16 (simulacro de demo, mensajes claros del bot de Telegram, refactors y pruebas UI del equipo).
- Rama **`feat/movil-comandos-realtime`** (todavía sin PR): control del móvil colgante con la Pico real. Contenido:
  - `supabase/migrations/20261119120000_comandos_movil_realtime_rls.sql` — Realtime para `comandos`/`cunas`, RLS de `comandos` (miembros leen e insertan comandos válidos de sus cunas; no pueden actualizarlos ni borrarlos). **Ya ejecutado en Supabase** y funcionando.
  - Web: `frontend/src/components/feeding/MobileControlCard.tsx`, `hooks/useMobileCommand.ts`, `hooks/useCribHeartbeat.ts`, `lib/mobileCommands.ts`. Inserta `movil_on` / `movil_off` en `public.comandos`, muestra "Enviando…", "Confirmado", el error o "La cuna no respondió" (10 s) escuchando la fila por Realtime, y En línea/Desconectada según `cunas.ultimo_heartbeat` (> 60 s = desconectada). Las canciones quedaron en `feeding/SongsCard.tsx`.
  - Firmware: `firmware/movil_supabase/main.py` (versión que usa la Pico real) + `config.example.py`. La animación corre en el **núcleo 1** (`_thread`) porque cada HTTPS a Supabase bloquea 0.5–2 s; antes las tres velocidades se veían iguales (~1 cuadro cada 1.5 s). Pruebas en PC: `firmware/test_movil_supabase.py`.
  - Decisión registrada como **D15** en `docs/PROJECT_CONTEXT.md`.
- Pendiente de esa rama: confirmar en la Pico real que lenta/media/rápida se distinguen; luego abrir el PR.

## 3. Arquitectura real hoy (resumen)
- Simuladores y la mayoría de nodos: protocolo TCP con el backend C# (`docs/PROTOCOL.md`).
- **Móvil colgante real (D15):** la Pico W consulta Supabase cada 1 s con las RPC `dispositivo_comandos_pendientes(p_cuna, p_token, p_tipos)` y `dispositivo_confirmar_comando(p_cuna, p_token, p_id, p_estado)`, y actualiza `cunas.ultimo_heartbeat` / `cunas.estado`. **No cambiar esas funciones ni los nombres de columnas, tipos, patrones o velocidades.**
  - Patrones: `secuencia`, `parpadeo`, `columnas`, `filas`, `espiral`. Velocidades: `lenta` 600 ms, `media` 300 ms, `rapida` 120 ms. Duración: entero 1–30 min. LEDs en matriz 3×3 (pines en el `main.py`).
- Creado **a mano en Supabase y NO está en `supabase/migrations/`**: la tabla `comandos` (`id`, `cuna_id`, `tipo`, `payload jsonb`, `estado` default `pendiente`, `creado_en`, `confirmado_en`), las columnas `cunas.estado`, `cunas.ultimo_heartbeat`, `cunas.token_dispositivo` y las dos RPC. Para versionarlas habría que pedir al usuario su definición (`SELECT pg_get_functiondef('public.dispositivo_comandos_pendientes'::regproc);`).

## 4. Datos actuales en Supabase (prueba)
- Una familia ("Familia Prueba") con una cuna ("Cuna Principal", id `6d154f20-09ad-4be4-90d7-30d1bd4d873e`) y un usuario principal. Hay otras 2 cuentas de Google que iniciaron sesión pero no pertenecen a ninguna familia (necesitan invitación).
- 5 dispositivos simulados `demo-*-01` emparejados a esa cuna (`scripts/provision_demo_devices.py`).

## 5. Pendientes y riesgos conocidos
1. Probar el firmware nuevo del móvil en la Pico real y abrir el PR de `feat/movil-comandos-realtime`.
2. `cunas.token_dispositivo` lo puede leer cualquier miembro de la familia, y Realtime lo envía en cada cambio de `cunas` → proponer permisos por columna (preguntar antes).
3. RF33 (historial de uso del móvil): la ruta vieja del backend lo registraba; ahora el uso queda en `comandos`, pero el historial de la web no lo muestra.
4. La prueba `SecurityChecklist_RepositoryHasNoSecretsAndProtectsEnvAndSecretsPy` falla si existen `backend/.env` o `frontend/.env` en la carpeta (aunque Git los ignore). Se podría cambiar para que verifique que están ignorados.
5. Antes de la demo: Google OAuth en modo *Testing* solo deja entrar a los *test users*; Supabase gratis se pausa tras días sin uso.
6. Proceso del curso (no software): Product Backlog con puntos, minutas, retrospectiva (ver PDF de la especificación del curso).

## 6. Entorno
- **Local (Windows del usuario):** repo en `C:\dev\ProyectoModelado-repo` (fuera de `Documentos` porque Defender "Acceso controlado a carpetas" bloquea npm/dotnet ahí). Claves en `backend/.env`, `frontend/.env`, `simulators/devices.local.json`, `firmware/movil_supabase/config.py` (todo ignorado). En esa PC `python3` apunta al alias de la Microsoft Store: usar `python`.
- **En la nube:** no hay `.env`, ni Pico, ni `localhost` del usuario. Se puede leer y cambiar código, correr pruebas (`dotnet test backend/CunaServer.sln`, `cd frontend && npm ci && npm run lint && npm test && npm run build`, `python -m unittest discover -s firmware -p "test_*.py"`, simuladores) y abrir PRs. Para probar contra Supabase hay que pedirle al usuario que configure las variables en el entorno de la nube, nunca en el repo.
- Comandos útiles: `README.md` y `docs/DEMO_GUIDE.md` (sección 1.1: simulacro con cuenta real).
