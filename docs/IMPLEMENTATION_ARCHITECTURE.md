# IMPLEMENTATION_ARCHITECTURE — arquitectura real

## 1. Vista general
```
Navegador (React) ──HTTPS──▶ Supabase (Auth Google, Postgres+RLS, Realtime)   [lectura, realtime]
        │
        └──HTTP──▶ Backend C# (localhost:5080) ──secret key──▶ Supabase       [comandos, admin]
                        │  ├─ Servidor TCP (9000) ◀──Wi-Fi hotspot──▶ 5 Pico W (o simuladores)
                        │  ├─ Watchdog, recordatorios (BackgroundService)
                        │  └─ Bot Telegram (sondeo a api.telegram.org)
```
- La web **lee** con supabase-js (RLS) y recibe cambios por Realtime. Las **acciones** (comandos, admin, emparejamiento, invitaciones, exportaciones) pasan por el backend.
- El backend escribe telemetría/eventos con la clave secreta. Por eso nunca confía en IDs del cliente (ver `AGENTS.md` regla 4).
- Los Pico se conectan a la IP privada de la laptop dentro del hotspot (nunca `localhost`). Hotspot en 2.4 GHz y con comunicación entre clientes (verificar).

## 2. Backend C#
Proyecto `backend/CunaServer/`:
- `Api/`: endpoints (comandos, invitaciones, emparejamiento, vínculo Telegram, exportaciones). Validación de JWT de Supabase + membresía + rol.
- `Tcp/`: listener, sesiones por dispositivo, parser de líneas, autenticación (`hello`), enrutado de comandos.
- `Services/`: estado de cuna (Monitoreo/Atención/Alarma), ventana de gracia de 5 s, watchdog (60 s sin heartbeat), recordatorios de toma, reglas de alerta.
- `Telegram/`: sondeo, comandos `/start`, `/vincular CODIGO`, `/estado`, envío de alertas a todos los chats vinculados de la familia.
- `Data/`: acceso a Supabase (preferir cliente C# comunitario si es estable; si no, REST/PostgREST con `HttpClient`). Justificar la elección en el plan.
- Sesiones TCP activas en memoria: `device_id → conexión`. Estado oficial en BD.

## 3. Reglas distribuidas por dispositivo (D10)
| Dispositivo | Decide | Envía |
|---|---|---|
| Colchón | retiro (RF04: masa ≥ 2.50 kg → < 1.00 kg en ≤ 1 s), índice de movimiento a 5 Hz (RF06/RF29) | `event` retiro/movimiento; `telemetry` cada 60 s |
| Central | umbrales T/H sostenidos 10 s (RF07), lámpara PWM con rampa 1.5 s (RF09), alarma local, botón táctil (RF05), LED RGB de estado, audio | `telemetry`, `event`, `heartbeat` |
| Móvil | animación de 9 LED (patrón/velocidad/duración, RF13) | `event` fin de sesión (RF33) |
| Biberón | masa y temperatura IR (RF15), batería | `telemetry` al cambiar, `event` biberón colocado |
| Juguete | juego de 5 botones (RF34/35), batería (RF36) | `event` fin de juego con aciertos y tiempo |
| Servidor | estado Monitoreo/Atención (RF05, 900 s), gracia 5 s y orden de alarma (RF04), Telegram, watchdog (RF20), recordatorios (RF18), cálculo de ml (RF17) | `command` |

Umbrales de RF04 y de movimiento son configurables (la celda de la maqueta puede ser de menor rango).

## 4. Flujos clave
- **Retiro (RF04):** colchón envía `event removal` → servidor abre 5 s → si no hay pausa/Modo Atención, envía `command alarm_set on` a la central y alerta crítica por Telegram a todos los vinculados.
- **Modo Atención (RF05):** web o botón táctil → servidor guarda estado, temporizador 900 s, ordena LED amarillo y silencia alarma del colchón; al vencer vuelve a Monitoreo.
- **Watchdog (RF20):** sin heartbeat de la central > 60 s → cuna "Desconectada" + Telegram urgente. Al volver, se reenvía el búfer offline (sin duplicados por `seq`).
- **Alimentación (RF37/RF17):** Iniciar guarda M_inicial, temperatura, hora, perfil activo (exige ≥ 20 g). Finalizar guarda M_final y ml = (M_inicial − M_final)/1.03. Alerta visual fuera de 35.0–38.5 °C (RF16).
- **Recordatorios (RF18):** cada minuto el backend compara la última toma con el intervalo (1–8 h).
- **Telegram:** `/vincular CODIGO` (código de un solo uso, 10 min, generado en la web por el usuario). `/estado` solo responde a chats vinculados. Quitar a un miembro borra su vínculo.

## 5. Emparejamiento (RF24, RF25, RF27)
1. Cada dispositivo trae un **código de reclamo** (aleatorio, ≥ 8 caracteres alfanuméricos, impreso/visible por consola). La BD guarda solo su hash y el estado "sin dueño".
2. El Principal pulsa "Agregar dispositivo" en la web: se abre una ventana de 10 minutos y escribe el código.
3. El backend verifica: existe, no está vinculado a otra familia (si lo está, rechaza con el motivo, RF24), no excede intentos (máx. 5 por ventana), no caducó. Vincula dispositivo → cuna → familia y genera el **token de dispositivo** (aleatorio de 32 bytes; solo se guarda su hash).
4. El dispositivo recibe el token en su siguiente conexión (o se carga en `secrets.py` en el prototipo) y lo usa en `hello`.
5. Desvincular (RF27, con doble confirmación) revoca el token y cierra la sesión TCP.
Los cinco dispositivos usan el mismo mecanismo (sustituye el escaneo BLE de RF25).

## 6. Datos (punto de partida: Diagrama 1b, ajustado)
Tablas: `familias`, `usuarios` (id = auth.users.id, `family_id`, `rol` principal|secundario, email), `perfiles_cuidador` (eliminación lógica, máx. 6), `invitaciones` (hash del código, `expira_en`, `usada`), `telegram_vinculos` (por usuario, `chat_id`, código con caducidad), `cunas`, `dispositivos` (cuna_id, tipo, hash_reclamo, hash_token, estado, batería, último heartbeat), `umbrales`, `canciones`, `bebes`, `telemetria` (con **índice de movimiento**, `ts` cada 60 s), `sesiones_sueno`, `sesiones_alimentacion`, `eventos_alertas`, `recordatorios`, `uso_juguetes`, `bitacora` (acciones por perfil), **`bitacora_seguridad`** (accesos denegados, exportaciones, vínculos; nueva).
- RLS en todas, por membresía. Los nombres pueden ajustarse justificando el cambio.
- Telemetría: índice `(cuna_id, ts)`; mantener volumen bajo el límite de 500 MB.

## 7. Frontend
React + TypeScript + Vite + Tailwind + shadcn/ui + Recharts. Pantallas: inicio de sesión (Google), selector de perfil (≤ 2 toques), panel de la cuna (temperatura, humedad, colchón, luz, estado, alertas en < 1 s), controles (luz, Modo Atención, canciones, móvil, intercomunicador), alimentación, historiales (sueño y alimentación, filtros, exportar PDF/CSV), administración (cunas, dispositivos, miembros, invitaciones, umbrales, Telegram, bebé, perfiles).
Diseño: limpio, calmado, accesible, responsive móvil primero. El micrófono del intercomunicador solo funciona en localhost/HTTPS (demo en la laptop).
