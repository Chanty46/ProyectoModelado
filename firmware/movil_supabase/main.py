# =====================================================================
# Cuna Inteligente · Móvil colgante (Pico W, MicroPython)
# Se conecta al Wi-Fi, pregunta a Supabase por comandos del móvil
# y ejecuta el patrón de LEDs con la velocidad y duración pedidas (RF13).
#
# Comandos que entiende (tabla "comandos"):
#   tipo = "movil_on"  payload = {"patron": "secuencia", "velocidad": "media", "duracion_min": 5}
#   tipo = "movil_off" payload = {}
# Patrones: secuencia, parpadeo, columnas, filas, espiral
# Velocidades: lenta, media, rapida      Duración: 1 a 30 minutos
#
# Los dos núcleos del RP2040 se reparten el trabajo:
#   - Núcleo 0 (este programa): Wi-Fi y consultas HTTPS a Supabase.
#     Cada consulta bloquea entre 0.5 y 2 s (abre una conexión TLS nueva).
#   - Núcleo 1 (hilo _thread): solo la animación de los LEDs, con tiempos
#     exactos, así la red no la frena y las tres velocidades se notan.
# =====================================================================
from machine import Pin
import gc, network, time, json, _thread
import urequests as requests
import config

# ---------- LEDs (número de LED en la matriz 3x3 -> pin GP) ----------
#  1 2 3      columnas: rojo, amarillo, azul
#  4 5 6
#  7 8 9
PINES = {1: 15, 2: 12, 3: 16,
         4: 14, 5: 11, 6: 17,
         7: 13, 8: 10, 9: 21}
leds = {n: Pin(gp, Pin.OUT) for n, gp in PINES.items()}
led_placa = Pin("LED", Pin.OUT)   # LED verde de la Pico: parpadea mientras busca Wi-Fi


def apagar_todos():
    for led in leds.values():
        led.off()


def prender(numeros):
    for n, led in leds.items():
        led.value(1 if n in numeros else 0)


# ---------- Patrones: cada uno es una lista de "cuadros" ----------
PATRONES = {
    "secuencia": [[n] for n in range(1, 10)],
    "parpadeo":  [[1, 2, 3, 4, 5, 6, 7, 8, 9], []],
    "columnas":  [[1, 4, 7], [2, 5, 8], [3, 6, 9]],          # rojo, amarillo, azul
    "filas":     [[1, 2, 3], [4, 5, 6], [7, 8, 9]],
    "espiral":   [[1], [2], [3], [6], [9], [8], [7], [4], [5]],
}
VELOCIDADES_MS = {"lenta": 600, "media": 300, "rapida": 120}

# ---------- Estado compartido entre núcleos ----------
# El núcleo 0 lo cambia (iniciar / detener) y el núcleo 1 lo lee.
# Solo el núcleo 1 toca los pines de los LEDs mientras corre la animación.
candado = _thread.allocate_lock()
activo = False
cuadros = PATRONES["secuencia"]
intervalo = 300
fin_ms = 0
reiniciar = False      # True = el núcleo 1 debe empezar el patrón desde el primer cuadro


def iniciar(patron, velocidad, duracion_min):
    global activo, cuadros, intervalo, fin_ms, reiniciar
    if patron not in PATRONES:
        raise ValueError("Patrón desconocido: " + str(patron))
    duracion_min = max(1, min(30, int(duracion_min)))     # RF13: de 1 a 30 min
    nuevos_cuadros = PATRONES[patron]
    nuevo_intervalo = VELOCIDADES_MS.get(velocidad, 300)
    nuevo_fin = time.ticks_add(time.ticks_ms(), duracion_min * 60 * 1000)
    candado.acquire()
    try:
        cuadros = nuevos_cuadros
        intervalo = nuevo_intervalo
        fin_ms = nuevo_fin
        activo = True
        reiniciar = True
    finally:
        candado.release()
    print("Móvil ENCENDIDO:", patron, velocidad, duracion_min, "min")


def detener():
    global activo
    candado.acquire()
    try:
        activo = False
    finally:
        candado.release()
    print("Móvil APAGADO")


