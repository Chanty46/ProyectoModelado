# SECURITY — reglas y checklist

Proyecto de curso, red local, no público. Aun así se aplican las medidas baratas que evitan los errores graves.

## Qué es público y qué es privado
| Dato | Público | Dónde vive |
|---|---|---|
| Project URL, project ref, `sb_publishable_` | Sí (seguro con RLS) | `frontend/.env`, `.env.example` |
| `sb_secret_` (salta RLS) | **No** | Solo `backend/.env` |
| Google Client ID | Sí | Panel de Supabase |
| Google Client Secret | **No** | Panel de Supabase + gestor de contraseñas |
| Token del bot de Telegram | **No** | `backend/.env` |
| Wi-Fi, `DEVICE_TOKEN` | **No** | `secrets.py` de cada Pico |
| Contraseña de la BD | **No** | Gestor de contraseñas |
Si algo privado se filtra: rotarlo de inmediato (Supabase → nueva secret key; BotFather `/revoke`; Google → nuevo secreto).

## Checklist por hito
- [ ] No hay secretos en el código ni en el historial (`git diff`, buscar `sb_secret`, `AAH`, contraseñas).
- [ ] Tablas nuevas con RLS y políticas por membresía; sin `using (true)`; sin acceso `anon`.
- [ ] Prueba de aislamiento entre familias.
- [ ] Endpoints: validan JWT, rol y membresía; ignoran `family_id` del cliente; devuelven 403 y registran.
- [ ] Entradas validadas (tipo, rango, tamaño); errores sin detalles internos.
- [ ] Logs sin tokens ni datos del bebé.
- [ ] Códigos/tokens: aleatorios criptográficos, hasheados, con caducidad, un uso, intentos limitados.
- [ ] Servidor TCP solo en la red local; autenticación de `hello`; límite de línea.
- [ ] Dependencias nuevas justificadas y con versión fija.

## Revisión final (M10)
Autenticación · autorización · base de datos/RLS · backend · frontend · firmware (secretos fuera del repo) · comunicación TCP · Telegram · secretos y `.gitignore` · plan de demostración.

## Trabajo con IA
No pegar secretos en chats de IA. Cursor ignora `.env*` y `secrets.py` vía `.cursorignore`. MCP de Supabase solo lectura y solo este proyecto; con datos de prueba.
