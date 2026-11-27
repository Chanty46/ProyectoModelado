-- =============================================================================
-- Cuna Inteligente — Migración definitiva de credenciales privadas de cuna
-- y Realtime 100 % compatible en Supabase
--
-- Proyecto objetivo: rjklzbarjuaxdhryklzw (verificar la URL del SQL Editor
-- antes de ejecutar).
--
-- Qué resuelve esta migración (100 % idempotente; segura tanto si ya corriste
-- 20261123120000 como si partes desde 20261119120000):
--   1. Crea la tabla privada 1-a-1 `public.cunas_credenciales` con RLS forzado
--      y CERO permisos para `anon`/`authenticated`, fuera de `supabase_realtime`.
--   2. Migra sin pérdida el `token_dispositivo` actual de `public.cunas` hacia
--      `public.cunas_credenciales` (tu "Cuna Principal" conserva su mismo token
--      para no tener que cambiar `config.py` en la Pico W) y limpia
--      `public.cunas.token_dispositivo = NULL`.
--   3. Restaura `GRANT SELECT ON public.cunas TO authenticated` para que
--      Supabase Realtime (`postgres_changes`) entregue siempre los heartbeats
--      y cambios de estado en vivo sin exponer ningún secreto, y limita `UPDATE`
--      en `public.cunas` únicamente a columnas editables.
--   4. Actualiza las RPC `dispositivo_comandos_pendientes`,
--      `dispositivo_confirmar_comando` (con cast seguro de `duracion_min` para
--      RF33) y `rotar_token_dispositivo_cuna` para operar sobre
--      `public.cunas_credenciales` conservando firmas exactas para la Pico W.
--   5. Endurecimiento de auditoría en vivo (`rjklzbarjuaxdhryklzw`): elimina
--      la política permisiva `invitaciones_update_backend` (`USING (true)`),
--      borra la función huérfana `puente_movil_web()`, revoca `EXECUTE` en
--      funciones trigger, aplica mínimo privilegio en todas las tablas para
--      `authenticated` y optimiza las políticas RLS con `(SELECT auth.uid())`.
-- =============================================================================

BEGIN;

-- -----------------------------------------------------------------------------
-- 1. Asegurar columnas esperadas en public.cunas
-- -----------------------------------------------------------------------------
ALTER TABLE public.cunas
    ADD COLUMN IF NOT EXISTS estado text NOT NULL DEFAULT 'desconectada';

ALTER TABLE public.cunas
    ADD COLUMN IF NOT EXISTS ultimo_heartbeat timestamptz;

ALTER TABLE public.cunas
    ADD COLUMN IF NOT EXISTS token_dispositivo text;

ALTER TABLE public.cunas
    ADD COLUMN IF NOT EXISTS token_expira_en timestamptz;

