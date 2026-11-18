# PROJECT_CONTEXT — Cuna Inteligente

## 1. Qué es
Sistema "Ecosistema Ciberfísico para Supervisión Perimetral, Microclima y Nutrición Infantil": aplicación web, backend, base de datos Supabase, autenticación Google, cinco dispositivos con Raspberry Pi Pico W, bot de Telegram, historiales, alertas y controles. Se implementa **completo** (todo el ERS salvo lo descartado abajo).

Dispositivos: (1) Unidad Central, (2) Colchón, (3) Móvil colgante, (4) Base de biberón, (5) Juguete de botones.

## 2. Jerarquía de fuentes
1. Este documento (decisiones aprobadas) — **gana siempre**.
2. ERS v1.0 (`docs/source/ERS_v1_0.md`) — requisitos.
3. Diagramas v1.1 — arquitectura OBJETIVO/teórica (la cuna "real"); no es lo que se construye.
4. BOM de maqueta — materiales reales (no hay modelos exactos ni pines definidos).

## 3. Decisiones aprobadas (corrigen contradicciones del ERS y los diagramas)
| # | Tema | ERS / diagramas dicen | Decisión vigente |
|---|---|---|---|
| D1 | Transporte | MQTT/TLS 1.3 con mTLS, broker Mosquitto | **Sockets TCP en C#**, mensajes JSON por línea, **sin cifrado**, autenticación por token de dispositivo. Aprobado por el profesor. Afecta IC-01 y RNF02 (la parte de cifrado de transporte). RLS sigue vigente. |
| D2 | Firmware | C/C++ Pico SDK | **MicroPython**, un programa por dispositivo. |
| D3 | Enlaces | UART (colchón, móvil) y BLE GATT (biberón, juguete) hacia la central | **Cada Pico se conecta por Wi-Fi/TCP directamente al servidor**; el USB solo alimenta/programa. Aprobado por el profesor. Afecta RF14, RF25, RF36, IC-03, IH-04. |
| D4 | Llanto | RF10 y micrófono | **Descartado** con el profesor. No implementar. |
| D5 | Audio | WebRTC DTLS/SRTP, I2S | Sin WebRTC. Audio por TCP (navegador → backend → central, PCM de baja calidad) reproducido por PWM + amplificador sencillo. **Prueba técnica temprana (spike)**; si no es viable, plan B: demostrar RF11 con el simulador y documentar la limitación. |
| D6 | Telegram | Un chat por familia, webhook | **Vínculo por usuario** (`telegram_vinculos`); las alertas llegan a **todos** los usuarios vinculados de la familia. Bot con **sondeo (`getUpdates`)**, sin webhook ni URL pública. |
| D7 | Despliegue | VM Linux, Mosquitto, Node worker, Edge Functions, pg_cron, coturn | **Todo en la laptop del responsable (localhost)**: backend C# + web. Supabase en la nube. Recordatorios y watchdog son servicios en segundo plano del backend. RNF03 no se mide con 99.8 % (demostración local aceptada). |
| D8 | Invitaciones (RF02) | Enlace enviado por correo | **Enlace firmado de un solo uso, 24 h**, que el Principal copia y comparte manualmente. |
| D9 | Roles y RF22 | Rol en el JWT | Rol y familia en la tabla `usuarios`; RLS consulta esa tabla. Quitar a alguien corta el acceso al instante (< 5 s). |
| D10 | Lógica de reglas | La central decide todo | Cada dispositivo aplica las reglas de **sus** sensores y manda eventos. El **servidor C#** es la fuente de verdad del estado (Monitoreo/Atención), ventana de gracia, alarmas, Telegram y watchdog. |
| D11 | Web Push | PWA con Push | Opcional y al final (alerta de escritorio). En iOS requiere versiones recientes y PWA instalada; se avisa al profesor. |
| D12 | Memoria | Flash externa 16 MB | Solo los ~2 MB de la Pico: búfer de 48 h con registros **agregados por minuto**; canciones como secuencias de notas, no audio grabado. |
| D13 | OTA (RNF06) | Reversión < 15 s | Fuera del alcance de la maqueta salvo que se apruebe. Compatibilidad web Android 10+/iOS 14+ se mantiene. |

## 4. Limitaciones aceptadas
- Si el servidor TCP se cae, el colchón y la central siguen midiendo y guardando datos offline, pero la **alarma sonora por retiro** (decide el servidor, suena en la central) no se activa. Se documenta como limitación del prototipo.
- Sin reloj propio en la Pico: la hora llega del servidor en `hello_ack`; tras un reinicio sin red se pierde la hora real de los datos locales.
- El tráfico TCP va sin cifrar dentro del hotspot del equipo (aprobado). Mitigación: token por dispositivo, validación estricta, red privada.
- Email y contraseña en Supabase Auth como método principal en la nube (Google OAuth descartado por el equipo) + acceso temporal local `Padre` / `123` para pruebas y presentación preliminar; un usuario sin familia no ve datos gracias a RLS.

