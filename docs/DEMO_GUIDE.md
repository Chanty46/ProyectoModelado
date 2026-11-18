# Guion de Demostración en Vivo — Cuna Inteligente (Hito M11)

Este documento describe el paso a paso reproducible para presentar la demostración del **Ecosistema Ciberfísico para Supervisión Perimetral, Microclima y Nutrición Infantil** en la laptop del equipo (con simuladores en Python 3 o con las 5 Raspberry Pi Pico W en el hotspot local).

---

## 1. Preparación Previa (5 minutos antes de la defensa)

1. **Verificar que el proyecto de Supabase esté activo**:
   - Entrar al panel de Supabase (`rjklzbarjuaxdhryklzw`) para confirmar que la instancia no esté pausada por inactividad.
2. **Verificar variables de entorno locales (ignoradas por Git)**:
   - Confirmar que `backend/.env` y `frontend/.env` existen localmente a partir de `.env.example` (nunca mostrarlos en pantalla durante la presentación).
3. **Ejecutar verificación integral de salud y pruebas (1 comando)**:
   ```bash
   python3 scripts/verify_environment.py
   ```
   *(Comprueba herramientas, checklist de seguridad, pruebas .NET, simuladores, firmware MicroPython, pruebas unitarias/build de frontend React y simulación HTTP de Telegram Bot API)*.

### 1.1 Primera vez con una cuenta real (simulacro sin maqueta)

1. **Claves locales**: `python3 scripts/setup_local.py --env` (`python scripts/setup_local.py --env` en Windows) pide las claves ocultas y las escribe en `backend/.env` y `frontend/.env` (Enter = conservar el valor actual).
2. **Esquema en Supabase**: si la API responde `PGRST205` (tabla no encontrada), pegar `supabase/migrations/20261107200000_m1_esquema_inicial_rls.sql` en el SQL Editor del proyecto y ejecutarlo.
3. **Dispositivos "de fábrica"**: `python -I scripts/provision_demo_devices.py` registra `demo-central-01`, `demo-colchon-01`, `demo-movil-01`, `demo-biberon-01` y `demo-juguete-01` sin dueño e imprime un código de reclamo para cada uno (`--status` solo muestra el estado).
4. **Emparejar en la web** (Gestión de Cunas → Agregar dispositivo): primero la central (crea la cuna) y luego los periféricos en esa cuna. Copiar el token que se muestra una sola vez.
5. **Tokens para los simuladores**: `python3 scripts/setup_local.py --sim-tokens` los guarda en `simulators/devices.local.json` (ignorado por Git). `python -m simulators.run_all` usa ese archivo automáticamente si existe.
6. **Windows**: si "Acceso controlado a carpetas" de Defender está activo, trabajar el repositorio fuera de `Documentos` (por ejemplo `C:\dev`), o npm/dotnet no podrán escribir.

---

## 2. Arranque de los 3 Componentes en la Laptop

Abrir **3 terminales** en la raíz del repositorio:

- **Terminal 1 — Backend C# (.NET 8) + Servidor TCP (`:5080` y `:9000`)**:
  ```bash
  dotnet run --project backend/CunaServer/CunaServer.csproj
  ```
  Verificar en `http://localhost:5080/health` que responde `{"status":"ok","service":"CunaServer","tcpPort":9000,"connectedDevices":0}`.

- **Terminal 2 — Aplicación Web React + Vite (`:5173`)**:
  ```bash
  cd frontend && npm run dev
  ```
  Abrir `http://localhost:5173` en el navegador.

- **Terminal 3 — Simuladores de los 5 Dispositivos (o monitoreo de las 5 Pico W)**:
  ```bash
  python3 -m simulators.run_all --host 127.0.0.1 --port 9000
  ```

---

## 3. Recorrido de Demostración (10–12 minutos)

### Paso 1: Cuentas, Perfiles de Bebé y Control de Acceso (M2 — RF01, RF02, RF03, RF22, RF23)
1. Mostrar la **Barra Superior de Selección de Perfil** (`≤ 2 toques`) con alternancia instantánea entre los bebés activos de la familia (hasta 6 perfiles).
2. Demostrar el rol **Cuidador Principal** vs **Cuidador Secundario**:
   - Con el Principal, generar un enlace de invitación de un solo uso con vigencia de 24 horas (`RF02`).
   - Mostrar cómo un intento de acción administrativa por un Secundario devuelve `403 Forbidden` y queda registrado en `bitacora_seguridad` (`RF03`).

### Paso 2: Emparejamiento Seguro y Gestión de Cunas (M4 — RF24 a RF28)
1. En la sección **Gestión de Cunas y Emparejamiento**, seleccionar la cuna asignada al bebé activo.
2. Generar un **código de reclamo de 6 caracteres** (`10 min`, máx. `5 intentos`, almacenado como hash SHA-256) para reclamar un nodo (`central`, `colchon`, `movil`, `biberon` o `juguete`).
3. Mostrar que el token de dispositivo de 32 bytes se presenta **una sola vez** al emparejar y que al desvincular un dispositivo se cierra su sesión TCP de inmediato (`RF28`).

