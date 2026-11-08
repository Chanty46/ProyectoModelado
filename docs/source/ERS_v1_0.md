**ESTÁNDAR IEEE 830-1998  |  INGENIERÍA DE REQUERIMIENTOS**

**Especificación de requerimientos**

**de software**

Proyecto:

** **

*Ecosistema Ciberfísico Distribuido para Supervisión Perimetral, Microclima y Nutrición Infantil*

| **Autores** | **Andrés Felipe Guerrero Cervantes 2026094459** **Santiago Allón Quesada** **2026099579** **Evan Alejandro Umaña Sojo** **2026009696** **Sebastián Lobo Mejía** **2026094098** **Jose Guillermo Mora Fajardo** **2026121465** |
| --- | --- |
| **Versión del Documento** | **Versión 1.0** |
| **Organización / Asignatura** | Modelado de Software — Ingeniería de Sistemas |
| **Fecha de Emisión** | 13 de octubre de 2026 |

# Tabla de Contenido

**Tabla de Contenido****:**

1.0 Introducción

1.1 Propósito

1.2 Alcance

1.3 Personal involucrado

1.4 Definiciones, acrónimos y abreviaturas

1.5 Referencias

1.6 Resumen

2 Descripción general

2.1 Perspectiva del producto

2.2 Funcionalidad del producto

2.3 Características de los usuarios

2.4 Restricciones

2.5 Suposiciones y dependencias

2.6 Evolución futura del sistema

3 Requisitos específicos

Requerimientos funcionales

Requerimientos no funcionales

3.1 Requisitos comunes de las interfaces

3.1.1 Interfaces de usuario

3.1.2 Interfaces de hardware

3.1.3 Interfaces de software

3.1.4 Interfaces de comunicaciones

3.2 Requisitos no funcionales

3.2.1 Requisitos de rendimiento

3.2.2 Seguridad

3.2.4 Fiabilidad

3.2.5 Disponibilidad

3.2.6 Mantenibilidad

3.2.7 Portabilidad

 

# 1.0 Introducción

Este documento corresponde a la Especificación de Requisitos de Software (ERS) para el sistema ciberfísico de supervisión y bienestar infantil "Producto". La especificación se estructura conforme a las directrices del estándar ANSI/IEEE Std. 830-1998 e ISO/IEC/IEEE 29148:2018, empleando la micro-sintaxis EARS (Easy Approach to Requirements Syntax) para la formulación de requisitos funcionales y el marco PLANguage para la cuantificación verificable de atributos de calidad.

## 1.1 Propósito

Establecer de manera completa, consistente, no ambigua y verificable las capacidades funcionales, interfaces externas, restricciones operativas y atributos de calidad del sistema "Producto", sirviendo como contrato técnico vinculante entre los arquitectos de hardware, desarrolladores de software, diseñadores de interfaces y verificadores de control de calidad.

## 1.2 Alcance

"Producto" es una solución integrada de hardware embebido, servicios en la nube y aplicaciones cliente diseñada para proporcionar un entorno seguro, monitorizado y configurable para el infante, permitiendo a los padres y cuidadores supervisar sus variables biométricas, ambientales y conductuales tanto de forma local como remota.

 

Lo que el sistema HARÁ:

●        Sensar masa (rango de 0.50 kg a 35.00 kg), presencia, retiro imprevisto y fluctuaciones físicas mediante un colchón inteligente con cuatro celdas de carga.

●        Capturar temperatura y humedad relativa en el microclima de la cuna con margen de error menor a ± 2 °C, emitiendo alertas ante desvíos de umbral.

●        Proveer iluminación nocturna dimerizable con rampa gradual de 1.5 s (PWM ≥ 1.0 kHz) y audio unidireccional (intercomunicador en tiempo real y canciones de cuna almacenadas localmente).

●        Enlazar periféricos de estimulación temprana y entretenimiento (móvil colgante de matriz LED y módulo cognitivo de botones vía BLE).

●        Registrar la alimentación mediante una base inteligente para biberón (cálculo diferencial de volumen consumido en mililitros y medición infrarroja de temperatura del líquido).

●        Gestionar cuentas familiares jerárquicas con trazabilidad por perfil de cuidador activo y almacenamiento seguro en Supabase (PostgreSQL con políticas RLS).

●        Emitir alertas críticas de forma redundante en menos de 2.0 s mediante notificaciones Push y pasarela de Bot en Telegram.

●        Mantener resiliencia local con retención mínima de 48 horas en memoria flash SPI y alarmas audibles autónomas de 60 dBA en caso de corte de red.

** **

Lo que el sistema NO HARÁ:

●        No suministrará leche, líquidos ni medicamentos de forma mecánica automática (se descartan dispensadores motorizados directos por seguridad física del menor).

●        No sustituirá el criterio médico profesional ni emitirá diagnósticos clínicos pediátricos automatizados.

●        No operará como equipo certificado de soporte vital o monitor médico hospitalario para unidades de cuidados intensivos.

 

Beneficios y Objetivos:

Objetivo general:

Proveer a familias o cuidadores una herramienta que permite supervisar local o remotamente la seguridad, entorno y alimentación de un infante.

Objetivos del proyecto:
 Construir un prototipo general que contenga sensores, actuadores, nube y página web, además de validar los requisitos cuantitativos como latencia, disponibilidad, usabilidad, resiliencia, mediante las pruebas descritas en los RNF.

 

Beneficios:

●        Reciben alertas críticas mediante Telegram y notificación push en menos de 2 segundos (P95) incluso fuera del hogar.

●        Pueden realizar acciones de cuidado como alimentar, entretener y vigilar al infante con solo un botón mediante una Página Web.

●        Se comparte el cuidado del infante entre varios adultos con permisos diferenciados y registros sobre el infante.

●        El infante mantiene un entorno cómodo según los parámetros configurado por los padres.

●        El biberón verifica la temperatura antes del consumo para evitar posibles quemaduras o disgustos con el líquido

●        El bebé dispone de medios de entretenimiento mediante estimulaciones visuales y sonoras

●        Cuenta con historial de movimiento y alimentación a la que los padres pueden consultar y compartir si se desea

●        Conserva el monitoreo local y las funciones automatizadas en caso de que se pierda la conexión.

 

## 1.3 Personal involucrado

| **Nombre** | **Andrés Felipe Guerrero Cervantes** |
| --- | --- |
| **Rol** | Arquitecto de Sistemas Embebidos & Líder de Firmware IoT |
| **Categoría Profesional** | Estudiante de Ingeniería en Computadores (TEC) |
| **Responsabilidad** | Diseñar la arquitectura de hardware de la Unidad Central y periféricos, programar el firmware de adquisición de sensores (I2C, I2S, HX711), gestionar el almacenamiento resiliente en Flash SPI y los enlaces de comunicación BLE GATT y MQTT sobre TLS. |
| **Información de contacto** | Carné: 2026094459 — Instituto Tecnológico de Costa Rica |

 

| **Nombre** | **Santiago Allón Quesada** |
| --- | --- |
| **Rol** | Líder de Desarrollo Frontend, UX/UI & Streaming Multimedia |
| **Categoría Profesional** | Estudiante de Ingeniería en Computadores (TEC) |
| **Responsabilidad** | Desarrollar la aplicación web progresiva (PWA) responsiva optimizada para operación a una mano, integrar la visualización reactiva por WebSockets y gestionar el canal de audio unidireccional en tiempo real mediante protocolo WebRTC. |
| **Información de contacto** | Carné: 2026099579 — Instituto Tecnológico de Costa Rica |

 

| **Nombre** | **Evan Alejandro Umaña Sojo** |
| --- | --- |
| **Rol** | Arquitecto Cloud, Backend & Seguridad de Datos |
| **Categoría Profesional** | Estudiante de Ingeniería en Computadores (TEC) |
| **Responsabilidad** | Diseñar el esquema relacional en PostgreSQL (Supabase), implementar las políticas de aislamiento *Row-Level Security (RLS)*, configurar la autenticación federada Google OAuth 2.0 y administrar el bróker de mensajería en tiempo real. |
| **Información de contacto** | Carné: 2026009696 — Instituto Tecnológico de Costa Rica |

 

| **Nombre** | **Sebastián Lobo Mejía** |
| --- | --- |
| **Rol** | Product Owner & Líder de Ingeniería de Requerimientos |
| **Categoría Profesional** | Estudiante de Ingeniería en Computadores (TEC) |
| **Responsabilidad** | Definir la visión funcional del producto, gestionar y priorizar el *Product Backlog*, validar el cumplimiento de los criterios de aceptación con los interesados y supervisar la trazabilidad normativa del estándar EARS. |
| **Información de contacto** | Carné:  2026094098 — Instituto Tecnológico de Costa Rica |

 

 

