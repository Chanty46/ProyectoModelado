# SECURITY — reglas y checklist

Proyecto de curso, red local, no público. Aun así se aplican las medidas baratas que evitan los errores graves.

## Qué es público y qué es privado
| Dato | Público | Dónde vive |
|---|---|---|
| Project URL, project ref, `sb_publishable_` | Sí (seguro con RLS) | `frontend/.env`, `.env.example` |
| `sb_secret_` (salta RLS) | **No** | Solo `backend/.env` |
| Token del bot de Telegram | **No** | `backend/.env` |
| Wi-Fi, `DEVICE_TOKEN` | **No** | `secrets.py` de cada Pico |
| Contraseña de la BD | **No** | Gestor de contraseñas |
Si algo privado se filtra: rotarlo de inmediato (Supabase → nueva secret key; BotFather `/revoke`).

## Checklist por hito (Verificado en M10)
- [x] No hay secretos en el código ni en el historial (`git diff`, buscar `sb_secret`, `AAH`, contraseñas; verificado en `EndToEndAndSecurityChecklistTests`).
- [x] Tablas nuevas con RLS y políticas por membresía (`public.usuarios`); sin `using (true)`; sin acceso `anon` (`DatabaseMigrationSchemaTests`).
- [x] Prueba de aislamiento entre familias (`supabase/tests/isolation_test.sql` y pruebas de repositorio).
- [x] Endpoints: validan JWT, rol y membresía; ignoran `family_id` del cliente; devuelven 403 y registran en `bitacora_seguridad` (`AccountAndFamilyEndpointsTests`, `PairingAndCribEndpointsTests`).
- [x] Entradas validadas (tipo, rango, tamaño); errores sin detalles internos.
- [x] Logs sin tokens ni datos del bebé.
- [x] Códigos/tokens: aleatorios criptográficos (`RandomNumberGenerator`), hasheados (`SHA-256`), con caducidad (`10 min` / `24 h`), un solo uso, intentos limitados (`máx. 5`) y comparación en tiempo constante (`CryptographicOperations.FixedTimeEquals`).
- [x] Servidor TCP solo en la red local; autenticación de `hello` en `≤ 5 s`; límite de línea `4096 B` y bloqueo temporal tras `3` fallos consecutivos (`TcpDeviceServerTests`).
- [x] Dependencias nuevas justificadas y con versión fija.

## Revisión final (M10)
- **Autenticación y Autorización**: Validador JWKS ES256/RS256 (`JwtTokenValidator.cs`), deducción de `familia_id` y `rol` desde `usuarios`, revocación inmediata (`RF22`).
- **Base de datos y RLS**: Migración `20261107200000_m1_esquema_inicial_rls.sql` con RLS forzado en las 14 tablas, funciones `SECURITY DEFINER` con `search_path = public` y revocación total a `anon`.
- **Backend y Servidor TCP**: `DeviceTcpServer.cs` con límite de `4 KB`, `hello` obligatorio en `≤ 5 s`, deduplicación por `seq`, Watchdog de `60 s` (`RF20`) y máquina de estados en el servidor (`D10`).
- **Frontend**: `frontend/.env.example` documenta únicamente `VITE_SUPABASE_URL`, `VITE_SUPABASE_PUBLISHABLE_KEY` y `VITE_API_BASE_URL`.
- **Firmware y Simuladores**: Secretos fuera del repositorio (`firmware/secrets.py` ignorado en `.gitignore` y `.cursorignore`), pines en `firmware/common/pins.py` y búfer circular de `48 h` (`2880` registros).
- **Telegram**: Vínculo por código de un solo uso (`10 min`, hash SHA-256), `/estado` restringido a chats vinculados y baja automática de vínculos al revocar a un miembro (`D6`).
- **Plan de Demostración**: Documentado paso a paso en `docs/DEMO_GUIDE.md`.

## Trabajo con IA
No pegar secretos en chats de IA. Cursor ignora `.env*` y `secrets.py` vía `.cursorignore`. MCP de Supabase solo lectura y solo este proyecto; con datos de prueba.