### Paso 3: Cuna en Vivo, Retiro Súbito y Modo Atención (M5 — RF04 a RF09, RF20, RF29)
1. En el **Panel de Cuna en Vivo**, observar el indicador de estado (**Monitoreo** en verde), masa en el colchón (`mass_scale = 0.2`, muñeco de `0.50–0.80 kg`), índice de movimiento a `5 Hz` y gráficas en tiempo real (`< 1 s`).
2. **Prueba de Modo Atención (`RF05`)**:
   - Activar **Modo Atención (15 min)** desde la web o simulando el botón táctil `TTP223` de la central (`button_attention`).
   - El estado cambia a **Atención** (LED amarillo en la central) y suprime alarmas de retiro mientras el cuidador atiende al bebé.
3. **Prueba de Retiro Súbito (`RF04` / `D14`)**:
   - Volver a **Monitoreo** y simular retiro súbito en el colchón (caída de `≥ 0.50 kg` a `< 0.20 kg` en `≤ 1 s`).
   - Mostrar la ventana de gracia de `5 s` y, al cumplirse sin activar Atención, el paso automático a **ALARMA** (LED rojo, alarma local y alerta crítica en la web y Telegram).
4. **Iluminación Nocturna (`RF09`) y Umbrales Ambientales (`RF07`/`RF08`)**:
   - Ajustar el control deslizante de luz nocturna (`0–100 %`, rampa suave PWM de `1.5 s` a `≥ 1 kHz`).

### Paso 4: Intercomunicador Push-to-Talk por TCP/PWM (M8 — RF11 / D5)
1. En el bloque **Intercomunicador de Voz (RF11)**, mantener presionado **Hablar al bebé** (o pulsar **Enviar Tono de Prueba 440 Hz**).
2. Mostrar cómo el navegador captura PCM 8-bit a `8 kHz`, el backend C# lo valida y reenvía como `command: audio_chunk` por TCP a la unidad `central`, y el contador de tramas recibidas incrementa en tiempo real.

### Paso 5: Bot de Telegram y Recordatorios Dinámicos (M6 — RF18, RF19, RF38, RF39)
1. Generar un código `/vincular CODIGO` (o usar el botón **Abrir en Telegram** con enlace profundo `/start CODIGO`) en la pestaña **Telegram y Recordatorios**.
   - *Nota técnica Bot API*: Telegram no permite a los bots iniciar mensajes directamente hacia números telefónicos (por ejemplo `+506 8000-0000`); el usuario debe enviar `/start` primero a `@CunasInteligentes_Modelado_Bot` para obtener un `chat_id` entero autorizado.
2. Usar **Enviar mensaje de prueba** (`POST /api/telegram/test` / `GET /api/telegram/status`) o `python3 scripts/test_telegram.py --mock` / `--probe-network` para validar el transporte HTTP hacia `api.telegram.org`, y enviar `/estado` al bot para mostrar la respuesta en `< 2 s`.
3. Configurar el intervalo de recordatorio de alimentación (`1–8 h`) y mostrar cómo una nueva toma reinicia automáticamente el temporizador (`RF18`).

### Paso 6: Alimentación Inteligente, Juguetes e Historiales Auditados (M7 — RF12 a RF17, RF30 a RF37)
1. En **Alimentación, Juguetes e Historiales**:
   - Iniciar una toma con `m_inicial = 150 g` (`≥ 20 g`, `RF37`) y probar una temperatura fuera de `35.0–38.5 °C` para disparar la alerta `RF16`.
   - Finalizar la toma con `m_final = 47 g` y verificar el cálculo automático `ml = round((150 - 47) / 1.03, 1) = 100.0 ml` (`RF17`).
2. Reproducir una canción de cuna por secuencia de notas PWM (`RF12` / `D12`), encender el móvil de 9 LEDs (`RF13`/`RF33`) y registrar una partida del juguete de 5 botones (`RF14`/`RF34`–`RF36`).
3. Descargar el **Reporte Exportable en CSV y PDF** (`< 10 s`, `RF32`) y verificar su registro en la bitácora de auditoría.

---

## 4. Plan de Contingencia Durante la Demostración

| Situación | Acción inmediata |
|---|---|
| Interferencia Wi-Fi en el salón con las Pico W | Ejecutar `python3 -m simulators.run_all --host 127.0.0.1 --port 9000` en la misma laptop (usa exactamente el mismo servidor TCP en `:9000`). |
| Bot de Telegram no responde a `/start` o `/estado` | Verificar que `backend/.env` existe con `TELEGRAM_BOT_TOKEN`, que `dotnet run --project backend/CunaServer/CunaServer.csproj` está activo y ejecutar `python3 scripts/test_telegram.py --delete-webhook` para comprobar `getMe` y limpiar webhooks previos. |
| Sin salida a Internet hacia Telegram API | Mostrar la cola de alertas despachadas en el panel web y en las pruebas de integración `TelegramAndRemindersTests`. |
| Reconexión tras corte de red (`RNF04` / `D12`) | Desconectar y reconectar el simulador o Pico W para demostrar el vaciado del búfer circular de 48 h (`2880` registros) con `seq` sin duplicados. |