| **Nombre** | **Jose Guillermo Mora Fajardo** |
| --- | --- |
| **Rol** | Líder de Aseguramiento de Calidad (QA), Telemetría & Integración Bot |
| **Categoría Profesional** | Estudiante de Ingeniería en Computadores (TEC) |
| **Responsabilidad** | Desarrollar la pasarela redundante de alertas y comandos con Telegram Bot API, ejecutar el plan de pruebas cuantitativas bajo métricas PLANguage (latencia P95, resiliencia offline, estrés) y auditar la confiabilidad extremo a extremo. |
| **Información de contacto** | Carné: 2026121465 — Instituto Tecnológico de Costa Rica |

 

## 1.4 Definiciones, acrónimos y abreviaturas

| **Nombre / Acrónimo** | **Descripción** |
| --- | --- |
| **Usuario Principal** | Madre, padre o tutor legal administrador de la cuenta familiar con plenas facultades técnicas para vincular hardware, gestionar miembros y configurar umbrales críticos. |
| **Usuario Secundario** | Cuidador autorizado (cónyuge, abuelos, niñera) con permisos operativos de supervisión y cuidado infantil, pero sin facultades administrativas. |
| **Perfil Activo** | Subentidad seleccionada en la página web al iniciar turno para firmar digitalmente y atribuir acciones en la bitácora operativa familiar. |
| **BLE** | Bluetooth Low Energy: tecnología inalámbrica de corto alcance y bajo consumo energético en la banda de 2.4 GHz. |
| **dBA** | Decibeles con ponderación A: unidad de nivel de presión sonora ajustada a la percepción auditiva humana. |
| **EARS** | Easy Approach to Requirements Syntax: marco gramatical estandarizado para la redacción estructurada y sin ambigüedad de requisitos funcionales. |
| **GATT** | Generic Attribute Profile: protocolo de transmisión de atributos y telemetría sobre enlaces BLE. |
| **Heartbeat** | Señal periódica de sincronización enviada cada 15 s por la cuna al servidor para validar su conexión activa en línea. |
| **HX711** | Convertidor analógico-digital (ADC) de 24 bits especializado en puentes de Wheatstone para celdas de carga. |
| **JWT** | JSON Web Token: estándar de autenticación y transmisión compacta de tokens criptográficos de sesión. |
| **Modo Atención** | Estado operativo temporal (15 minutos) en el cual la alarma acústica del colchón se inhibe para manipular al bebé sin detonar falsas alarmas. |
| **Modo Monitoreo** | Estado operativo en el cual la alarma perimetral de peso está armada y vigila continuamente la presencia del infante. |
| **P95** | Percentil 95: métrica estadística donde el 95 % de las observaciones se ubica por debajo del valor límite especificado. |
| **PLANguage** | Lenguaje de especificación cuantitativa desarrollado por Tom Gilb para estructurar requisitos y atributos de calidad auditables. |
| **PWM** | Pulse Width Modulation: modulación por ancho de pulsos para la regulación fina de brillo lumínico libre de parpadeo. |
| **RLS** | Row-Level Security: mecanismo de seguridad y aislamiento de base de datos a nivel de fila en PostgreSQL / Supabase. |
| **TLS 1.3** | Transport Layer Security 1.3: protocolo criptográfico para asegurar canales de comunicación en red. |
| **Bot de Telegram** | Canal conversacional de respaldo para alertas críticas, avisos y consultas breves. No admite audio ni configuración de hardware. |
| **Colchón Inteligente** | Subunidad con cuatro celdas de carga que mide la masa, la presencia, el movimiento y el retiro del infante. |
| **Cuna Inteligente** | Conjunto formado por la Unidad Central, el Colchón Inteligente y los actuadores integrados (luz, altavoz, matriz LED). Es el sujeto de los RF que describen el comportamiento del hardware de la cuna |
| **Base de Biberón** | Periférico con celda de masa (0 a 500 g) y sensor infrarrojo de temperatura que mide el biberón y la toma del infante. |
| **Página Web** | Pagina con la que los usuarios gestionan cuentas, supervisan la telemetría y controlan la cuna y sus periféricos. |

 

## 1.5 Referencias

| **Título del documento** | **Referencia** |
| --- | --- |
| **IEEE Std 830-1998: Recommended Practice for Software Requirements Specifications** | IEEE |
| **ISO/IEC/IEEE 29148:2018: Systems and software engineering — Life cycle processes — Requirements engineering** | ISO / IEC / IEEE |
| **Directrices EARS: Easy Approach to Requirements Syntax (2009)** | Alistair Mavin et al. (Rolls-Royce) |
| **Marco de Métricas Cuantitativas PLANguage: Competitive Engineering (2005)** | Tom Gilb |

 

## 1.6 Resumen

El presente documento detalla la especificación técnica del sistema "Producto" en tres secciones principales. La Sección 1 formaliza el propósito, el alcance funcional, los perfiles de los responsables, el glosario de términos y las referencias normativas. La Sección 2 describe la arquitectura de subsistemas del producto, sus funciones principales, las características de los usuarios, las restricciones operativas, las dependencias y la evolución futura. Finalmente, la Sección 3 especifica las fichas técnicas y el catálogo normativo de interfaces externas, requisitos funcionales regidos por EARS y requisitos no funcionales cuantificados mediante PLANguage.

# 2 Descripción general

## 2.1 Perspectiva del producto

El sistema "Producto" es un ecosistema ciberfísico distribuido diseñado específicamente para supervisar la seguridad perimetral, el microclima, la estimulación temprana y la nutrición del infante. El producto integra siete subsistemas coordinados:

- Unidad Central de la Cuna: Dispositivo embebido con microcontrolador que enlaza el sensor ambiental I2C, el altavoz, el micrófono I2S, la memoria flash SPI, el botón capacitivo y la lámpara dimerizable PWM.

- Colchón Inteligente: Superficie sensora con cuatro celdas de carga en puente de Wheatstone y un amplificador de 24 bits (HX711), conectada por bus alámbrico blindado a la Unidad Central.

- Móvil Colgante de LEDs: Periférico accesorio con una matriz de LEDs 3x3, conectado físicamente a la Unidad Central.

- Base de Biberón: Periférico accesorio con sensor de temperatura infrarrojo sin contacto (MLX90614) y celda de masa de alta precisión (0 g a 500 g), enlazado por BLE.

- Juguete de Botones: Juguete lúdico-cognitivo independiente, alimentado por baterías y sincronizado por BLE con perfil GATT.

Nube Central (Supabase). Backend con base de datos PostgreSQL protegida con RLS, autenticación federada OAuth 2.0, almacenamiento y sincronización en tiempo real por WebSockets.

Página Web (cliente primario). Aplicación web progresiva optimizada para la gestión operativa con una sola mano y visualización reactiva continua.

Bot de Telegram (canal de respaldo y alertas). Interfaz conversacional para la transmisión redundante de avisos

## 2.2 Funcionalidad del producto

Las funciones principales del producto comprenden:

●        Monitoreo biométrico y ambiental continuo (presencia, masa, índice de movimiento nocturno, temperatura y humedad relativa).

●        Detección de patrones anómalos (llanto acústico sostenido > 70 dBA, ausencia imprevista de peso < 1.00 kg, temperatura o humedad fuera de rango).

●        Comunicación y contención interactiva (audio unidireccional en streaming WebRTC, canciones de cuna locales y luminaria nocturna PWM).

●        Estimulación temprana y control de entretenimiento (animaciones temporizadas en matriz LED colgante y registro de interacción lúdica vía BLE).

●        Gestión integral de la alimentación (control térmico seguro del biberón entre 35.0 °C y 38.5 °C, cálculo diferencial de dosis ingerida y recordatorios programados de tomas).

●        Supervisión familiar jerárquica con trazabilidad de cuidadores mediante Perfil Activo y bitácora de auditoría en la nube.

**Figura 2.1 | ***Funcionalidad e interacción de actores en el ecosistema **"**Producto**"*

## 2.3 Características de los usuarios

| **Tipo de usuario** | **Usuario Principal (Administrador Familiar)** |
| --- | --- |
| **Formación** | Educación secundaria o superior; competencia digital media. |
| **Actividades** | • Autenticarse mediante Google OAuth 2.0 y crear la cuenta familiar. • Vincular y calibrar físicamente la Unidad Central de la Cuna. • Invitar o revocar permisos a Usuarios Secundarios. • Configurar y actualizar umbrales críticos de temperatura, humedad y seguridad. • Supervisar telemetría, controlar periféricos y administrar historiales clínicos/nutricionales. |

 

 

