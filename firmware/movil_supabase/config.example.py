# Copia este archivo como config.py en la Pico W del móvil y rellena los valores.
# config.py NO se sube a Git: lleva la contraseña del Wi-Fi y el token del dispositivo.

WIFI_SSID = ""
WIFI_PASS = ""

SUPABASE_URL = "https://rjklzbarjuaxdhryklzw.supabase.co"
SUPABASE_ANON_KEY = ""        # clave pública sb_publishable_... (nunca la sb_secret_)

CUNA_ID = ""                  # id de la fila en public.cunas
TOKEN_DISPOSITIVO = ""        # mismo valor que public.cunas_credenciales.token_dispositivo (o devuelto por rotar_token_dispositivo_cuna)

CONSULTAR_CADA_MS = 1000      # cada cuánto se preguntan comandos pendientes
