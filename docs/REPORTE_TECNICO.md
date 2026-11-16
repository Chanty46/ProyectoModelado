# Reporte Técnico Final — Cuna Inteligente (Hitos M0–M11)

**Proyecto:** Ecosistema Ciberfísico para Supervisión Perimetral, Microclima y Nutrición Infantil  
**Curso:** Modelado y Programación / Ingeniería en Computadores  
**Entorno de Demostración:** Red local de desarrollo (localhost) — Puertos: Web `:5173`, API `:5080`, Servidor TCP `:9000`  
**Repositorio:** `Chanty46/ProyectoModelado`  
**Fecha de Entrega:** Noviembre 2026  

---

## 1. Resumen Ejecutivo

**Cuna Inteligente** es un sistema de supervisión perimetral, microclima y nutrición infantil desarrollado siguiendo el [ERS v1.0](source/ERS_v1_0.md) y los acuerdos registrados en [PROJECT_CONTEXT.md](PROJECT_CONTEXT.md).

El proyecto conecta cinco nodos embebidos en Raspberry Pi Pico W, un servidor central en .NET 8, base de datos en Supabase con aislamiento por familia y una aplicación web en React:

```
+-----------------------------------------------------------------------------------------+
|                                    INTERFACES DE USUARIO                                |
|  - Web SPA React 18 + TS + Vite + Tailwind (:5173)                                      |
|  - Bot de Telegram (@CunasInteligentes_Modelado_Bot) para alertas y /estado              |
+--------------------------------------------+--------------------------------------------+
                                             | HTTP / REST (JWT)
                                             v
+-----------------------------------------------------------------------------------------+
|                               BACKEND C# (.NET 8 LTS) (:5080)                           |
|  - ASP.NET Core Minimal API (cuentas, emparejamiento, cuna en vivo, historiales, audio) |
|  - Servidor TCP de Dispositivos (:9000, framing \n, línea <= 4 KB, hello <= 5 s)        |
|  - Watchdog de Cuna en Nube (60 s)                                                      |
|  - Servicio en Segundo Plano de Recordatorios Dinámicos y Sondeo de Telegram            |
+----------------------+------------------------------------+-----------------------------+
                       | PostgREST / Service Role           | TCP / JSON-Lines (:9000)
                       v                                    v
+------------------------------------+  +-------------------------------------------------+
|   SUPABASE (PostgreSQL 15)         |  |   5 DISPOSITIVOS EMBEBIDOS (RP2040 / SIMULADORES) |
|  - 18 tablas con RLS forzado       |  |  1. Unidad Central (DHT22/AHT10, Luz PWM, Audio)|
|  - Membresía por public.usuarios   |  |  2. Colchón Sensor (Celda 1 kg, HX711, 5 Hz)    |
|  - Cero privilegios para anon      |  |  3. Móvil Colgante (9 LEDs, patrones visuales)  |
|  - Bitácora y auditoría de eventos |  |  4. Base de Biberón (Masa, temp IR MLX90614)    |
+------------------------------------+  |  5. Juguete Cognitivo (5 pulsadores, buzzer)    |
                                        +-------------------------------------------------+
```

---

## 2. Arquitectura de Componentes e Implementación

### 2.1 Backend C# (.NET 8 LTS)
- **CunaServer Minimal API (`backend/CunaServer/Api/`)**:
  - Endpoints REST organizados modularmente (`AccountAndFamilyEndpoints`, `PairingAndCribEndpoints`, `CribLiveControlEndpoints`, `FeedingHistoryAndToysEndpoints`, `HealthEndpoints`, `IntercomEndpoints`).
  - Middleware global de captura de excepciones que neutraliza fugas de información interna y retorna respuestas JSON RFC 7807 homogéneas (`HTTP 500` con `{ error: "..." }`).
  - Deducción segura de `family_id` desde la base de datos a partir del JWT autenticado, rechazando cualquier `family_id` enviado en el cuerpo de peticiones (Regla de Seguridad 4).
- **Servidor TCP de Dispositivos (`backend/CunaServer/Tcp/DeviceTcpServer.cs`)**:
  - Escucha en red local `:9000` con lectura delimitada por saltos de línea (`\n`) y tope estricto de `4096 B`.
  - Fase de autenticación de dispositivo mediante sobre `hello` con tiempo límite de 5 segundos (`HelloTimeout`).
  - Lista negra temporal por IP tras 3 intentos consecutivos fallidos de autenticación.
  - Sincronización horaria (`server_time`), entrega de configuración adaptativa (`mass_scale = 0.2`, umbrales) y número de secuencia inicial (`last_seq`).
  - Concurrencia segura: resolución atómica de sesiones activas (`ConcurrentDictionary`) y cancelación inmediata de `_pendingAcks` en caso de desconexión abrupta.