| **Tipo de usuario** | **Usuario Secundario (Cuidador Autorizado)** |
| --- | --- |
| **Formación** | Sin formación técnica requerida (cónyuge, abuelos, niñeras o familiares cercanos); competencia digital básica a media. |
| **Actividades** | • Visualizar en tiempo real la telemetría del colchón y el microclima de la cuna. • Activar el Modo Atención (pausa de 15 minutos) antes de levantar al bebé. • Regular el brillo de la lámpara nocturna, reproducir canciones de cuna y hablar por el intercomunicador. • Pesar el biberón, verificar su temperatura y registrar las tomas de leche. • Restricción: No posee privilegios para alterar umbrales de alarma, desvincular hardware ni administrar miembros. |

 

| **Tipo de usuario** | **Perfil Activo (Subentidad de Cuenta Familiar)** |
| --- | --- |
| **Formación** | No aplica (selección visual rápida en la pantalla de bienvenida). |
| **Actividades** | • Identificar al cuidador en turno en un máximo de 2 toques de pantalla. • Firmar y atribuir automáticamente cada acción en la bitácora operativa (ej. "Perfil Abuela registró toma de 120 ml"). |

 

## 2.4 Restricciones

●        Restricciones de Hardware: El altavoz de la cuna no deberá superar un nivel de presión sonora de 65 dBA a 30 centímetros de distancia para proteger el sistema auditivo del infante. La lámpara LED debe operar con control de corriente constante a frecuencia PWM ≥ 1.0 kHz libre de parpadeo perceptible (flicker-free).

●        Restricciones de Conectividad: La unidad embebida de la cuna requiere un enlace Wi-Fi estándar de 2.4 GHz (IEEE 802.11 b/g/n) y Bluetooth Low Energy (BLE). No se asume disponibilidad de bandas de 5 GHz en el microcontrolador.

●        Restricciones de Interfaz: El Bot de Telegram únicamente tramitará mensajes de texto plano estructurado, botones de confirmación inline y fragmentos breves de telemetría; las transmisiones de audio unidireccional y las configuraciones de hardware están restringidas exclusivamente a la Página Web.

●        Restricciones de Privilegios: El único rol con acceso a la configuración de umbrales, desvinculación de cuna y gestión de miembros será el Usuario Principal.

●        Restricciones Regulatorias y de Seguridad Física: Todo cable de alimentación debe contar con aislamiento anti-tensión y enrutamiento blindado fuera del alcance físico del infante. Los componentes en contacto indirecto deben poseer certificación de materiales no tóxicos libres de BPA.

## 2.5 Suposiciones y dependencias

●        Suministro Energético: La unidad central de la cuna depende de una toma de corriente residencial de 110 V / 220 V CA, contando con un módulo de batería de respaldo (Li-Ion) de al menos 2 horas de autonomía para sostener las funciones críticas de sensado y alarma local.

●        Infraestructura Cloud: El sistema asume la continuidad operativa de los servicios en la nube de Supabase (PostgreSQL, Auth, Realtime), Google OAuth 2.0 y de la infraestructura de la API de Telegram Bot.

●        Dispositivos Móviles: Se asume que los cuidadores cuentan con un teléfono inteligente con sistema operativo Android 10.0+ o iOS 14.0+, con conectividad a internet activa para la recepción de alertas remotas.

## 2.6 Evolución futura del sistema

●        Integración de algoritmos de visión por computadora mediante cámara infrarroja nocturna para el análisis postural del sueño.

●        Incorporación de modelos predictivos de aprendizaje automático para correlacionar variaciones de peso y agitación motriz con brotes de crecimiento o ciclos de cólicos.

●        Expansión del ecosistema con actuadores electromecánicos de balanceo automático suave accionados por detección de llanto.

# 3 Requisitos específicos

## Requerimientos funcionales

