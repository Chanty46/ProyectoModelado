# FUNCTIONAL_REQUIREMENTS — índice

Detalle completo de cada requisito: `docs/source/ERS_v1_0.md` (abrir solo el necesario). Estado: **OK** = tal cual, **ADAPTADO** = cambia la forma de cumplirlo (ver decisión Dn en `PROJECT_CONTEXT.md`), **DESCARTADO** = fuera de alcance.

| ID | Nombre | Prioridad | Módulos | Estado | Nota |
|---|---|---|---|---|---|
| RF01 | Inicio de Sesión Principal mediante Google OAuth | Alta | Web, Backend, BD | OK | Rol en tabla `usuarios`, no en JWT |
| RF02 | Invitación de Usuario Secundario | Media | Web, Backend, BD | ADAPTADO | Enlace 24 h compartido manualmente (D8) |
| RF03 | Restricción de Privilegios Administrativos | Alta | Web, Backend | ADAPTADO | 403 desde backend + `bitacora_seguridad` |
| RF04 | Alarma por Retiro Imprevisto del Infante | Crítica | FW colchón, Backend, FW central | ADAPTADO | Retiro lo detecta el colchón; alarma la ordena el servidor (D10). Offline: limitación aceptada. Umbrales escalados por celda de 1 kg (D14) |
| RF05 | Pausa de Seguridad - Modo Atención | Alta | Web, Backend, FW central | OK | Estado en servidor; botón táctil de la central |
| RF06 | Registro de Fluctuación y Movimiento Nocturno | Media | FW colchón, Backend, BD | OK | Índice de movimiento en telemetría; umbral de presencia escalado (D14) |
| RF07 | Detección de Desvío Térmico o de Humedad | Alta | FW central, Backend, Telegram | OK | 10 s sostenidos |
| RF08 | Ajuste Dinámico de Umbrales Ambientales | Media | Web, Backend, FW central | ADAPTADO | Por TCP, no MQTT |
| RF09 | Regulación Gradual de Iluminación Nocturna | Media | Web, Backend, FW central | OK | PWM ≥ 1 kHz, rampa 1.5 s |
| RF10 | Detección Acústica de Llanto | Alta | — | DESCARTADO | Acordado con el profesor (D4) |
| RF11 | Transmisión de Voz unidireccional (Intercomunicador) | Media | Web, Backend, FW central | ADAPTADO / RIESGO | Sin WebRTC; spike temprano con plan B (D5) |
| RF12 | Reproducción de Pistas Pregrabadas | Baja | Web, Backend, FW central | ADAPTADO | Melodías como notas por PWM (D12) |
| RF13 | Control del Móvil Colgante de LEDs | Baja | Web, Backend, FW móvil | ADAPTADO | 9 LED normales |
| RF14 | Sincronización del Juguete de Botones vía BLE | Baja | FW juguete, Backend | ADAPTADO | TCP en lugar de BLE (D3) |
| RF15 | Sensado Térmico y Ponderal de la Base de Biberón | Media | FW biberón, Backend, Web | OK |  |
| RF16 | Alerta por Temperatura Inadecuada de Alimento | Alta | Web, Backend | OK | 35.0–38.5 °C |
| RF17 | Cálculo Automático de Volumen Consumido | Media | Web, Backend, BD | OK | Densidad 1.03 |
| RF18 | Recordatorio Dinámico de Tomas | Media | Backend, Telegram, BD | ADAPTADO | Servicio en segundo plano, sin pg_cron |
| RF19 | Despacho Redundante de Alertas por Telegram | Crítica | Backend, Telegram | ADAPTADO | A todos los usuarios vinculados (D6) |
| RF20 | Detección de Pérdida de Conexión - Watchdog en Nube | Crítica | Backend, Telegram | OK | Heartbeat 15 s / watchdog 60 s |
| RF21 | Modo de Contingencia Local - Operación Offline | Crítica | FW central, FW colchón | ADAPTADO | Búfer agregado en ~2 MB (D12) |
| RF22 | Revocación de Usuario Secundario | Alta | Web, Backend, BD | ADAPTADO | RLS por membresía (D9) |
| RF23 | Gestión de Perfiles de Cuidador | Media | Web, BD | OK | Eliminación lógica, máx. 6 |
| RF24 | Registro y Vinculación de la Cuna | Alta | Web, Backend, BD | ADAPTADO | Código de reclamo + token de dispositivo |
| RF25 | Registro de Periféricos Inalámbricos | Media | Web, Backend | ADAPTADO | Sin BLE; mismo mecanismo de reclamo (D3) |
| RF26 | Listado y Renombrado de Cunas | Media | Web, BD | OK |  |
| RF27 | Desvinculación de Cuna | Media | Web, Backend | ADAPTADO | Revoca token de dispositivo |
| RF28 | Registro del Perfil del Bebé | Alta | Web, BD | OK |  |
| RF29 | Alerta por Movimiento Anómalo del Colchón | Media | FW colchón, Backend, Telegram | OK | 30 s sostenidos |
| RF30 | Consulta del Historial de Sueño (Siestas y Noches) | Media | Web, BD | OK | < 3 s |
| RF31 | Consulta del Historial de Alimentación | Media | Web, BD | OK | < 3 s |
| RF32 | Compartir Historial del Infante | Baja | Web, Backend | OK | PDF/CSV < 10 s; registrar en bitácora |
| RF33 | Registro de Uso del Juguete de LEDs | Baja | Backend, BD, FW móvil | OK |  |
| RF34 | Mecánica de Juego del Juguete de Botones | Baja | FW juguete | OK | < 200 ms |
| RF35 | Reinicio del Juego por Fallo | Baja | FW juguete | OK |  |
| RF36 | Aviso de Batería Baja del Juguete de Botones | Baja | FW juguete, Web | ADAPTADO | Aviso por TCP, no BLE |
| RF37 | Inicio de Sesión de Alimentación | Media | Web, Backend, BD | OK |  |
| RF38 | Vinculación del Chat de Telegram | Alta | Web, Backend, Telegram | ADAPTADO | Por usuario (D6) |
| RF39 | Consulta de Estado desde Telegram | Media | Backend, Telegram | OK | < 2 s, solo chats vinculados |
| RNF01 | Latencia Despacho Alertas Emergencia | Crítica | Backend | ADAPTADO | Medir Telegram y notificación local; Push opcional |
| RNF02 | Seguridad Cifrado Aislamiento Datos | Crítica | Todo | ADAPTADO | Sin cifrado de transporte (D1, aprobado). RLS y aislamiento sí |
| RNF03 | Disponibilidad Servicio Backend | Alta | Backend | ADAPTADO | Demostración en laptop (D7) |
| RNF04 | Resiliencia Buffer Local Desconexion | Alta | FW | ADAPTADO | 48 h con datos agregados (D12) |
| RNF05 | Eficiencia Operativa Interfaz Cuidador | Media | Web | OK | Usabilidad |
| RNF06 | Mantenibilidad Escalabilidad Portabilidad | Media | Web, FW | PARCIAL | Compatibilidad web sí; OTA pendiente (D13) |

## Interfaces (resumen)
- IU-01 panel reactivo < 1 s; IU-02 cambio de perfil ≤ 2 toques; IU-03 botón capacitivo retroiluminado (LED RGB en maqueta).
- IH-01 celdas + HX711 (maqueta: 1 celda); IH-02 sensor T/H I2C ±0.5 °C aprox.; IH-03 PWM ≥ 1 kHz; IH-04 base de biberón con MLX90614 (enlace por TCP en vez de BLE).
- IS-01 Supabase (HTTPS/WebSockets); IS-02 Google OAuth; IS-03 Telegram Bot API (REST).
- IC-01 (MQTT/TLS → TCP sin cifrado, D1); IC-02 (WebRTC → audio por TCP, D5); IC-03 (BLE → no aplica, D3).