- **Intercomunicador de Voz Push-to-Talk (Spike M8 — RF11 / D5)**:
  - Streaming unidireccional por tramas PCM 8-bit a 8 kHz codificadas en Base64.
  - Limitador acústico digital en backend (`MaxSafeGain = 0.65`, punto medio 128) que garantiza una emisión $\le 65$ dBA en el parlante de la cuna.

### 2.2 Base de Datos y Seguridad (Supabase / PostgreSQL)
- **Migración (`supabase/migrations/20261107200000_m1_esquema_inicial_rls.sql`)**:
  - 18 tablas cubriendo familias, usuarios, perfiles de cuidador, cunas, dispositivos, telemetría, eventos/alertas, sesiones de sueño, alimentación, juguetes y bitácoras de auditoría.
  - `ALTER TABLE ... ENABLE ROW LEVEL SECURITY;` y `ALTER TABLE ... FORCE ROW LEVEL SECURITY;` aplicados a todas las tablas de `public`.
  - Prohibición estricta de políticas permisivas `using (true)` o roles anónimos (`anon`).
  - Funciones auxiliares `SECURITY DEFINER` con `search_path = ''` para evitar secuestro de esquemas.
  - Índices compuestos de alto rendimiento: `(cuna_id, ts DESC)`, `(cuna_id, seq DESC)`, `(bebe_id, inicio DESC)`.

### 2.3 Frontend Web (React 18 + TypeScript + Vite)
- **Single Page Application (`frontend/`)**:
  - Panel en vivo con actualización inferior a 1 segundo para estado de cuna, masa, índice de movimiento, temperatura y humedad.
  - Selector de perfiles de cuidador con persistencia en `localStorage` y cambio en $\le 2$ toques (IU-02).
  - Componente de captura de voz Push-to-Talk (`IntercomPushToTalk.tsx`) con soporte accesible para eventos de ratón, táctiles y teclado (Espacio/Enter).
  - Exportación de historiales en PDF nativo (vía generador PDF 1.4) y CSV con desinfección contra inyección de fórmulas (`'`, `=`, `+`, `-`, `@`).
  - Barrera de errores (`ErrorBoundary.tsx`) para recuperación limpia de fallos de renderizado sin congelamiento de pantalla.