| **Identificador del Requerimiento** | **RF01 (REQ-AUTH-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Inicio de Sesión Principal mediante Google OAuth |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Simplifica el acceso eliminando contraseñas manuales débiles e indexa de forma segura el identificador único familiar (family_id). Precondición: Conectividad a internet activa en el terminal móvil. Postcondición: Sesión iniciada con emisión de token JWT y rol Usuario_Principal. |
| **Descripción del requerimiento** | CUANDO el Usuario Principal seleccione autenticarse mediante Google OAuth 2.0, la Página Web deberá validar el token de identidad con Supabase Auth, emitir un token JWT con rol Usuario_Principal y crear un registro de cuenta familiar en caso de ser un usuario nuevo. |
| **Requerimientos no funcionales** | ●         RNF02 ●         RNF03 ●         RNF05 |
| **Prioridad** | Alta |

 

| **Identificador del Requerimiento** | **RF02 (REQ-AUTH-002)** |
| --- | --- |
| **Nombre del Requerimiento** | Invitación de Usuario Secundario |
| **Patrón EARS** | Complejo (MIENTRAS... CUANDO) |
| **Características** | Permite la delegación controlada del acceso a familiares o cuidadores sin compartir credenciales primarias. Precondición: El usuario emisor debe poseer privilegios de administración familiar. Postcondición: Registro de invitación pendiente con vigencia de 24 horas creado en la base de datos. |
| **Descripción del requerimiento** | MIENTRAS el usuario mantenga una sesión activa con rol de Usuario Principal, CUANDO ingrese el correo electrónico de un tercero para autorizarlo, el Sistema de Backend deberá generar y remitir un enlace de invitación firmado criptográficamente con vigencia de 24 horas para vincular dicha cuenta como Usuario Secundario. |
| **Requerimientos no funcionales** | ●         RNF02 ●         RNF03 ●         RNF05 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF03 (REQ-AUTH-003)** |
| --- | --- |
| **Nombre del Requerimiento** | Restricción de Privilegios Administrativos |
| **Patrón EARS** | Comportamiento No Deseado (SI... ENTONCES) |
| **Características** | Garantiza la integridad del ecosistema familiar frente a modificaciones no autorizadas o accidentales desde cuentas subordinadas. Precondición: Petición originada desde una sesión con rol Usuario_Secundario. Postcondición: Transacción rechazada con código HTTP 403 y registro del evento en la bitácora de seguridad. |
| **Descripción del requerimiento** | SI un Usuario Secundario intenta alterar umbrales de alarma, revocar permisos a otros miembros o eliminar la cuna registrada, ENTONCES la Página Web deberá bloquear la acción con código HTTP 403, desplegar un mensaje de error indicando denegación por privilegios insuficientes y registrar el intento en la bitácora de auditoría de seguridad. |
| **Requerimientos no funcionales** | ●         RNF02 ●         RNF05 |
| **Prioridad** | Alta |

 

| **Identificador del Requerimiento** | **RF04 (REQ-SAFE-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Alarma por Retiro Imprevisto del Infante |
| **Patrón EARS** | Complejo (MIENTRAS... CUANDO... SI... ENTONCES) |
| **Características** | Alerta inmediatamente si el infante cae de la cuna o es sustraído sin previa autorización de los padres. Precondición: Masa previa registrada en el colchón ≥ 2.50 kg y estado del sistema en Modo Monitoreo Activo. Postcondición: Alarma sonora local activa a 60 dBA y mensaje de alerta crítica entregado a la nube. |
| **Descripción del requerimiento** | MIENTRAS el sistema se encuentre en Modo Monitoreo Activo, CUANDO la masa sensada caiga de un valor ≥ 2.50 kg a menos de 1.00 kg en un intervalo ≤ 1.0 s, SI el sistema no recibe un comando de desactivación o pausa en una ventana de gracia de 5.0 s, ENTONCES la Cuna Inteligente deberá accionar la alarma acústica local a 60 dBA (a 30 cm) y despachar una notificación de emergencia con prioridad crítica al Bot de Telegram. |
| **Requerimientos no funcionales** | ●         RNF01 ●         RNF02 ●         RNF03 ●         RNF04 |
| **Prioridad** | Crítica |

 

| **Identificador del Requerimiento** | **RF05 (REQ-SAFE-002)** |
| --- | --- |
| **Nombre del Requerimiento** | Pausa de Seguridad - Modo Atención |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Permite a los padres levantar al infante para alimentarlo o consolarlo sin detonar alarmas de seguridad. Precondición: Cuna en operación normal. Postcondición: Temporizador de atención de 15 minutos iniciado y suspensión temporal de disparadores de peso. |
| **Descripción del requerimiento** | CUANDO un usuario autenticado presione el botón de Modo Atención en la página web o en el pulsador físico de la cuna, el Sistema deberá inhibir la alarma sonora del colchón durante un lapso de 15 minutos (900 s), conmutar la indicación luminosa de estado a color amarillo y rearmar automáticamente el Modo Monitoreo al concluir el tiempo. |
| **Requerimientos no funcionales** | ●         RNF01 ●         RNF04 ●         RNF05 |
| **Prioridad** | Alta |

 

| **Identificador del Requerimiento** | **RF06 (REQ-SAFE-003)** |
| --- | --- |
| **Nombre del Requerimiento** | Registro de Fluctuación y Movimiento Nocturno |
| **Patrón EARS** | Dirigido por Estado (MIENTRAS) |
| **Características** | Construye la curva de agitación nocturna para evaluar la profundidad y calidad del sueño del bebé. Precondición: Peso estable sensado mayor a 1.00 kg. Postcondición: Registro numérico agregado cada minuto en la tabla de telemetría de sueño. |
| **Descripción del requerimiento** | MIENTRAS el bebé permanezca sobre el colchón en Modo Monitoreo con masa > 1.00 kg, la Cuna Inteligente deberá muestrear las variaciones transitorias de masa a una frecuencia de 5 Hz y persistir en Supabase el índice acumulado de movimiento cada 60 s. |
| **Requerimientos no funcionales** | ●         RNF02 ●         RNF03 ●         RNF04 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF07 (REQ-ENV-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Detección de Desvío Térmico o de Humedad |
| **Patrón EARS** | Comportamiento No Deseado (SI... ENTONCES) |
| **Características** | Previene estrés térmico o riesgos de hipertermia/hipotermia en la habitación del infante. Precondición: Umbrales personalizados configurados previamente en la base de datos. Postcondición: Mensaje con magnitud leída y umbral transgredido entregado al usuario. |
| **Descripción del requerimiento** | SI el sensor ambiental I2C detecta durante al menos 10 s consecutivos una temperatura o humedad relativa inferior al umbral mínimo o superior al umbral máximo configurado en la página web, ENTONCES la Cuna Inteligente deberá enviar una señal de alerta inmediata a Supabase para notificar a los cuidadores mediante Telegram y notificación Push. |
| **Requerimientos no funcionales** | ●         RNF01 ●         RNF03 ●         RNF04 |
| **Prioridad** | Alta |

 

| **Identificador del Requerimiento** | **RF08 (REQ-ENV-002)** |
| --- | --- |
| **Nombre del Requerimiento** | Ajuste Dinámico de Umbrales Ambientales |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Adecua las condiciones de supervisión según la estación climática o prescripción pediátrica. Precondición: Cuna enlazada a la red Wi-Fi. Postcondición: Variables de frontera actualizadas en la memoria no volátil del microcontrolador local. |
| **Descripción del requerimiento** | CUANDO el Usuario Principal actualice los valores límites de temperatura o humedad en la página web, el Sistema de Backend deberá sincronizar vía MQTT los nuevos parámetros con la memoria no volátil de la Cuna en menos de 2.0 s. |
| **Requerimientos no funcionales** | ●         RNF01 ●         RNF02 ●         RNF05 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF09 (REQ-AV-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Regulación Gradual de Iluminación Nocturna |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Evita cambios bruscos de intensidad lumínica que provoquen el despertar súbito del infante. Precondición: Lámpara de la cuna energizada. Postcondición: Nivel de luminosidad estabilizado en el porcentaje solicitado (0 % a 100 %). |
| **Descripción del requerimiento** | CUANDO un usuario envíe desde la página web un comando de encendido, apagado o cambio de brillo, la Cuna Inteligente deberá ajustar la salida PWM de la luz aplicando una rampa de transición lineal suave con una duración de 1.5 s entre el valor actual y el valor destino (0 % equivale a luz apagada). |
| **Requerimientos no funcionales** | ●         RNF01 ●         RNF05 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF10 (REQ-AV-002)** |
| --- | --- |
| **Nombre del Requerimiento** | Detección Acústica de Llanto |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Alerta a los padres ante episodios de malestar o necesidad de asistencia inmediata del infante descartando ruidos breves. Precondición: Micrófono I2S activo y libre de fallos de hardware. Postcondición: Marca temporal de llanto registrada en la base de datos y alerta emitida. |
| **Descripción del requerimiento** | CUANDO el micrófono ambiental registre una presión sonora sostenida superior a 70 dBA durante más de 3.0 s consecutivos, la Cuna Inteligente deberá catalogar el evento como llanto infantil y despachar una notificación de advertencia a la página web y al Bot de Telegram. |
| **Requerimientos no funcionales** | ●         RNF01 ●         RNF03 ●         RNF04 |
| **Prioridad** | Alta |

 

| **Identificador del Requerimiento** | **RF11 (REQ-AV-003)** |
| --- | --- |
| **Nombre del Requerimiento** | Transmisión de Voz unidireccional (Intercomunicador) |
| **Patrón EARS** | Dirigido por Estado (MIENTRAS) |
| **Características** | Permite el consuelo verbal remoto del menor sin intermediación física protegiendo su audición. Precondición: Canal de audio inicializado y permisos de micrófono concedidos en el teléfono móvil. Postcondición: Audio reproducido en el altavoz con latencia ≤ 250 ms y presión sonora ≤ 65 dBA. |
| **Descripción del requerimiento** | MIENTRAS el cuidador mantenga presionado el botón de intercomunicador en la página web, el Sistema deberá capturar el flujo de audio del terminal móvil y reproducirlo en el altavoz de la Cuna mediante WebRTC (con cifrado DTLS/SRTP) en tiempo real con latencia ≤ 250 ms y volumen limitado a ≤ 65 dBA. |
| **Requerimientos no funcionales** | ●         RNF01 ●         RNF02 ●         RNF05 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF12 (REQ-AV-004)** |
| --- | --- |
| **Nombre del Requerimiento** | Reproducción de Pistas Pregrabadas |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Facilita la conciliación del sueño infantil mediante estimulación sonora continua e independiente de la conexión a internet. Precondición: Archivo de audio presente en el almacenamiento local de la cuna. Postcondición: Reproductor de audio embebido en ejecución continua. |
| **Descripción del requerimiento** | CUANDO el usuario seleccione una pista de ruido blanco o canción de cuna desde la página web, la Cuna Inteligente deberá reproducir el archivo de audio almacenado localmente en su memoria flash SPI con el volumen configurado (máximo 65 dBA). |
| **Requerimientos no funcionales** | ●         RNF04 ●         RNF05 |
| **Prioridad** | Baja |

 

| **Identificador del Requerimiento** | **RF13 (REQ-ENT-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Control del Móvil Colgante de LEDs |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Proporciona estimulación visual controlada y apagado automático para inducir el descanso. Precondición: Matriz de LEDs conectada físicamente al puerto de control superior de la cuna. Postcondición: Animación activa con temporizador regresivo de apagado. |
| **Descripción del requerimiento** | CUANDO el usuario ordene encender el juguete de LEDs desde la página web especificando tiempo de duración (1 a 30 minutos), patrón de animación y velocidad (lenta, media o rápida), la Cuna Inteligente deberá alimentar la matriz colgante y proyectar la animación seleccionada durante el tiempo configurado, y deberá apagarla de inmediato si el usuario ordena detenerla antes de que expire el temporizador. |
| **Requerimientos no funcionales** | ●         RNF05 ●         RNF06 |
| **Prioridad** | Baja |

 

| **Identificador del Requerimiento** | **RF14 (REQ-ENT-002)** |
| --- | --- |
| **Nombre del Requerimiento** | Sincronización del Juguete de Botones vía BLE |
| **Patrón EARS** | Característica Opcional / Complejo (DONDE... CUANDO) |
| **Características** | Registra el desarrollo motor y cognitivo del menor a partir de las interacciones con el juguete interactivo. Precondición: Batería del juguete de botones con carga operativa mayor al 10 %. Postcondición: Registro de actividad lúdica indexado bajo el perfil del infante. |
| **Descripción del requerimiento** | DONDE el Juguete de Botones interactivo mantenga un enlace BLE GATT activo con la Cuna, CUANDO el infante concluya una secuencia lúdica, el Juguete de Botones deberá transmitir el número de aciertos y el tiempo de juego en segundos a la Cuna para su posterior guardado en Supabase. |
| **Requerimientos no funcionales** | ●         RNF02 ●         RNF04 ●         RNF06 |
| **Prioridad** | Baja |

 

| **Identificador del Requerimiento** | **RF15 (REQ-FEED-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Sensado Térmico y Ponderal de la Base de Biberón |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Brinda retroalimentación objetiva sobre la preparación y masa inicial de la fórmula láctea. Precondición: Base sensora calibrada a cero (0 g) antes del apoyo. Postcondición: Datos de peso inicial y temperatura proyectados en la interfaz de usuario. |
| **Descripción del requerimiento** | CUANDO el cuidador coloque un biberón con masa ≥ 20 g sobre la base sensora, la Base Inteligente de Alimentación deberá medir la masa total del recipiente y la temperatura superficial del líquido, desplegando los valores en la página web en menos de 1.0 s. |
| **Requerimientos no funcionales** | ●         RNF01 ●         RNF05 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF16 (REQ-FEED-002)** |
| --- | --- |
| **Nombre del Requerimiento** | Alerta por Temperatura Inadecuada de Alimento |
| **Patrón EARS** | Comportamiento No Deseado (SI... ENTONCES) |
| **Características** | Evita quemaduras térmicas en la mucosa bucal del bebé o ingesta a temperaturas desfavorables. Precondición: Lectura válida emitida por el sensor infrarrojo MLX90614. Postcondición: Advertencia visual roja desplegada requiriendo confirmación manual del cuidador. |
| **Descripción del requerimiento** | SI la temperatura sensada en el biberón resulta menor a 35.0 °C o mayor a 38.5 °C en el instante de registro, ENTONCES la página web deberá mostrar una advertencia visual destacada indicando que el líquido no se encuentra en el rango seguro para consumo infantil. |
| **Requerimientos no funcionales** | ●         RNF01 ●         RNF05 |
| **Prioridad** | Alta |

 

| **Identificador del Requerimiento** | **RF17 (REQ-FEED-003)** |
| --- | --- |
| **Nombre del Requerimiento** | Cálculo Automático de Volumen Consumido |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Automatiza la cuantificación de la ingesta nutricional del bebé sin estimaciones subjetivas aplicando factor de conversión de densidad láctea (1.03 g/ml). Precondición: Registro previo de peso inicial capturado en el mismo ciclo. Postcondición: Registro histórico de toma creado en la tabla nutricional de Supabase. |
| **Descripción del requerimiento** | CUANDO el cuidador presione el botón de Finalizar Alimentación tras colocar nuevamente el biberón en la base, la Página Web deberá calcular la diferencia entre el peso inicial y el peso residual (ΔM = M_inicial - M_final), computar los mililitros ingeridos (ΔM / 1.03 g/ml) y almacenar el evento asociado al perfil del cuidador activo. |
| **Requerimientos no funcionales** | ●         RNF02 ●         RNF03 ●         RNF05 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF18 (REQ-FEED-004)** |
| --- | --- |
| **Nombre del Requerimiento** | Recordatorio Dinámico de Tomas |
| **Patrón EARS** | Complejo (MIENTRAS... CUANDO) |
| **Características** | Asiste a los cuidadores en el cumplimiento estricto de las pautas de alimentación pediátricas. Precondición: Registro de al menos una toma previa en la base de datos. Postcondición: Notificación de recordatorio remitida a todos los miembros de la familia. |
| **Descripción del requerimiento** | MIENTRAS el recordatorio automático de alimentación esté habilitado, CUANDO transcurra el intervalo de horas programado (configurable entre 1 y 8 horas) desde la última ingesta guardada, el Sistema de Backend deberá despachar un mensaje de recordatorio a los teléfonos vinculados mediante la página web y Telegram. |
| **Requerimientos no funcionales** | ●         RNF01 ●         RNF03 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF19 (REQ-NOTIF-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Despacho Redundante de Alertas por Telegram |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Asegura una vía alternativa y de alta penetración para avisos urgentes en caso de que la página web no esté en primer plano. Precondición: Bot de Telegram configurado con el Chat ID familiar correspondiente. Postcondición: Mensaje recibido en el telegram de mensajería del usuario en menos de 2.0 s. |
| **Descripción del requerimiento** | CUANDO se registre un evento de criticidad alta o de emergencia en la Cuna Inteligente, el Sistema de Backend deberá transmitir un mensaje estructurado con detalles del evento al chat de Telegram vinculado a la familia en un tiempo menor a 2.0 s. |
| **Requerimientos no funcionales** | ●         RNF01 ●         RNF02 ●         RNF03 |
| **Prioridad** | Crítica |

 

| **Identificador del Requerimiento** | **RF20 (REQ-NOTIF-002)** |
| --- | --- |
| **Nombre del Requerimiento** | Detección de Pérdida de Conexión - Watchdog en Nube |
| **Patrón EARS** | Comportamiento No Deseado (SI... ENTONCES) |
| **Características** | Erradica la falla silenciosa por desconexión de red o corte de suministro eléctrico. Precondición: La cuna debe haber estado previamente en estado de sincronización en línea. Postcondición: Estado de cuna marcado como Desconectada en la nube. |
| **Descripción del requerimiento** | SI el Sistema de Backend en la nube deja de recibir la señal periódica de latido (heartbeat emitido cada 15 s) de la Cuna durante más de 60 s continuos, ENTONCES el Sistema de Backend deberá remitir una notificación urgente por Telegram y Push indicando pérdida de comunicación con la cuna. |
| **Requerimientos no funcionales** | ●         RNF01 ●         RNF03 ●         RNF04 |
| **Prioridad** | Crítica |

 

| **Identificador del Requerimiento** | **RF21 (REQ-NOTIF-003)** |
| --- | --- |
| **Nombre del Requerimiento** | Modo de Contingencia Local - Operación Offline |
| **Patrón EARS** | Comportamiento No Deseado (SI... ENTONCES) |
| **Características** | Garantiza que la protección física y el sensado del infante no queden inoperativos por fallas de telecomunicaciones externas. Precondición: Cuna energizada (por red eléctrica o batería de respaldo). Postcondición: Transición a modo de almacenamiento local en cola circular de memoria flash SPI. |
| **Descripción del requerimiento** | SI la Cuna Inteligente experimenta una interrupción en el enlace Wi-Fi o en el acceso a internet, ENTONCES la Cuna deberá mantener activas todas las funciones locales de monitoreo del colchón, accionar sus alarmas acústicas de forma autónoma y almacenar las lecturas de los sensores en un búfer local de memoria no volátil hasta que el enlace sea restablecido, sincronizando automáticamente el historial retenido hacia Supabase al recuperar la conexión. |
| **Requerimientos no funcionales** | ●         RNF04 ●         RNF06 |
| **Prioridad** | Crítica |

 

| **Identificador del Requerimiento** | **RF22 (REQ-AUTH-004)** |
| --- | --- |
| **Nombre del Requerimiento** | Revocación de Usuario Secundario |
| **Patrón EARS** | Complejo (MIENTRAS... CUANDO) |
| **Características** | Permite al administrador retirar el acceso de un familiar o cuidador que ya no debe supervisar al infante, sin perder el historial existente. Precondición: Sesión activa con rol Usuario_Principal y al menos un Usuario Secundario vinculado a la familia. Postcondición: Vínculo eliminado, tokens JWT invalidados y evento registrado en la bitácora de auditoría. |
| **Descripción del requerimiento** | MIENTRAS el usuario mantenga una sesión activa con rol de Usuario Principal, CUANDO seleccione eliminar a un Usuario Secundario de la familia y confirme la acción, el Sistema de Backend deberá revocar el vínculo de dicho usuario con la familia, invalidar sus tokens JWT vigentes en menos de 5.0 s, conservar sin alteración los registros históricos atribuidos a sus perfiles y notificar el resultado al Usuario Principal. |
| **Requerimientos no funcionales** | RNF02 RNF03 RNF05 |
| **Prioridad** | Alta |

 

| **Identificador del Requerimiento** | **RF23 (REQ-AUTH-005)** |
| --- | --- |
| **Nombre del Requerimiento** | Gestión de Perfiles de Cuidador |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Permite que varios cuidadores compartan una misma cuenta familiar con identidad propia (modelo de perfiles), habilitando la trazabilidad del Perfil Activo. Precondición: Sesión activa con rol Usuario_Principal. Postcondición: Perfil creado, modificado o eliminado y disponible en el selector de perfiles (IU-02). |
| **Descripción del requerimiento** | CUANDO el Usuario Principal cree, edite o elimine un perfil de cuidador indicando nombre (1 a 30 caracteres) y avatar, con un máximo de 6 perfiles por familia, el Sistema de Backend deberá persistir el cambio asociado al family_id, reflejarlo en el selector de perfiles de todos los dispositivos vinculados en menos de 2.0 s y, en caso de eliminación, conservar los registros históricos atribuidos a dicho perfil marcándolos como "perfil eliminado". |
| **Requerimientos no funcionales** | RNF02 RNF05 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF24 (REQ-DEV-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Registro y Vinculación de la Cuna |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Establece la asociación inequívoca entre la unidad física y la familia; es requisito previo de toda telemetría y alerta. Precondición: Sesión activa de Usuario_Principal y Unidad Central energizada, en modo de emparejamiento y con enlace Wi-Fi. Postcondición: Cuna registrada en Supabase bajo el family_id con estado En línea y heartbeat iniciado. |
| **Descripción del requerimiento** | CUANDO el Usuario Principal ingrese o escanee el código de emparejamiento de la Unidad Central de la Cuna desde la página web, el Sistema de Backend deberá validar que el código no esté asociado a otra familia, registrar la cuna bajo el family_id del usuario, provisionar sus credenciales de comunicación MQTT y confirmar el resultado en la página web en menos de 10.0 s; si el código ya está vinculado, deberá rechazar el registro e informar el motivo. |
| **Requerimientos no funcionales** | RNF02 RNF03 RNF05 |
| **Prioridad** | Alta |

 

| **Identificador del Requerimiento** | **RF25 (REQ-DEV-002)** |
| --- | --- |
| **Nombre del Requerimiento** | Registro de Periféricos Inalámbricos |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Permite incorporar el Juguete de Botones y la Base de Biberón al ecosistema desde la página web. Precondición: Cuna registrada (RF24) y periférico encendido en modo de emparejamiento BLE. Postcondición: Periférico registrado con tipo, identificador y estado en Supabase y disponible en la página web. |
| **Descripción del requerimiento** | CUANDO el Usuario Principal inicie el emparejamiento de un periférico desde la página web, la Cuna Inteligente deberá escanear dispositivos BLE durante un máximo de 30 s, enlazar el periférico seleccionado mediante perfil GATT y registrar en Supabase su tipo (Juguete de Botones o Base de Biberón), su identificador único y su nivel de batería cuando aplique. |
| **Requerimientos no funcionales** | RNF02 RNF05 RNF06 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF26 (REQ-DEV-003)** |
| --- | --- |
| **Nombre del Requerimiento** | Listado y Renombrado de Cunas |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Permite identificar cada cuna con un nombre propio y administrar varias cunas bajo una misma familia. Precondición: Al menos una cuna registrada y sesión de Usuario_Principal. Postcondición: Nombre actualizado y visible para todos los miembros de la familia. |
| **Descripción del requerimiento** | CUANDO el Usuario Principal acceda a la sección de administración de cunas, la página web deberá listar las cunas registradas en la familia con su nombre y su estado de conexión (En línea o Desconectada) y permitir modificar el nombre de cada una (1 a 30 caracteres), propagando el cambio a todos los miembros en menos de 2.0 s. |
| **Requerimientos no funcionales** | RNF02 RNF05 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF27 (REQ-DEV-004)** |
| --- | --- |
| **Nombre del Requerimiento** | Desvinculación de Cuna |
| **Patrón EARS** | Complejo (MIENTRAS... CUANDO) |
| **Características** | Permite liberar una cuna (reemplazo, falla o cambio de propietario) sin dejar credenciales activas ni alertas huérfanas. Precondición: Sesión de Usuario_Principal y cuna registrada. Postcondición: Cuna desvinculada, credenciales revocadas y alertas asociadas desactivadas. |
| **Descripción del requerimiento** | MIENTRAS el usuario mantenga una sesión activa con rol de Usuario Principal, CUANDO confirme la desvinculación de una cuna mediante una segunda confirmación explícita, el Sistema de Backend deberá revocar las credenciales MQTT de dicha cuna, detener el despacho de alertas asociadas a ella, conservar el historial del bebé y notificar a los demás miembros de la familia. |
| **Requerimientos no funcionales** | RNF02 RNF05 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF28 (REQ-BABY-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Registro del Perfil del Bebé |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Crea la entidad infante a la que se asocian la telemetría, el historial de sueño, la alimentación y el juego. Precondición: Cuna registrada (RF24) y sesión de Usuario_Principal. Postcondición: Perfil del bebé creado, asociado a la cuna y con acceso restringido a los miembros autorizados. |
| **Descripción del requerimiento** | CUANDO el Usuario Principal registre o actualice a un bebé ingresando nombre y fecha de nacimiento, el Sistema de Backend deberá crear o modificar el perfil del infante vinculado al family_id, asociarlo a la cuna seleccionada y restringir el acceso a sus datos exclusivamente a los miembros autorizados de la familia mediante políticas RLS. |
| **Requerimientos no funcionales** | RNF02 RNF05 |
| **Prioridad** | Alta |

 

| **Identificador del Requerimiento** | **RF29 (REQ-SAFE-004)** |
| --- | --- |
| **Nombre del Requerimiento** | Alerta por Movimiento Anómalo del Colchón |
| **Patrón EARS** | Comportamiento No Deseado (SI... ENTONCES) |
| **Características** | Avisa a los padres de agitación inusual del bebé, usando el índice de movimiento que ya calcula RF06. Precondición: Modo Monitoreo activo, masa > 1.00 kg y alerta de movimiento habilitada con un umbral configurado. Postcondición: Notificación con el índice medido y la hora del evento entregada por página web y Telegram. |
| **Descripción del requerimiento** | SI el índice de movimiento calculado a partir de las variaciones de masa supera el umbral configurado por el Usuario Principal durante al menos 30 s consecutivos con el sistema en Modo Monitoreo, ENTONCES la Cuna Inteligente deberá despachar una notificación de advertencia a la página web y al Bot de Telegram indicando el índice medido y la hora del evento. |
| **Requerimientos no funcionales** | RNF01 RNF03 RNF04 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF30 (REQ-HIST-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Consulta del Historial de Sueño (Siestas y Noches) |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Convierte los registros de movimiento de RF06 en una vista útil de siestas y noches. Se entiende por sesión de sueño el periodo continuo con masa > 1.00 kg en Modo Monitoreo. Precondición: Registros de movimiento persistidos en Supabase. Postcondición: Sesiones de sueño desplegadas con duración e índice de movimiento. |
| **Descripción del requerimiento** | CUANDO un usuario autorizado seleccione el historial de sueño y un rango de fechas, la página web deberá mostrar en menos de 3.0 s las sesiones de sueño del infante clasificadas como siesta o noche según su hora de inicio (franja nocturna por defecto de 19:00 a 06:00), con hora de inicio, hora de fin, duración e índice de movimiento promedio, además del total de horas dormidas por día. |
| **Requerimientos no funcionales** | RNF02 RNF05 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF31 (REQ-HIST-002)** |
| --- | --- |
| **Nombre del Requerimiento** | Consulta del Historial de Alimentación |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Permite revisar las tomas registradas para detectar cambios en la ingesta. Precondición: Tomas registradas según RF17. Postcondición: Listado de tomas desplegado con su total diario. |
| **Descripción del requerimiento** | CUANDO un usuario autorizado seleccione el historial de alimentación y un rango de fechas, la página web deberá mostrar en menos de 3.0 s las tomas del infante con fecha y hora, volumen ingerido en mililitros, temperatura registrada y perfil de cuidador responsable, además del total diario de mililitros. |
| **Requerimientos no funcionales** | RNF02 RNF05 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF32 (REQ-HIST-003)** |
| --- | --- |
| **Nombre del Requerimiento** | Compartir Historial del Infante |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Permite entregar reportes al pediatra u otros adultos sin compartir acceso a la cuenta, como ofrece el alcance (sección 1.2). Precondición: Usuario autorizado y datos existentes en el rango elegido. Postcondición: Reporte generado, compartido por el usuario y exportación registrada en la bitácora. |
| **Descripción del requerimiento** | CUANDO un usuario autorizado solicite compartir el historial de sueño o de alimentación de un rango de fechas y confirme una advertencia de privacidad, la página web deberá generar un reporte en formato PDF o CSV en menos de 10.0 s, abrir el menú de compartir del sistema operativo y registrar la exportación (fecha, perfil y rango) en la bitácora de auditoría. |
| **Requerimientos no funcionales** | RNF02 RNF05 |
| **Prioridad** | Baja |

 

| **Identificador del Requerimiento** | **RF33 (REQ-ENT-003)** |
| --- | --- |
| **Nombre del Requerimiento** | Registro de Uso del Juguete de LEDs |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Deja constancia del uso del móvil colgante, tal como piden los apuntes. Precondición: Sesión del juguete iniciada según RF13. Postcondición: Registro de uso almacenado en Supabase, o en el búfer local si no hay conexión. |
| **Descripción del requerimiento** | CUANDO finalice una sesión del Móvil Colgante de LEDs, ya sea por vencimiento del temporizador o por apagado manual, la Cuna Inteligente deberá registrar la hora de inicio, la duración efectiva, el patrón y la velocidad utilizados y el perfil de cuidador activo que la inició. |
| **Requerimientos no funcionales** | RNF02 RNF04 |
| **Prioridad** | Baja |

 

| **Identificador del Requerimiento** | **RF34 (REQ-ENT-004)** |
| --- | --- |
| **Nombre del Requerimiento** | Mecánica de Juego del Juguete de Botones |
| **Patrón EARS** | Complejo (MIENTRAS... CUANDO) |
| **Características** | Define el comportamiento del juguete de 5 botones con 5 LEDs. Precondición: Juguete encendido con batería > 10 % y un único LED iluminado. Postcondición: Contador de aciertos incrementado y un nuevo botón iluminado. |
| **Descripción del requerimiento** | MIENTRAS el juego esté activo, CUANDO el infante presione el botón iluminado, el Juguete de Botones deberá reproducir un sonido de acierto, apagar el LED de ese botón, iluminar un botón distinto elegido al azar en menos de 200 ms e incrementar el contador de aciertos. |
| **Requerimientos no funcionales** | RNF05 RNF06 |
| **Prioridad** | Baja |

 

| **Identificador del Requerimiento** | **RF35 (REQ-ENT-005)** |
| --- | --- |
| **Nombre del Requerimiento** | Reinicio del Juego por Fallo |
| **Patrón EARS** | Comportamiento No Deseado (SI... ENTONCES) |
| **Características** | Completa la mecánica del juego y define cuándo concluye una secuencia lúdica para el envío de resultados de RF14. Precondición: Juego activo con un botón iluminado. Postcondición: Contador reiniciado a cero y nueva partida lista. |
| **Descripción del requerimiento** | SI el infante presiona un botón cuyo LED está apagado mientras el juego está activo, ENTONCES el Juguete de Botones deberá reproducir un sonido distintivo de error, finalizar la secuencia (reportando aciertos y tiempo de juego conforme a RF14 cuando exista enlace BLE), reiniciar el contador de aciertos a cero e iluminar un nuevo botón en menos de 1.0 s. |
| **Requerimientos no funcionales** | RNF05 RNF06 |
| **Prioridad** | Baja |

 

| **Identificador del Requerimiento** | **RF36 (REQ-ENT-006)** |
| --- | --- |
| **Nombre del Requerimiento** | Aviso de Batería Baja del Juguete de Botones |
| **Patrón EARS** | Comportamiento No Deseado (SI... ENTONCES) |
| **Características** | El juguete funciona con baterías y RF14 exige una carga mayor al 10 %; este requisito avisa antes de llegar a ese punto. Precondición: Enlace BLE activo entre el juguete y la cuna. Postcondición: Aviso de batería baja visible en la página web. |
| **Descripción del requerimiento** | SI el nivel de batería del Juguete de Botones desciende por debajo del 10 %, ENTONCES el juguete deberá informarlo a la Cuna mediante BLE y la página web deberá mostrar un aviso de batería baja con el porcentaje restante en menos de 5.0 s. |
| **Requerimientos no funcionales** | RNF04 RNF06 |
| **Prioridad** | Baja |

 

| **Identificador del Requerimiento** | **RF37 (REQ-FEED-005)** |
| --- | --- |
| **Nombre del Requerimiento** | Inicio de Sesión de Alimentación |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Cubre el botón para alimentar de los apuntes y fija la masa inicial que RF17 necesita para calcular el volumen ingerido. Precondición: Biberón con masa ≥ 20 g sobre la base (RF15) y Perfil Activo seleccionado. Postcondición: Sesión de alimentación abierta con masa inicial, hora y perfil. |
| **Descripción del requerimiento** | CUANDO el cuidador presione el botón Iniciar Alimentación en la página web con un biberón sobre la base sensora, la página web deberá registrar la masa inicial medida (M_inicial), la temperatura del líquido, la hora de inicio y el perfil del cuidador activo, y mantener la sesión abierta hasta que se ejecute Finalizar Alimentación (RF17). |
| **Requerimientos no funcionales** | RNF01 RNF05 |
| **Prioridad** | Media |

 

| **Identificador del Requerimiento** | **RF38 (REQ-NOTIF-004)** |
| --- | --- |
| **Nombre del Requerimiento** | Vinculación del Chat de Telegram |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | RF19 asume un Chat ID familiar ya configurado; este requisito define cómo se obtiene. Precondición: Sesión de Usuario_Principal. Postcondición: Chat ID asociado al family_id y mensaje de confirmación enviado. |
| **Descripción del requerimiento** | CUANDO el Usuario Principal solicite vincular Telegram, la página web deberá generar un código de un solo uso con vigencia de 10 minutos que, al ser enviado al Bot, permita al Sistema de Backend asociar el Chat ID al family_id y responder con un mensaje de confirmación en menos de 5.0 s. |
| **Requerimientos no funcionales** | RNF02 RNF03 |
| **Prioridad** | Alta |

 

| **Identificador del Requerimiento** | **RF39 (REQ-NOTIF-005)** |
| --- | --- |
| **Nombre del Requerimiento** | Consulta de Estado desde Telegram |
| **Patrón EARS** | Dirigido por Eventos (CUANDO) |
| **Características** | Da la comunicación unidireccional con Telegram que piden los apuntes, dentro de la restricción 2.4 (solo texto y telemetría breve, sin configuración de hardware). Precondición: Chat de Telegram vinculado (RF38). Postcondición: Resumen de estado entregado al chat. |
| **Descripción del requerimiento** | CUANDO un chat vinculado envíe el comando /estado al Bot de Telegram, el Sistema de Backend deberá responder en menos de 2.0 s con un mensaje de texto que incluya la temperatura, la humedad relativa, el estado del colchón (presente, ausente o en Modo Atención) y el estado de conexión de la cuna, y no responderá a chats no vinculados. |
| **Requerimientos no funcionales** | RNF01 RNF02 RNF03 |
| **Prioridad** | Media |

 

 

## Requerimientos no funcionales

| **Identificador del Requerimiento** | **RNF01 (RNF-PERF-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Latencia_Despacho_Alertas_Emergencia |
| **Características** | Atributo: Rendimiento y Eficiencia Temporal. Tiempo transcurrido desde la detección de un evento crítico en la cuna (ej. retiro imprevisto) hasta su llegada al terminal móvil del usuario. |
| **Descripción del requerimiento** | ●         Escala: Milisegundos (ms) transcurridos en red pública de telecomunicaciones. ●         Métrica: Inyección de 50 eventos de prueba simulados cronometrando el arribo de la notificación Push y el mensaje de Telegram. ●         Umbral de Fallo: Mayor a 3,500 ms. ●         Umbral Objetivo: Menor a 2,000 ms (en percentil P95). |
| **Prioridad** | Crítica (En incidentes infantiles, los retrasos prolongados en las alertas incrementan el riesgo para la integridad del menor). |

 

| **Identificador del Requerimiento** | **RNF02 (RNF-SEC-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Seguridad_Cifrado_Aislamiento_Datos |
| **Características** | Atributo: Seguridad y Confidencialidad. Protección criptográfica de flujos de datos en tránsito y aislamiento lógico de información entre cuentas familiares. |
| **Descripción del requerimiento** | ●         Escala: Estándar criptográfico aplicado y cumplimiento de políticas de seguridad. ●         Métrica: Auditoría de vulnerabilidades con análisis estático de tráfico y pruebas de penetración en endpoints de API. ●         Umbral de Fallo: Uso de protocolos anteriores a TLS 1.3 o acceso cruzado entre identificadores de familia en Supabase. ●         Umbral Objetivo: 100 % del tráfico cifrado bajo TLS 1.3 con certificados válidos y aislamiento forzado mediante Row-Level Security (RLS) en PostgreSQL. |
| **Prioridad** | Crítica (La telemetría y los datos de infantes están sujetos a regulaciones estrictas de privacidad y protección de menores). |

 

 

| **Identificador del Requerimiento** | **RNF03 (RNF-AVAIL-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Disponibilidad_Servicio_Backend |
| **Características** | Atributo: Disponibilidad de Sistema. Fracción de tiempo en la cual los servicios de ingesta de telemetría y autenticación en la nube se encuentran en operación ininterrumpida. |
| **Descripción del requerimiento** | ●         Escala: Porcentaje de tiempo operativo mensual sin paradas imprevistas. ●         Métrica: Informes automáticos de monitoreo de disponibilidad del clúster de Supabase computados mensualmente. ●         Umbral de Fallo: Menor al 99.0 % mensual. ●         Umbral Objetivo: Mayor o igual al 99.8 % de disponibilidad mensual (excluyendo ventanas de mantenimiento informadas). ●         . |
| **Prioridad** | Alta (Un fallo prolongado del servidor inhabilita la supervisión remota de los cuidadores que no están en el hogar). |

 

| **Identificador del Requerimiento** | **RNF04 (RNF-REL-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Resiliencia_Buffer_Local_Desconexion |
| **Características** | Atributo: Fiabilidad y Tolerancia a Fallos. Capacidad de la cuna para almacenar lecturas de sensores localmente ante cortes prolongados de internet sin pérdida de información. |
| **Descripción del requerimiento** | ●         Escala: Horas de almacenamiento continuo de datos en memoria no volátil local a tasa nominal de muestreo. ●         Métrica: Desconexión intencional del enrutador Wi-Fi durante una prueba de laboratorio y verificación de descarga posterior hacia Supabase. ●         Umbral de Fallo: Menor a 12 horas de retención de telemetría sin desbordamiento de búfer. ●         Umbral Objetivo: Mínimo 48 horas continuas de retención de datos en memoria flash SPI integrada. |
| **Prioridad** | Alta (Las desconexiones nocturnas de red no deben traducirse en lagunas de información en los historiales de sueño y salud). |

 

 

| **Identificador del Requerimiento** | **RNF05 (RNF-USAB-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Eficiencia_Operativa_Interfaz_Cuidador |
| **Características** | Atributo: Usabilidad y Facilidad de Uso. Esfuerzo de interacción requerido para ejecutar las acciones frecuentes de cuidado infantil (iniciar alimentación, activar modo atención o encender luz). |
| **Descripción del requerimiento** | ●         Escala: Número total de toques sobre la pantalla táctil y tiempo en segundos requerido por cuidadores nuevos. ●         Métrica: Pruebas de usabilidad cronometradas con una muestra de 10 usuarios sin entrenamiento previo en la página web ●         Umbral de Fallo: Más de 5 toques de pantalla o más de 10.0 s por acción crítica de cuidado. ●         Umbral Objetivo: Máximo 2 toques de pantalla y tiempo de ejecución menor a 4.0 s desde la pantalla principal de la página web. |
| **Prioridad** | Media (Los padres que sostienen al bebé necesitan interactuar con la página web de forma ágil y con una sola mano). |

 

| **Identificador del Requerimiento** | **RNF06 (RNF-MAINT-001)** |
| --- | --- |
| **Nombre del Requerimiento** | Mantenibilidad_Escalabilidad_Portabilidad |
| **Características** | Atributo: Mantenibilidad y Portabilidad. Capacidad del firmware embebido para recibir actualizaciones remotas seguras (Over-The-Air / OTA) y compatibilidad de la Página Web en múltiples sistemas operativos. |
| **Descripción del requerimiento** | ●         Escala: Tiempo de reversión (rollback) ante fallo OTA y porcentaje de compatibilidad en Android 10.0+ e iOS 14.0+. ●         Métrica: Inyección de corte eléctrico durante actualización OTA y prueba de despliegue en terminales Android e iOS. ●         Umbral de Fallo: Bloqueo del microcontrolador (bricking) o incompatibilidad en alguno de los dos sistemas operativos móviles. ●         Umbral Objetivo: Recuperación automática en menos de 15.0 s por partición de respaldo y 100 % de paridad funcional en Android 10.0+ e iOS 14.0+. |
| **Prioridad** | Media (Garantiza la evolución del producto y el soporte de nuevos periféricos BLE sin recambio de hardware). |

 

## 3.1 Requisitos comunes de las interfaces

### 3.1.1 Interfaces de usuario

●        IU-01 (Página Web): La Página Web deberá desplegar un panel de control intuitivo con actualización reactiva inferior a 1.0 s para variables de temperatura (°C), humedad relativa (%), estado del colchón (presente/ausente/modo atención) y estado de la luminaria (0 % a 100 %).

●        IU-02 (Selector de Perfiles): La pantalla de bienvenida de la página web deberá permitir alternar entre perfiles familiares en no más de 2 toques de pantalla.

●        IU-03 (Botón Físico de Cuna): La unidad física de la cuna dispondrá de un botón capacitivo retroiluminado para conmutar localmente entre Modo Monitoreo y Modo Atención sin requerir el teléfono celular.

### 3.1.2 Interfaces de hardware

Para el correcto funcionamiento del ecosistema ciberfísico se dispondrá de las siguientes interfaces electrónicas:

●        IH-01 (Módulo de Peso): Interfaz con cuatro celdas de carga dispuestas en puente de Wheatstone acopladas a un convertidor analógico-digital de 24 bits (HX711) para sensar masa entre 0.50 kg y 35.00 kg con resolución de ± 10 g.

●        IH-02 (Sensores Ambientales): Conexión vía protocolo I2C con sensor digital de temperatura y humedad de alta precisión (margen de error menor a ± 2 °C y ± 2 % HR).

●        IH-03 (Luminaria PWM): Salida de modulación por ancho de pulsos de al menos 1.0 kHz para regulación suave de brillo de 0 % a 100 %.

●        IH-04 (Base de Biberón): Conexión inalámbrica BLE (perfil GATT) con base sensora dotada de sensor infrarrojo de temperatura sin contacto (MLX90614) y celda de pesaje de 0 g a 500 g (resolución de ± 1 g).

### 3.1.3 Interfaces de software

En cuanto a las integraciones de software externas se contará con lo siguiente:

●        IS-01 (Supabase Backend): Conexión HTTPS y WebSockets con el backend de Supabase para operaciones CRUD y eventos en tiempo real sobre la base de datos PostgreSQL, asegurada mediante políticas de seguridad por fila (RLS).

●        IS-02 (Google OAuth 2.0): Integración con la API de autenticación federada de Google para la validación de identidad del Usuario Principal.

●        IS-03 (Telegram Bot API): Comunicación vía API REST HTTPS con el servicio de mensajería de Telegram para el despacho de alertas y recepción de comandos del bot.

### 3.1.4 Interfaces de comunicaciones

●        IC-01 (Protocolo de Telemetría): Comunicación entre la cuna y la nube implementada mediante MQTT sobre TLS 1.3 con autenticación mutua de certificados para el envío de telemetría y recepción de comandos remotos.

●        IC-02 (Streaming de Audio): Protocolo WebRTC con cifrado DTLS/SRTP para la transmisión de voz unidireccional entre la página web y el altavoz de la cuna.

●        IC-03 (Protocolo BLE Local): Perfil GATT de Bluetooth Low Energy para la sincronización de telemetría entre el juguete de botones y la cuna.

## 3.2 Requisitos no funcionales

### 3.2.1 Requisitos de rendimiento

●        Garantizar que el despacho de alertas de emergencia extremo a extremo se complete en un umbral objetivo menor a 2,000 ms (en percentil P95) y nunca supere el umbral de fallo de 3,500 ms (RNF01).

●        Actualizar el panel de telemetría de la interfaz móvil en menos de 1,000 ms ante cambios de peso, temperatura o humedad (IU-01).

### 3.2.2 Seguridad

●        Garantizar el cifrado del 100 % del tráfico en tránsito bajo el protocolo TLS 1.3 con certificados válidos y autenticación mutua en MQTT (RNF02).

●        Asegurar el aislamiento estricto de datos entre diferentes familias mediante políticas de Row-Level Security (RLS) en PostgreSQL / Supabase.

●        Diferenciar rigurosamente los privilegios de acceso según el rol del usuario (Usuario Principal vs. Usuario Secundario) y atribuir cada acción mediante el Perfil Activo.

 

**3.2.3 Usabilidad**

●        Permitir ejecutar las acciones frecuentes de cuidado en un máximo de 2 toques y menos de 4.0 s (RNF05).

●        Garantizar que usuarios nuevos completen las tareas básicas sin entrenamiento (RNF07).

●        Asegurar que cada alerta sea comprensible e indique tipo de evento, valor y hora (RNF08).

●        Proveer controles táctiles y texto legibles para uso con una mano (RNF09).

●        Ofrecer un uso nocturno de baja perturbación (RNF10) y una satisfacción medible (RNF11).

### 3.2.4 Fiabilidad

●        Garantizar una retención mínima de 48 horas continuas de telemetría en la memoria flash SPI local ante caídas de red (con umbral de fallo mínimo de 12 horas), sincronizando los datos automáticamente al recuperar la conexión (RNF04).

●        Detectar cualquier pérdida de comunicación con la cuna en un máximo de 60 s mediante validación continua de señales periódicas de latido (heartbeat cada 15 s) para erradicar fallas silenciosas (RF20).

### 3.2.5 Disponibilidad

●        El backend en la nube deberá mantener una disponibilidad operativa mensual mayor o igual al 99.8 % (con umbral de fallo en 99.0 %) para la ingesta de telemetría, autenticación y despacho de notificaciones (RNF03).

●        La supervisión local del colchón deberá operar de manera continua las 24 horas los 7 días de la semana, respaldada por una batería Li-Ion con al menos 2.0 horas de autonomía ante cortes eléctricos.

### 3.2.6 Mantenibilidad

●        El sistema embebido deberá realizarse de manera modular con soporte para actualizaciones remotas de firmware (Over-The-Air / OTA) con partición de reversión automática en menos de 15.0 s ante fallos (RNF06).

●        La arquitectura deberá ser escalable para enlazar nuevos periféricos mediante Bluetooth Low Energy (BLE) y registrar sus métricas sin alterar el núcleo de seguridad.

### 3.2.7 Portabilidad

●        La Página Web deberá ser implementada de forma híbrida para funcionar con paridad total en dispositivos compatibles con los sistemas operativos Android 10.0+ e iOS 14.0+ (RNF06).

●        El canal de alertas de respaldo en Telegram será accesible desde cualquier plataforma móvil o de escritorio compatible con el cliente oficial de mensajería.