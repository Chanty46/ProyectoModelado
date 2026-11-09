# PROTOCOL — mensajes TCP dispositivo ↔ servidor (v1)

Transporte: TCP, una línea JSON por mensaje terminada en `\n`, UTF-8, máximo 4 KB por línea (el audio puede requerir otro encuadre: se define en el spike de RF11).
Sin cifrado (decisión D1). La seguridad se apoya en token por dispositivo y validación estricta.

## Sobre
```json
{"v":1,"t":"heartbeat","seq":42,"ts":1760000000,"d":{}}
```
`t` = tipo, `seq` = contador por dispositivo (para evitar duplicados y reordenar), `ts` = segundos epoch según la hora recibida del servidor (puede ser `null` si aún no se sincronizó), `d` = datos.

## Tipos
| `t` | Sentido | Datos (`d`) |
|---|---|---|
| `hello` | D→S | `device_id`, `device_type` (central/colchon/movil/biberon/juguete), `fw`, `token` |
| `hello_ack` | S→D | `server_time`, `config` (umbrales, parámetros), `session_ok` |
| `heartbeat` | D→S | `battery_pct` (si aplica), `uptime_s` — cada 15 s |
| `telemetry` | D→S | por dispositivo: central `temp_c`, `hum_pct`; colchón `mass_kg`, `movement_idx`; biberón `mass_g`, `temp_c`, `battery_pct`; juguete `battery_pct`. Agregados por minuto |
| `event` | D→S | `kind`: `removal`, `env_out_of_range`, `movement_high`, `button_attention`, `bottle_placed`, `game_end` (`hits`, `secs`), `battery_low`, `mobile_session_end` (`secs`, `pattern`, `speed`) |
| `command` | S→D | `cmd`: `light_set`(`on`,`level`), `mode_set`, `alarm_set`, `thresholds_set`, `song_play`, `mobile_play`, `status_led`, `audio_*` (spike) ; `id` para ack |
| `ack` | ambos | `id` del mensaje confirmado, `ok` |
| `error` | ambos | `code`, `msg` |

## Reglas
- Tras conectar, el dispositivo envía `hello` en ≤ 5 s o el servidor cierra. 3 `hello` inválidos → cierre y bloqueo temporal por IP.
- El servidor ignora cualquier `family_id`/`cuna_id` enviado por el dispositivo; los obtiene de la BD según `device_id` autenticado.
- Validar tipo y rango de cada campo; descartar y registrar mensajes inválidos.
- Reconexión del dispositivo con espera 1, 2, 4… hasta 30 s. Búfer offline: registros agregados con `seq`; al reconectar se envían en orden; el servidor descarta `seq` ya recibidos.
- Comandos críticos (alarma, modo) requieren `ack`; se reintenta tras 2 s, hasta 3 veces.
- Heartbeat cada 15 s; watchdog del servidor a los 60 s (RF20).
- Versión `v` incompatible → `error` y cierre.