### 2.4 Firmware MicroPython (Raspberry Pi Pico W / RP2040)
- **Capa de Abstracción de Hardware y Red (`firmware/common/`)**:
  - Búfer circular persistente en memoria flash con capacidad para 48 horas de telemetría agregada minuto a minuto (2880 registros).
  - Vaciado automático ordenado por `seq` con confirmaciones `ack` al reanudar la conexión TCP.
  - Watchdog de hardware configurado en 8000 ms (`WDT_TIMEOUT_MS = 8000`).
  - Recolección de basura periódica (`gc.collect()`) en bucle de sensores y eventos de red para evitar fragmentación de heap en los 264 KB de RAM del RP2040.
  - Archivo de pines centralizado ([`pins.py`](file:///home/frigid/Documents/Git/ProyectoModelado/firmware/common/pins.py)) con marcas `TODO-HW` para validación con el hardware físico.

### 2.5 Simuladores de Dispositivos (Python 3)
- **Suite de Emulación (`simulators/`)**:
  - 5 simuladores asíncronos (`central`, `colchon`, `movil`, `biberon`, `juguete`) ejecutables como módulo (`python3 -m simulators.run_all`).
  - Soporte para ejecución en bucle continuo con emisión de heartbeats (15 s) y respuesta a comandos del servidor.
  - Manejo seguro de señales (`SIGINT`) con cierre ordenado de sockets TCP.

---

## 3. Matriz de Cumplimiento de Requisitos

### 3.1 Requisitos Funcionales (RF01 – RF39)

| ID | Requisito | Estado | Módulos | Evidencia de Verificación |
|:---|:---|:---:|:---|:---|
| **RF01** | Inicio de sesión mediante Google OAuth | **OK** | Web, Backend, BD | Verificado en `AccountAndFamilyEndpointsTests` y flujo Supabase Auth. |
| **RF02** | Invitación de usuario secundario | **ADAPTADO** | Web, Backend, BD | Enlace de 24 h generado con token opaco de 32 bytes y hash SHA-256 (Decisión D8). |
| **RF03** | Restricción de privilegios administrativos | **ADAPTADO** | Web, Backend | HTTP 403 Forbidden para cuidadores secundarios con registro en `bitacora_seguridad`. |
| **RF04** | Alarma por retiro imprevisto del infante | **ADAPTADO** | FW colchón, Backend, FW central | Caída de masa $\ge 0.50$ kg a $< 0.20$ kg en $\le 1$ s con ventana de gracia de 5 s (D10, D14). |
| **RF05** | Modo Atención (pausa de seguridad) | **OK** | Web, Backend, FW central | Temporizador de 15 min; activable desde web o botón táctil TTP223; suprime falsas alarmas. |
| **RF06** | Registro de fluctuación y movimiento | **OK** | FW colchón, Backend, BD | Muestreo a 5 Hz filtrado contra ruido de masa; persistido en telemetría. |
| **RF07** | Detección de desvío térmico o humedad | **OK** | FW central, Backend, Telegram | Alerta tras 10 s sostenidos fuera del rango seguro configurado. |
| **RF08** | Ajuste dinámico de umbrales ambientales | **ADAPTADO** | Web, Backend, FW central | Actualización por TCP en `hello_ack` o comando `set_config` (Decisión D1). |
| **RF09** | Regulación gradual de iluminación nocturna | **OK** | Web, Backend, FW central | Rampa PWM suave de 1.5 s a $\ge 1$ kHz (0 a 100 %). |
| **RF10** | Detección acústica de llanto | **DESCARTADO** | — | Descartado de común acuerdo con el docente (Decisión D4). |
| **RF11** | Intercomunicador de voz unidireccional | **ADAPTADO** | Web, Backend, FW central | Spike técnico M8 validado: PCM 8 kHz por TCP con limitador $\le 65$ dBA (D5). |
| **RF12** | Reproducción de pistas pregrabadas | **ADAPTADO** | Web, Backend, FW central | Secuencias de notas musicales por PWM con buzzer/altavoz (Decisión D12). |
| **RF13** | Control del móvil colgante de LEDs | **ADAPTADO** | Web, Backend, FW móvil | 9 LEDs manejados por GPIO individual con patrones (ola, cascada, destello). |
| **RF14** | Sincronización del juguete de botones | **ADAPTADO** | FW juguete, Backend | Conexión directa Wi-Fi/TCP al servidor sin intermediación BLE (Decisión D3). |
| **RF15** | Sensado de la base de biberón | **OK** | FW biberón, Backend, Web | Lectura combinada de celda de carga HX711 y sensor infrarrojo MLX90614. |
| **RF16** | Alerta por temperatura de alimento | **OK** | Web, Backend | Detección térmica fuera del rango seguro (35.0–38.5 °C). |
| **RF17** | Cálculo automático de volumen consumido | **OK** | Web, Backend, BD | $ml = \text{round}\left(\frac{m_i - m_f}{1.03}, 1\right)$ con densidad de leche de fórmula. |
| **RF18** | Recordatorio dinámico de tomas | **ADAPTADO** | Backend, Telegram, BD | Servicio en segundo plano con intervalo de 1 a 8 h que se reinicia con cada nueva toma. |
| **RF19** | Despacho redundante de alertas por Telegram | **ADAPTADO** | Backend, Telegram | Envío simultáneo a todos los chats de Telegram vinculados a la familia (Decisión D6). |
| **RF20** | Watchdog de cuna en nube | **OK** | Backend, Telegram | Heartbeat cada 15 s; declaración de desconexión y alerta tras 60 s sin señal. |
| **RF21** | Operación offline (contingencia local) | **ADAPTADO** | FW central, FW colchón | Búfer circular flash de 48 h en ~2 MB con reenvío ordenado tras reconexión (D12). |
| **RF22** | Revocación de usuario secundario | **ADAPTADO** | Web, Backend, BD | Pérdida inmediata de acceso ($< 5$ s) y desvinculación de Telegram mediante RLS (D9). |
| **RF23** | Gestión de perfiles de cuidador | **Media** | Web, BD | Hasta 6 perfiles activos con eliminación lógica (`eliminado_en`). |
| **RF24** | Registro y vinculación de cuna | **ADAPTADO** | Web, Backend, BD | Código de reclamo aleatorio (10 min, máx. 5 intentos) y token de 32 bytes (D1). |
| **RF25** | Registro de periféricos inalámbricos | **ADAPTADO** | Web, Backend | Reclamo individual por tipo (`central`, `colchon`, `movil`, `biberon`, `juguete`). |
| **RF26** | Listado y renombrado de cunas | **OK** | Web, BD | Actualización restringida al Cuidador Principal (1 a 30 caracteres). |
| **RF27** | Desvinculación de cuna | **ADAPTADO** | Web, Backend | Cierre inmediato de sesiones TCP, revocación de tokens y preservación del historial. |
| **RF28** | Registro del perfil del bebé | **OK** | Web, BD | Nombre, fecha de nacimiento (no futura) y notas médicas vinculadas a la cuna. |
| **RF29** | Alerta por movimiento anómalo | **OK** | FW colchón, Backend, Telegram | Alerta disparada tras 30 s sostenidos de fluctuación anormal. |
| **RF30** | Consulta del historial de sueño | **OK** | Web, BD | Registro de siestas y noches consultables en $< 3$ s. |
| **RF31** | Consulta del historial de alimentación | **OK** | Web, BD | Registro detallado de tomas consultables en $< 3$ s. |
| **RF32** | Exportación del historial infantil | **OK** | Web, Backend | Generación de reportes PDF y CSV en $< 10$ s con neutralización de inyección de fórmulas. |
| **RF33** | Registro de uso del móvil de LEDs | **OK** | Backend, BD, FW móvil | Registro de evento `session_end` con duración efectiva y patrón utilizado. |
| **RF34** | Mecánica de juego del juguete cognitivo | **OK** | FW juguete | Iluminación de objetivo aleatorio y detección de pulsación en $< 200$ ms. |
| **RF35** | Reinicio del juego por fallo | **OK** | FW juguete | Sonido de fallo y reinicio de racha de aciertos. |
| **RF36** | Aviso de batería baja del juguete | **ADAPTADO** | FW juguete, Web | Telemetría con porcentaje de batería; alerta cuando cae por debajo del 20 %. |
| **RF37** | Inicio de sesión de alimentación | **OK** | Web, Backend, BD | Validación de masa inicial mínima ($m_i \ge 20$ g). |
| **RF38** | Vinculación del bot de Telegram | **ADAPTADO** | Web, Backend, Telegram | Código temporal de 6 dígitos emitido desde la web y verificado vía `/vincular CODIGO`. |
| **RF39** | Consulta de estado desde Telegram | **OK** | Backend, Telegram | Comando `/estado` restringido a miembros autenticados con respuesta en $< 2$ s. |

### 3.2 Requisitos No Funcionales (RNF01 – RNF06)

| ID | Requisito | Estado | Justificación / Implementación |
|:---|:---|:---:|:---|
| **RNF01** | Latencia de despacho de alertas | **ADAPTADO** | Notificación en web por Realtime/WebSocket en $< 1$ s; despacho a Telegram en $< 2$ s. |
| **RNF02** | Seguridad y aislamiento de datos | **ADAPTADO** | RLS estricto en BD, tokens hasheados en SHA-256; transporte TCP local sin cifrar (Decisión D1 aprobada). |
| **RNF03** | Disponibilidad del backend | **ADAPTADO** | Demostración local en laptop (localhost) aceptada para el alcance académico (Decisión D7). |
| **RNF04** | Resiliencia y búfer local | **ADAPTADO** | Búfer circular de 48 h (2880 registros agregados) en la memoria flash de cada Pico W (Decisión D12). |
| **RNF05** | Eficiencia operativa de la interfaz | **OK** | Diseño adaptativo con Tailwind CSS y componentes accesibles (selector de perfil $\le 2$ toques). |
| **RNF06** | Mantenibilidad y portabilidad | **PARCIAL** | Arquitectura desacoplada en C# y React; OTA descartado para la maqueta física (Decisión D13). |

---

## 4. Resultados de la Suite de Pruebas Automatizadas

El sistema cuenta con una batería completa de pruebas automáticas en todos los lenguajes del ecosistema:

```
===================================================================================
SUITE DE PRUEBAS AUTOMATIZADAS — RESULTADOS GLOBALES (HITO M11)
===================================================================================
1. Backend C# (.NET 8 LTS — xUnit):
   - Archivo de solución: backend/CunaServer.sln
   - Pruebas ejecutadas: 54 pruebas
   - Estado: 54 PASADAS / 0 FALLIDAS (Duración: 1 s)
   - Cobertura: Cuentas familiares, RLS, contratos HTTP PostgREST con mock
     (PostgrestFamilyAccountRepository y PostgrestDeviceTelemetryRepository),
     TCP framing (ArrayPool TcpLineReader + DeviceTcpServer UTF-8), máquina de estados, consolidación
     multi-dispositivo, watchdog, bot de Telegram HTTP + deep-link /start + 429 retry,
     spikes de audio, cálculo de ml, CSV sanitization, PDF WinAnsiEncoding + envoltura
     por ancho en puntos Helvetica <= 500 pt / 92 columnas y checklist E2E.

2. Simuladores TCP (Python 3 — unittest):
   - Módulo: simulators/test_simulators.py
   - Pruebas ejecutadas: 10 pruebas
   - Estado: 10 PASADAS / 0 FALLIDAS (Duración: 0.027 s)
   - Cobertura: Detección de retiro <= 1 s, filtro de ruido 5 Hz O(1), rampa de luz PWM,
     decodificación PCM de audio, rechazo de tramas > 4 KB, estrés 10000 ticks,
     backoff <= 30 s y manejo de comandos intercalados antes del ack.

3. Firmware MicroPython (RP2040 — unittest):
   - Módulo: firmware/test_firmware.py
   - Pruebas ejecutadas: 7 pruebas
   - Estado: 7 PASADAS / 0 FALLIDAS (Duración: 0.009 s)
   - Cobertura: Asignación de pines RP2040 (GPIO 0-28), búfer circular de 48 h (2880 reg),
     máquina de estados de los 5 nodos, compatibilidad de notas SQL/memoria y vaciado TCP.

4. Frontend React (TypeScript + Node Test + Vitest + Vite):
   - Pruebas unitarias de helpers: formatters.test.ts (9 PASADAS / 0 FALLIDAS)
   - Pruebas interactivas de componentes UI (Vitest + JSDOM + Testing Library): 34 PASADAS / 0 FALLIDAS en 5 archivos (`CribAndDeviceManager.test.tsx`, `FeedingToysAndHistoryPanel.test.tsx`, `LiveCribPanel.test.tsx`, `TelegramAndRemindersPanel.test.tsx`, `OtherPanels.test.tsx`)
   - Total Frontend (`npm test`): 43 PASADAS / 0 FALLIDAS
   - Verificación de tipos: tsc --noEmit / tsc -b (0 ERRORES)
   - Empaquetado de producción: vite build (2098 módulos transformados)

5. Diagnóstico y Validación Telegram Bot API (scripts/test_telegram.py):
   - Mock HTTP Bot API (`--mock`): getMe + sendMessage chat_id=990001234 [OK]

6. Auditoría de Seguridad (verify_environment.py):
   - Escaneo de secretos reales (sb_secret, tokens, contraseñas): 0 COINCIDENCIAS
===================================================================================
TOTAL: 114 pruebas automáticas + verificación HTTP Bot API mock con 100 % de éxito.
===================================================================================
```

### 4.1 Métricas de Rendimiento y Refinamiento Técnico (Hito M11)
- **Parser TCP (`TcpLineReader` con `ArrayPool<byte>` y serialización UTF-8 directa en `DeviceTcpServer`)**: `10 000` líneas procesadas en `3.38 ms` (`2 958 055 líneas/s`); validación completa de `10 000` sobres `TcpProtocolParser` (telemetría + eventos) en `49.47 ms` (`202 128 sobres/s`).
- **Validación JWT ES256 con caché JWKS (`CachedJwksSnapshot`)**: `2 000` verificaciones en `105.46 ms` (`18 965 ops/s`).
- **Latencia de consulta en vivo (`GET /api/cribs/{id}/live`)**: `50` consultas en `30.31 ms` (promedio `0.61 ms/req`).
- **Exportación RF32 (CSV + PDF, 120 registros)**: `21.17 ms` (presupuesto `< 10 000 ms`); PDF multipágina verificado con `pdfinfo`/`pdftotext`/`pdftoppm` (Letter 612x792 pts, envoltura proporcional Helvetica <= 500 pt / <= 92 columnas, margen inferior >= 50 pt).
- **Higiene, UI y robustez**: helper unificado de autorización familiar y endpoints, límite en colas de telemetría/alertas/auditoría en memoria, eliminación de consultas N+1 hacia PostgREST de Telegram, prevención de sobrescritura de sliders al editar en frontend, soporte real de micrófono Web Audio API en el intercomunicador, placeholders esqueletizados animados durante la carga de telemetría, disposición responsiva `<540 px` y contraste accesible WCAG AA, corrección de autocierre del formulario de umbrales (`showThresholdsFormRef`), parseo estricto de entradas numéricas opcionales (`parseOptionalFiniteNumber` evitando alertas falsas `0.0 °C`), ajuste de anchura en puntos y cota de encabezados en PDF RF32 (sin falsos `0.0 °C` en tomas abiertas), disposición determinista de `JsonDocument` en auditoría PostgREST y filtro explícito `family_id` en lectura de umbrales.

---

## 5. Auditoría de Seguridad y Cumplimiento (`docs/SECURITY.md`)

- [x] **Gestión de Secretos**: Ningún archivo `.env` o `secrets.py` está versionado. El archivo `.gitignore` y `.cursorignore` los excluyen explícitamente. Se proveen plantillas sin valores sensibles (`.env.example` y `secrets.example.py`).
- [x] **Aislamiento Multitenant (RLS)**: Las 18 tablas en PostgreSQL tienen RLS activado y forzado. No existen políticas `using (true)`. El acceso se valida uniendo con `public.usuarios` sobre el `auth.uid()`.
- [x] **Deducción de Identidad en Servidor**: Los endpoints deducen el `family_id` desde la base de datos a partir del token JWT; ningún payload del cliente puede suplantar la familia o la cuna asignada.
- [x] **Almacenamiento Criptográfico Seguro**: Todos los tokens de dispositivo, enlaces de invitación y códigos de Telegram se generan con aleatoriedad criptográfica (`RandomNumberGenerator`), se guardan como hashes SHA-256 y se evalúan mediante comparación en tiempo constante (`CryptographicOperations.FixedTimeEquals`).
- [x] **Protección contra Fuerza Bruta**: Límite estricto de 5 intentos fallidos para códigos de invitación y reclamo de dispositivos; bloqueo temporal por IP tras 3 intentos inválidos de `hello` en el servidor TCP.
- [x] **Prevención de Inyección CSV**: Neutralización de caracteres ejecutables (`=`, `+`, `-`, `@`, `\t`) al exportar historiales médicos y de alimentación.

---

## 6. Guía Rápida de Demostración Local

Para arrancar el ecosistema en la laptop de evaluación:

1. **Verificar el entorno en 1 comando**:
   ```bash
   python3 scripts/verify_environment.py
   ```
2. **Terminal 1 — Backend C# y Servidor TCP**:
   ```bash
   dotnet run --project backend/CunaServer/CunaServer.csproj
   ```
   *(Verificar en `http://localhost:5080/health` que responde `status: ok` y `tcpPort: 9000`)*.
3. **Terminal 2 — Frontend Web React**:
   ```bash
   cd frontend && npm run dev
   ```
   *(Abrir `http://localhost:5173` en el navegador)*.
4. **Terminal 3 — Simuladores de los 5 Dispositivos**:
   ```bash
   python3 -m simulators.run_all --host 127.0.0.1 --port 9000
   ```

El recorrido guiado para la presentación frente al tribunal examinador se encuentra detallado en [`docs/DEMO_GUIDE.md`](file:///home/frigid/Documents/Git/ProyectoModelado/docs/DEMO_GUIDE.md).

---

## 7. Conclusiones

El sistema cubre los objetivos definidos entre los hitos M0 y M11:
1. **Tolerancia a desconexiones**: los nodos mantienen un búfer local de 48 horas en MicroPython y vacían datos al reconectarse al servidor TCP.
2. **Aislamiento y seguridad**: todas las tablas tienen RLS activado, los tokens se guardan como hashes SHA-256 y las entradas se validan en servidor.
3. **Pruebas y mantenimiento**: el repositorio queda modularizado por capa y cuenta con 114 pruebas automáticas ejecutables en local.
4. **Listo para revisión**: cambios integrados por Pull Request con documentación, guion de demo y guías visuales actualizadas.