## 5. Hardware real de la maqueta (BOM_maqueta.pdf + Cotizacion_maqueta.xlsx)
La cotización confirma los materiales. Pines NO definidos: se proponen en `pins.py` y se confirman con el hardware.
- Central: sensor T/H (DHT22 o AHT10: **pendiente de decidir**, ver abajo), LED blanco 5 mm **manejado por transistor NPN (2N2222/BC547) con resistencia de 100 Ω en la base** y PWM ≥ 1 kHz, amplificador PAM8403 + filtro RC (resistencia 1 kΩ + 100 nF) desde PWM + parlante 8 Ω, sensor táctil TTP223, LED RGB cátodo común con 3 resistencias de 220 Ω.
- Colchón: celda de carga tipo barra de **1 kg** + HX711 (confirmado en cotización). Ver D14.
- Móvil: 9 LED normales + 9 resistencias de 220 Ω (un GPIO por LED).
- Biberón: celda 500 g o 1 kg + HX711, MLX90614 (módulo GY-906, verificar que sea de 3.3 V), 3 pilas AA, divisor 100 kΩ/100 kΩ.
- Juguete: 5 pulsadores 12 mm, 5 LED + resistencias de 220 Ω, buzzer pasivo 5 V (por GPIO con resistencia en serie), 3 pilas AA, divisor de batería.
- Presupuesto de piezas: 20 LED (se usan 14 + RGB aparte), 20 resistencias de 220 Ω (se usan ~18), 5 de 100 kΩ (se usan 4), 2 de 1 kΩ, 1 de 100 Ω (el buzzer necesita otra: usar una de 220 Ω).
- No incluido en la cotización (confirmar que está cubierto): protoboards, jumpers, cables micro USB, power bank/hub USB, estructura de la cuna, muñeco con peso.

### Sensor ambiental (pendiente)
- DHT22: MicroPython trae el módulo `dht` incluido (más fácil de programar). Necesita una resistencia de 4.7–10 kΩ en la línea de datos si es de 4 pines (la cotización no trae de 10 kΩ; los módulos de 3 pines ya la incluyen). No es I2C (IH-02 pide I2C).
- AHT10/AHT20: I2C (cumple IH-02), sin piezas extra, requiere un driver pequeño propio.
- Recomendación: AHT10/AHT20 si se consigue; DHT22 en módulo de 3 pines es aceptable y se documenta la diferencia con IH-02. La capa de hardware debe aislar el sensor para poder cambiarlo.

## 5b. Decisión D14 — Escala de masa del colchón
La celda de 1 kg no permite los umbrales del ERS (≥ 2.50 kg → < 1.00 kg). Se aplica una **escala configurable** (`mass_scale`, por defecto 0.2) llegando por `hello_ack`:
- RF04 de la maqueta: retiro cuando la masa pasa de ≥ 0.50 kg a < 0.20 kg en ≤ 1 s.
- RF06: presencia del bebé con masa > 0.20 kg.
- Muñeco de prueba: entre 0.5 y 0.8 kg. **Nunca superar 1 kg** (daña la celda).
- Se presenta al profesor como equivalencia proporcional; los valores reales del ERS quedan como valores por defecto del diseño objetivo.

## 5c. Resultado del Spike de Audio M8 (RF11 / Decisión D5)
Se ejecutó la prueba técnica temprana de transmisión de voz unidireccional (`Navegador → Backend C# → Unidad Central por TCP`):
- **Encuadre sobre el protocolo TCP vigente**: se usa el comando `audio_chunk` con muestras PCM de 8 bits sin signo (`u8`, `4000..8000 Hz`) en bloques de hasta `1280` bytes (`≤ 160 ms` a `8 kHz`) codificados en Base64 (`~1.85 KB` por línea JSON, cumpliendo el límite estricto de `≤ 4 KB` sin requerir un segundo puerto ni romper el parser de líneas).
- **Seguridad auditiva (≤ 65 dBA, RF11)**: el backend aplica un limitador digital de amplitud máxima (`MaxSafeGain = 0.65` alrededor del punto medio `128`) antes de despachar cada bloque sin espera bloqueante de `ack`.
- **Latencia medida**: `< 15 ms` de extremo a extremo (`API HTTP → Servidor TCP → Simulador Central`), cumpliendo holgadamente el requisito de `≤ 250 ms` de RF11.
- **Resolución de viabilidad**:
  1. **En simulador Python 3 (`CentralAudioPlayer`)**: 100 % operativo y verificado con pruebas automatizadas (**Plan B garantizado para la demostración**).
  2. **En hardware Pico W (MicroPython)**: el transporte TCP y la decodificación Base64 a `bytearray` son viables; la reproducción continua a `8 kHz` en MicroPython sin DMA se implementa con `machine.PWM` + `machine.Timer` y queda marcada con `TODO-HW` para validación acústica con el amplificador `PAM8403` y el filtro RC (`1 kΩ + 100 nF`).

## 6. Pendientes
- Decidir sensor ambiental (DHT22 vs AHT10, ver §5). La celda del colchón es de 1 kg (D14).
- Alcance de OTA (D13) y de Web Push (D11).
- Pinout de la maqueta.
- Protección de la rama `main` (depende del plan de GitHub del equipo).

## 7. Infraestructura ya creada (sin secretos)
- Repositorio GitHub (privado), 5 colaboradores.
- Supabase proyecto `rjklzbarjuaxdhryklzw` (plan gratuito: 500 MB BD, se pausa tras 1 semana sin actividad → entrar al panel semanalmente y el día antes de la demo).
- Autenticación en Supabase Auth mediante correo y contraseña (Google OAuth descartado).
- Bot de Telegram `@CunaIntBot` (configurado en `backend/.env` local del responsable).