def bucle_animacion():
    """Núcleo 1: anima los LEDs a ritmo fijo, sin depender de la red."""
    global activo, reiniciar
    paso = 0
    proximo_ms = time.ticks_ms()
    encendidos = False
    while True:
        candado.acquire()
        try:
            act = activo
            cds = cuadros
            ivl = intervalo
            fin = fin_ms
            empezar = reiniciar
            reiniciar = False
        finally:
            candado.release()

        ahora = time.ticks_ms()

        if empezar:
            paso = 0
            proximo_ms = ahora                      # primer cuadro de inmediato

        if act and time.ticks_diff(ahora, fin) >= 0:  # se acabó el tiempo
            candado.acquire()
            try:
                if not reiniciar:                   # no pisar un comando que acaba de llegar
                    activo = False
            finally:
                candado.release()
            act = False

        if not act:
            if encendidos:
                apagar_todos()
                encendidos = False
            time.sleep_ms(5)
            continue

        if time.ticks_diff(ahora, proximo_ms) >= 0:
            prender(cds[paso % len(cds)])
            encendidos = True
            paso += 1
            # Ritmo fijo: el siguiente cuadro se programa desde el anterior, no desde "ahora".
            proximo_ms = time.ticks_add(proximo_ms, ivl)
            if time.ticks_diff(ahora, proximo_ms) >= 0:   # si se atrasó, retomar sin ráfagas
                proximo_ms = time.ticks_add(ahora, ivl)
        time.sleep_ms(2)


# ---------- Wi-Fi ----------
wlan = network.WLAN(network.STA_IF)


def conectar_wifi():
    wlan.active(True)
    if wlan.isconnected():
        return
    print("Conectando a", config.WIFI_SSID, "...")
    wlan.connect(config.WIFI_SSID, config.WIFI_PASS)
    for _ in range(40):                    # hasta 20 s
        if wlan.isconnected():
            break
        led_placa.toggle()
        time.sleep(0.5)
    led_placa.value(1 if wlan.isconnected() else 0)
    print("Wi-Fi conectado:" if wlan.isconnected() else "No se pudo conectar al Wi-Fi",
          wlan.ifconfig()[0] if wlan.isconnected() else "")


# ---------- Supabase (funciones RPC) ----------
HEADERS = {
    "apikey": config.SUPABASE_ANON_KEY,
    "Authorization": "Bearer " + config.SUPABASE_ANON_KEY,
    "Content-Type": "application/json",
}


def rpc(nombre, datos):
    url = config.SUPABASE_URL + "/rest/v1/rpc/" + nombre
    body = json.dumps(datos)
    try:
        r = requests.post(url, headers=HEADERS, data=body, timeout=5)
    except TypeError:
        r = requests.post(url, headers=HEADERS, data=body)
    try:
        if r.status_code >= 300:
            print("Error de Supabase", r.status_code, r.text)
            return None
        return r.json() if r.text else None
    finally:
        r.close()


def revisar_comandos():
    pendientes = rpc("dispositivo_comandos_pendientes", {
        "p_cuna": config.CUNA_ID,
        "p_token": config.TOKEN_DISPOSITIVO,
        "p_tipos": ["movil_on", "movil_off"],
    })
    for cmd in pendientes or []:
        estado = "confirmado"
        try:
            datos = cmd.get("payload") or {}
            if cmd["tipo"] == "movil_on":
                iniciar(datos.get("patron", "secuencia"),
                        datos.get("velocidad", "media"),
                        datos.get("duracion_min", 5))
            else:
                detener()
        except Exception as e:
            print("Comando inválido:", e)
            estado = "error"
        rpc("dispositivo_confirmar_comando", {
            "p_cuna": config.CUNA_ID,
            "p_token": config.TOKEN_DISPOSITIVO,
            "p_id": cmd["id"],
            "p_estado": estado,
        })


# ---------- Programa principal (núcleo 0) ----------
def main():
    apagar_todos()
    _thread.start_new_thread(bucle_animacion, ())   # la animación corre en el núcleo 1
    conectar_wifi()
    ultima_consulta = time.ticks_add(time.ticks_ms(), -config.CONSULTAR_CADA_MS)

    while True:
        ahora = time.ticks_ms()
        if time.ticks_diff(ahora, ultima_consulta) >= config.CONSULTAR_CADA_MS:
            ultima_consulta = ahora
            try:
                if not wlan.isconnected():
                    conectar_wifi()
                else:
                    revisar_comandos()
            except Exception as e:
                print("Error de red:", e)
            gc.collect()
        time.sleep_ms(20)


if __name__ == "__main__":
    main()
