# DEVELOPMENT_PLAN — hitos

Un hito = una rama `feat/mN-nombre` = un Pull Request. Cada hito termina con pruebas pasando y el checklist de `docs/SECURITY.md`.
Orden pensado para avanzar sin hardware y atacar pronto lo riesgoso.

| Hito | Contenido | Criterio de aceptación |
|---|---|---|
| **M0 Esqueleto** | Estructura de carpetas, proyectos vacíos (backend, frontend), `.env` locales desde los `.example`, README con pasos | `dotnet build` y `npm run dev` funcionan; ningún secreto en Git |
| **M1 Base de datos** | Migraciones: tablas, RLS por membresía, funciones, índices, datos de prueba | Pruebas SQL: la familia A no ve datos de la B; sin políticas para `anon` |
| **M2 Cuentas y familia** | Login Google (RF01), crear familia, perfiles (RF23), invitaciones por enlace (RF02), revocación (RF22), selector de perfil (IU-02) | Revocado pierde acceso en < 5 s; secundario recibe 403 en acciones admin (RF03) |
| **M3 Protocolo y servidor TCP** | Servidor TCP, `hello`, autenticación, heartbeat, telemetría y eventos a BD; **simuladores** de los 5 dispositivos | Simulador conecta, datos aparecen en BD; token inválido se rechaza; reconexión sin duplicados |
| **M4 Emparejamiento** | RF24–RF28: código de reclamo, token, cunas, desvinculación, bebé | Código reutilizado/caducado/ajeno rechazado; límite de intentos |
| **M5 Cuna en vivo** | Panel en tiempo real (< 1 s), luz (RF09), umbrales (RF07/08), Modo Atención (RF05), retiro (RF04), movimiento (RF06/29), watchdog (RF20) | Escenarios completos con simuladores; Realtime < 1 s |
| **M6 Telegram** | Vinculación por usuario (RF38), alertas (RF19), `/estado` (RF39), recordatorios (RF18) | Chat no vinculado no recibe nada; alertas a todos los vinculados |
| **M7 Alimentación, juguetes e historiales** | RF15–17, RF37, RF13/RF33, RF34–36, RF30–32 (PDF/CSV), canciones RF12 | Cálculo de ml correcto; historiales < 3 s; exportación < 10 s |
| **M8 Spike de audio (RF11)** | Prueba de navegador → backend → simulador/central por PWM. Puede adelantarse tras M3 | Decisión documentada: viable o plan B |
| **M9 Firmware MicroPython** | Los 5 programas con capa de hardware, pines propuestos, búfer offline, reconexión; pruebas en PC | Corren contra el servidor con la misma suite que los simuladores; lista de pruebas pendientes de hardware |
| **M10 Cierre** | Revisión de seguridad completa, pruebas de extremo a extremo, guion de demostración, documentación | Checklist final de `SECURITY.md` en verde |

Pruebas que solo se hacen con hardware: calibración HX711, nivel sonoro ≤ 65 dBA, PWM sin parpadeo, lectura MLX90614 real, memoria y estabilidad Wi-Fi en la Pico, hotspot sin aislamiento de clientes, consumo de pilas.

## Flujo de equipo
- `main` siempre estable. Una rama por hito, PR con descripción corta, revisión de al menos otra persona (y un agente de IA revisor si se desea).
- Cursor escribe; Claude/Gemini revisan. No dos agentes sobre la misma rama a la vez.
- Ahorro de créditos de Cursor: usar el modo Plan una vez por hito; tareas pequeñas; no repetir análisis.