-- -----------------------------------------------------------------------------
-- 2. Tabla privada 1-a-1 public.cunas_credenciales (fuera de Realtime y sin acceso cliente)
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.cunas_credenciales (
    cuna_id uuid PRIMARY KEY REFERENCES public.cunas(id) ON DELETE CASCADE,
    token_dispositivo text NOT NULL CHECK (char_length(btrim(token_dispositivo)) >= 16),
    token_expira_en timestamptz,
    actualizado_en timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE public.cunas_credenciales ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.cunas_credenciales FORCE ROW LEVEL SECURITY;

REVOKE ALL ON public.cunas_credenciales FROM PUBLIC, anon, authenticated;

DO $$
DECLARE
    pol record;
BEGIN
    FOR pol IN
        SELECT policyname FROM pg_policies
         WHERE schemaname = 'public' AND tablename = 'cunas_credenciales'
    LOOP
        EXECUTE format('DROP POLICY %I ON public.cunas_credenciales', pol.policyname);
    END LOOP;
END;
$$;

-- Migrar tokens existentes desde public.cunas preservando exactamente su valor
INSERT INTO public.cunas_credenciales (cuna_id, token_dispositivo, token_expira_en)
SELECT c.id, c.token_dispositivo, c.token_expira_en
  FROM public.cunas c
 WHERE c.token_dispositivo IS NOT NULL
   AND char_length(btrim(c.token_dispositivo)) >= 16
ON CONFLICT (cuna_id) DO NOTHING;

-- Generar token permanente (token_expira_en = NULL) para cualquier cuna sin credencial
INSERT INTO public.cunas_credenciales (cuna_id, token_dispositivo, token_expira_en)
SELECT c.id, encode(extensions.gen_random_bytes(24), 'hex'), c.token_expira_en
  FROM public.cunas c
 WHERE NOT EXISTS (
     SELECT 1 FROM public.cunas_credenciales cc WHERE cc.cuna_id = c.id
 )
ON CONFLICT (cuna_id) DO NOTHING;

-- Eliminar el secreto en texto claro de public.cunas para que nunca salga por SELECT ni Realtime
UPDATE public.cunas
   SET token_dispositivo = NULL
 WHERE token_dispositivo IS NOT NULL;

-- Trigger automático: toda cuna nueva creada por el backend recibe su credencial privada
CREATE OR REPLACE FUNCTION public.crear_credencial_cuna()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
BEGIN
    INSERT INTO public.cunas_credenciales (cuna_id, token_dispositivo, token_expira_en)
    VALUES (NEW.id, encode(extensions.gen_random_bytes(24), 'hex'), NEW.token_expira_en)
    ON CONFLICT (cuna_id) DO NOTHING;
    RETURN NEW;
END;
$$;

REVOKE ALL ON FUNCTION public.crear_credencial_cuna() FROM PUBLIC, anon, authenticated;

DROP TRIGGER IF EXISTS trg_cunas_crear_credencial ON public.cunas;
CREATE TRIGGER trg_cunas_crear_credencial
AFTER INSERT ON public.cunas
FOR EACH ROW
EXECUTE FUNCTION public.crear_credencial_cuna();

-- -----------------------------------------------------------------------------
-- 3. Asegurar tabla public.comandos e índice
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.comandos (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    cuna_id uuid NOT NULL REFERENCES public.cunas(id) ON DELETE CASCADE,
    tipo text NOT NULL CHECK (char_length(btrim(tipo)) BETWEEN 2 AND 50),
    payload jsonb NOT NULL DEFAULT '{}'::jsonb,
    estado text NOT NULL DEFAULT 'pendiente'
        CHECK (estado IN ('pendiente', 'confirmado', 'error')),
    creado_en timestamptz NOT NULL DEFAULT now(),
    confirmado_en timestamptz
);

CREATE INDEX IF NOT EXISTS idx_comandos_cuna_estado_creado
    ON public.comandos (cuna_id, estado, creado_en ASC);

-- -----------------------------------------------------------------------------
-- 4. Funciones RPC definitivas sobre public.cunas_credenciales (firmas intactas)
-- -----------------------------------------------------------------------------
DROP FUNCTION IF EXISTS public.dispositivo_comandos_pendientes(uuid, text, text[]);
CREATE OR REPLACE FUNCTION public.dispositivo_comandos_pendientes(
    p_cuna uuid,
    p_token text,
    p_tipos text[]
)
RETURNS TABLE (
    id uuid,
    tipo text,
    payload jsonb
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
DECLARE
    v_token_valido boolean;
BEGIN
    IF p_cuna IS NULL OR p_token IS NULL OR btrim(p_token) = '' THEN
        RETURN;
    END IF;

    SELECT EXISTS (
        SELECT 1
          FROM public.cunas_credenciales cc
         WHERE cc.cuna_id = p_cuna
           AND cc.token_dispositivo = p_token
           AND (cc.token_expira_en IS NULL OR cc.token_expira_en > now())
    )
    INTO v_token_valido;

    IF NOT v_token_valido THEN
        RETURN;
    END IF;

    UPDATE public.cunas
       SET ultimo_heartbeat = now(),
           estado = 'en_linea',
           estado_conexion = 'en_linea'
     WHERE public.cunas.id = p_cuna;

    RETURN QUERY
    SELECT cmd.id, cmd.tipo, cmd.payload
      FROM public.comandos cmd
     WHERE cmd.cuna_id = p_cuna
       AND cmd.estado = 'pendiente'
       AND (p_tipos IS NULL OR cmd.tipo = ANY(p_tipos))
     ORDER BY cmd.creado_en ASC
     LIMIT 20;
END;
$$;

DROP FUNCTION IF EXISTS public.dispositivo_confirmar_comando(uuid, text, uuid, text);
CREATE OR REPLACE FUNCTION public.dispositivo_confirmar_comando(
    p_cuna uuid,
    p_token text,
    p_id uuid,
    p_estado text
)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
DECLARE
    v_token_valido boolean;
    v_cmd record;
    v_duracion_s integer;
BEGIN
    IF p_cuna IS NULL
       OR p_token IS NULL
       OR btrim(p_token) = ''
       OR p_id IS NULL
       OR p_estado NOT IN ('confirmado', 'error') THEN
        RETURN false;
    END IF;

    SELECT EXISTS (
        SELECT 1
          FROM public.cunas_credenciales cc
         WHERE cc.cuna_id = p_cuna
           AND cc.token_dispositivo = p_token
           AND (cc.token_expira_en IS NULL OR cc.token_expira_en > now())
    )
    INTO v_token_valido;

    IF NOT v_token_valido THEN
        RETURN false;
    END IF;

    UPDATE public.cunas
       SET ultimo_heartbeat = now(),
           estado = 'en_linea',
           estado_conexion = 'en_linea'
     WHERE public.cunas.id = p_cuna;

    UPDATE public.comandos
       SET estado = p_estado,
           confirmado_en = now()
     WHERE public.comandos.id = p_id
       AND public.comandos.cuna_id = p_cuna
       AND public.comandos.estado = 'pendiente'
    RETURNING public.comandos.tipo, public.comandos.payload, public.comandos.creado_en
      INTO v_cmd;

    IF NOT FOUND THEN
        RETURN false;
    END IF;

    -- RF33: Registrar automáticamente en uso_juguetes cada encendido confirmado del móvil
    IF v_cmd.tipo = 'movil_on' AND p_estado = 'confirmado' THEN
        v_duracion_s := LEAST(
            86400,
            GREATEST(
                60,
                CASE
                    WHEN (v_cmd.payload ->> 'duracion_min') ~ '^[0-9]+$'
                        THEN (v_cmd.payload ->> 'duracion_min')::integer
                    ELSE 5
                END * 60
            )
        );

        INSERT INTO public.uso_juguetes (
            cuna_id,
            tipo,
            perfil_id,
            inicio,
            duracion,
            aciertos,
            patron,
            velocidad
        )
        VALUES (
            p_cuna,
            'movil',
            NULL,
            COALESCE(v_cmd.creado_en, now()),
            v_duracion_s,
            NULL,
            v_cmd.payload ->> 'patron',
            CASE
                WHEN v_cmd.payload ->> 'velocidad' IN ('lenta', 'media', 'rapida')
                    THEN v_cmd.payload ->> 'velocidad'
                ELSE NULL
            END
        );
    END IF;

    RETURN true;
END;
$$;

CREATE OR REPLACE FUNCTION public.rotar_token_dispositivo_cuna(
    p_cuna uuid,
    p_dias_vigencia integer DEFAULT NULL
)
RETURNS TABLE (
    token_dispositivo text,
    token_expira_en timestamptz
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
DECLARE
    v_family_id uuid;
    v_nuevo_token text;
    v_expira_en timestamptz;
BEGIN
    IF p_cuna IS NULL OR NOT public.es_principal_cuna(p_cuna) THEN
        RAISE EXCEPTION 'Solo el Usuario Principal de la familia puede rotar el token de la cuna.'
            USING ERRCODE = '42501';
    END IF;

    IF p_dias_vigencia IS NOT NULL AND (p_dias_vigencia < 1 OR p_dias_vigencia > 3650) THEN
        RAISE EXCEPTION 'p_dias_vigencia debe ser NULL (permanente) o estar entre 1 y 3650 días.'
            USING ERRCODE = '22023';
    END IF;

    SELECT c.family_id INTO v_family_id
      FROM public.cunas c
     WHERE c.id = p_cuna;

    v_nuevo_token := encode(extensions.gen_random_bytes(24), 'hex');
    v_expira_en := CASE
        WHEN p_dias_vigencia IS NULL THEN NULL
        ELSE now() + make_interval(days => p_dias_vigencia)
    END;

    INSERT INTO public.cunas_credenciales (cuna_id, token_dispositivo, token_expira_en, actualizado_en)
    VALUES (p_cuna, v_nuevo_token, v_expira_en, now())
    ON CONFLICT (cuna_id) DO UPDATE
       SET token_dispositivo = EXCLUDED.token_dispositivo,
           token_expira_en = EXCLUDED.token_expira_en,
           actualizado_en = EXCLUDED.actualizado_en;

    UPDATE public.cunas c
       SET token_dispositivo = NULL,
           token_expira_en = v_expira_en
     WHERE c.id = p_cuna;

    INSERT INTO public.bitacora_seguridad (family_id, usuario_id, evento, detalle)
    VALUES (
        v_family_id,
        auth.uid(),
        'cuna_token_dispositivo_rotado',
        jsonb_build_object('cuna_id', p_cuna, 'dias_vigencia', p_dias_vigencia)
    );

    RETURN QUERY SELECT v_nuevo_token, v_expira_en;
END;
$$;

REVOKE ALL ON FUNCTION public.dispositivo_comandos_pendientes(uuid, text, text[]) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.dispositivo_comandos_pendientes(uuid, text, text[]) TO anon, authenticated;

REVOKE ALL ON FUNCTION public.dispositivo_confirmar_comando(uuid, text, uuid, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.dispositivo_confirmar_comando(uuid, text, uuid, text) TO anon, authenticated;

REVOKE ALL ON FUNCTION public.rotar_token_dispositivo_cuna(uuid, integer) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.rotar_token_dispositivo_cuna(uuid, integer) TO authenticated;

-- -----------------------------------------------------------------------------
-- 5. Permisos Realtime-safe en public.cunas y RLS estricto en public.comandos
-- -----------------------------------------------------------------------------
ALTER TABLE public.cunas ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.cunas FORCE ROW LEVEL SECURITY;

REVOKE ALL ON public.cunas FROM PUBLIC, anon, authenticated;
-- GRANT SELECT completo a nivel de tabla para garantizar compatibilidad con Supabase Realtime;
-- el secreto ya no reside en public.cunas sino en public.cunas_credenciales.
GRANT SELECT ON public.cunas TO authenticated;
GRANT UPDATE (
    nombre,
    modo_operacion,
    modo_atencion_expira_en,
    luz_encendida,
    luz_nivel,
    escala_masa
) ON public.cunas TO authenticated;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_policies
         WHERE schemaname = 'public'
           AND tablename = 'cunas'
           AND policyname = 'cunas_select_miembro'
    ) THEN
        CREATE POLICY cunas_select_miembro ON public.cunas
            FOR SELECT TO authenticated
            USING (public.es_miembro_familia(family_id));
    END IF;
END;
$$;

ALTER TABLE public.comandos ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.comandos FORCE ROW LEVEL SECURITY;

REVOKE ALL ON public.comandos FROM PUBLIC, anon, authenticated;
GRANT SELECT, INSERT ON public.comandos TO authenticated;

DO $$
DECLARE
    pol record;
BEGIN
    FOR pol IN
        SELECT policyname FROM pg_policies
         WHERE schemaname = 'public' AND tablename = 'comandos'
    LOOP
        EXECUTE format('DROP POLICY %I ON public.comandos', pol.policyname);
    END LOOP;
END;
$$;

CREATE POLICY comandos_select_miembro ON public.comandos
    FOR SELECT TO authenticated
    USING (cuna_id IS NOT NULL AND public.es_miembro_cuna(cuna_id));

CREATE POLICY comandos_insert_miembro ON public.comandos
    FOR INSERT TO authenticated
    WITH CHECK (
        cuna_id IS NOT NULL
        AND public.es_miembro_cuna(cuna_id)
        AND estado = 'pendiente'
        AND confirmado_en IS NULL
        AND (
            (tipo = 'movil_off' AND payload = '{}'::jsonb)
            OR (
                tipo = 'movil_on'
                AND jsonb_typeof(payload) = 'object'
                AND payload ?& ARRAY['patron', 'velocidad', 'duracion_min']
                AND (SELECT count(*) FROM jsonb_object_keys(payload)) = 3
                AND payload ->> 'patron' IN ('secuencia', 'parpadeo', 'columnas', 'filas', 'espiral')
                AND payload ->> 'velocidad' IN ('lenta', 'media', 'rapida')
                AND CASE
                        WHEN jsonb_typeof(payload -> 'duracion_min') = 'number' THEN
                            (payload ->> 'duracion_min')::numeric BETWEEN 1 AND 30
                            AND (payload ->> 'duracion_min')::numeric = trunc((payload ->> 'duracion_min')::numeric)
                        ELSE false
                    END
            )
        )
    );

-- -----------------------------------------------------------------------------
-- 6. Publicación Realtime (cunas_credenciales NUNCA se publica)
-- -----------------------------------------------------------------------------
DO $$
DECLARE
    t text;
BEGIN
    FOREACH t IN ARRAY ARRAY['comandos', 'cunas', 'telemetria', 'eventos_alertas']
    LOOP
        IF NOT EXISTS (
            SELECT 1 FROM pg_publication_tables
             WHERE pubname = 'supabase_realtime'
               AND schemaname = 'public'
               AND tablename = t
        ) THEN
            EXECUTE format('ALTER PUBLICATION supabase_realtime ADD TABLE public.%I', t);
        END IF;
    END LOOP;

    IF EXISTS (
        SELECT 1 FROM pg_publication_tables
         WHERE pubname = 'supabase_realtime'
           AND schemaname = 'public'
           AND tablename = 'cunas_credenciales'
    ) THEN
        ALTER PUBLICATION supabase_realtime DROP TABLE public.cunas_credenciales;
    END IF;
END;
$$;

-- -----------------------------------------------------------------------------
-- 7. Endurecimiento de auditoría en vivo: cero USING(true), mínimo privilegio,
--    limpieza de función huérfana y optimización (SELECT auth.uid())
-- -----------------------------------------------------------------------------
-- Eliminar políticas heredadas inseguras en public.invitaciones (el backend usa clave secreta)
DROP POLICY IF EXISTS invitaciones_update_backend ON public.invitaciones;
DROP POLICY IF EXISTS invitaciones_insert_backend ON public.invitaciones;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_policies
         WHERE schemaname = 'public'
           AND tablename = 'invitaciones'
           AND policyname = 'invitaciones_select_principal'
    ) THEN
        CREATE POLICY invitaciones_select_principal ON public.invitaciones
            FOR SELECT TO authenticated
            USING (public.es_principal_familia(family_id));
    END IF;
END;
$$;

-- Restringir privilegios de tabla para authenticated al mínimo exacto del esquema M1
-- (elimina TRUNCATE, TRIGGER, REFERENCES y escrituras no autorizadas heredadas por defecto)
REVOKE ALL ON
    public.familias,
    public.usuarios,
    public.perfiles_cuidador,
    public.invitaciones,
    public.telegram_vinculos,
    public.dispositivos,
    public.umbrales,
    public.canciones,
    public.bebes,
    public.telemetria,
    public.sesiones_sueno,
    public.sesiones_alimentacion,
    public.eventos_alertas,
    public.recordatorios,
    public.uso_juguetes,
    public.bitacora,
    public.bitacora_seguridad
FROM PUBLIC, anon, authenticated;

ALTER DEFAULT PRIVILEGES IN SCHEMA public
    REVOKE ALL ON TABLES FROM PUBLIC, anon, authenticated;

GRANT SELECT, UPDATE ON public.familias TO authenticated;
GRANT SELECT, DELETE ON public.usuarios TO authenticated;
GRANT SELECT, INSERT, UPDATE ON public.perfiles_cuidador TO authenticated;
GRANT SELECT ON public.invitaciones TO authenticated;
GRANT SELECT, DELETE ON public.telegram_vinculos TO authenticated;
GRANT SELECT ON public.dispositivos TO authenticated;
GRANT SELECT, INSERT, UPDATE ON public.umbrales TO authenticated;
GRANT SELECT ON public.canciones TO authenticated;
GRANT SELECT, INSERT, UPDATE, DELETE ON public.bebes TO authenticated;
GRANT SELECT ON public.telemetria TO authenticated;
GRANT SELECT ON public.sesiones_sueno TO authenticated;
GRANT SELECT, INSERT, UPDATE ON public.sesiones_alimentacion TO authenticated;
GRANT SELECT, UPDATE ON public.eventos_alertas TO authenticated;
GRANT SELECT, INSERT, UPDATE ON public.recordatorios TO authenticated;
GRANT SELECT ON public.uso_juguetes TO authenticated;
GRANT SELECT, INSERT ON public.bitacora TO authenticated;
GRANT SELECT ON public.bitacora_seguridad TO authenticated;

-- Eliminar función puente legada sin triggers activos y revocar EXECUTE en funciones trigger
DROP FUNCTION IF EXISTS public.puente_movil_web();

DO $$
BEGIN
    IF to_regprocedure('public.calcular_sesion_alimentacion()') IS NOT NULL THEN
        REVOKE ALL ON FUNCTION public.calcular_sesion_alimentacion() FROM PUBLIC, anon, authenticated;
    END IF;
    IF to_regprocedure('public.verificar_limite_perfiles_cuidador()') IS NOT NULL THEN
        REVOKE ALL ON FUNCTION public.verificar_limite_perfiles_cuidador() FROM PUBLIC, anon, authenticated;
    END IF;
END;
$$;

-- Optimización auth_rls_initplan: evaluar auth.uid() una sola vez por consulta
DROP POLICY IF EXISTS usuarios_delete_principal ON public.usuarios;
CREATE POLICY usuarios_delete_principal ON public.usuarios
    FOR DELETE TO authenticated
    USING (public.es_principal_familia(family_id) AND id <> (SELECT auth.uid()) AND rol = 'secundario');

DROP POLICY IF EXISTS telegram_select_propio ON public.telegram_vinculos;
CREATE POLICY telegram_select_propio ON public.telegram_vinculos
    FOR SELECT TO authenticated
    USING (usuario_id = (SELECT auth.uid()) AND public.obtener_mi_family_id() IS NOT NULL);

DROP POLICY IF EXISTS telegram_delete_propio ON public.telegram_vinculos;
CREATE POLICY telegram_delete_propio ON public.telegram_vinculos
    FOR DELETE TO authenticated
    USING (usuario_id = (SELECT auth.uid()) AND public.obtener_mi_family_id() IS NOT NULL);

DROP POLICY IF EXISTS canciones_select_miembro ON public.canciones;
CREATE POLICY canciones_select_miembro ON public.canciones
    FOR SELECT TO authenticated
    USING ((SELECT auth.uid()) IS NOT NULL AND public.obtener_mi_family_id() IS NOT NULL);

COMMIT;

-- -----------------------------------------------------------------------------
-- Verificación rápida (opcional, solo lectura después de ejecutar el script):
-- -----------------------------------------------------------------------------
-- SELECT count(*) AS credenciales_creadas FROM public.cunas_credenciales;
-- SELECT count(*) AS tokens_en_cunas_debe_ser_cero FROM public.cunas WHERE token_dispositivo IS NOT NULL;
-- SELECT count(*) AS politicas_using_true_debe_ser_cero FROM pg_policies
--  WHERE schemaname = 'public' AND (lower(btrim(coalesce(qual,''))) = 'true' OR lower(btrim(coalesce(with_check,''))) = 'true');
-- SELECT tablename FROM pg_publication_tables
--  WHERE pubname = 'supabase_realtime'
--    AND tablename IN ('comandos', 'cunas', 'telemetria', 'eventos_alertas', 'cunas_credenciales')
--  ORDER BY 1;
-- SELECT grantee, privilege_type FROM information_schema.role_table_grants
--  WHERE table_schema = 'public' AND table_name = 'cunas_credenciales';
