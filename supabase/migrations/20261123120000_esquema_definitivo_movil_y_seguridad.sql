-- =============================================================================
-- Cuna Inteligente — Versión definitiva del esquema en Supabase (Móvil D15,
-- tokens de sincronización duraderos, historial RF33 y protección de columnas)
--
-- Proyecto objetivo: rjklzbarjuaxdhryklzw (verificar la URL del SQL Editor
-- antes de ejecutar).
--
-- Qué garantiza esta migración (100 % idempotente y segura sobre datos reales):
--   1. Versiona en Git las columnas de `public.cunas` (`estado`,
--      `ultimo_heartbeat`, `token_dispositivo`, `token_expira_en`) sin tocar
--      ni sobrescribir tokens existentes.
--   2. Versiona la tabla `public.comandos` y su índice de consulta para la Pico W.
--   3. Versiona las funciones RPC definitivas `dispositivo_comandos_pendientes`
--      y `dispositivo_confirmar_comando` (D15), manteniendo firmas exactas para
--      `firmware/movil_supabase/main.py` y registrando automáticamente el uso
--      confirmado en `public.uso_juguetes` (RF33).
--   4. Añade `rotar_token_dispositivo_cuna` (solo Usuario Principal) para emitir
--      un token criptográfico definitivo (`p_dias_vigencia = NULL`) o con
--      vigencia configurable en días.
--   5. Endurece permisos: oculta `cunas.token_dispositivo` de lecturas `SELECT`
--      del rol `authenticated`, fuerza RLS en `comandos` y `cunas` sin `USING (true)`
--      y confirma la publicación `supabase_realtime`.
-- =============================================================================

BEGIN;

-- -----------------------------------------------------------------------------
-- 1. Columnas definitivas en public.cunas (preservando valores existentes)
-- -----------------------------------------------------------------------------
ALTER TABLE public.cunas
    ADD COLUMN IF NOT EXISTS estado text NOT NULL DEFAULT 'desconectada';

ALTER TABLE public.cunas
    ADD COLUMN IF NOT EXISTS ultimo_heartbeat timestamptz;

ALTER TABLE public.cunas
    ADD COLUMN IF NOT EXISTS token_dispositivo text;

ALTER TABLE public.cunas
    ADD COLUMN IF NOT EXISTS token_expira_en timestamptz;

-- Si una cuna aún no tiene token de dispositivo, genera uno criptográfico definitivo
-- (token_expira_en = NULL significa vigencia indefinida hasta rotación manual).
UPDATE public.cunas
   SET token_dispositivo = encode(extensions.gen_random_bytes(24), 'hex')
 WHERE token_dispositivo IS NULL OR btrim(token_dispositivo) = '';

-- -----------------------------------------------------------------------------
-- 2. Tabla definitiva public.comandos e índice de sondeo
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
-- 3. Funciones RPC definitivas para el móvil Pico W (D15) y rotación de token
-- -----------------------------------------------------------------------------
-- NOTA: No alterar los nombres ni tipos de parámetros de dispositivo_comandos_pendientes
-- ni dispositivo_confirmar_comando; firmware/movil_supabase/main.py depende de ellos.
-- DROP FUNCTION IF EXISTS dentro de la transacción evita el error 42P13 si la versión
-- creada a mano tenía un tipo de retorno ligeramente distinto.

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
          FROM public.cunas c
         WHERE c.id = p_cuna
           AND c.token_dispositivo IS NOT NULL
           AND c.token_dispositivo = p_token
           AND (c.token_expira_en IS NULL OR c.token_expira_en > now())
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
          FROM public.cunas c
         WHERE c.id = p_cuna
           AND c.token_dispositivo IS NOT NULL
           AND c.token_dispositivo = p_token
           AND (c.token_expira_en IS NULL OR c.token_expira_en > now())
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
                COALESCE((v_cmd.payload ->> 'duracion_min')::integer, 5) * 60
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

-- Permite al Usuario Principal rotar el token de sincronización de una cuna
-- con vigencia indefinida (p_dias_vigencia = NULL) o por N días (1..3650).
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

    UPDATE public.cunas c
       SET token_dispositivo = v_nuevo_token,
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

-- Permisos estrictos de ejecución en funciones RPC
REVOKE ALL ON FUNCTION public.dispositivo_comandos_pendientes(uuid, text, text[]) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.dispositivo_comandos_pendientes(uuid, text, text[]) TO anon, authenticated;

REVOKE ALL ON FUNCTION public.dispositivo_confirmar_comando(uuid, text, uuid, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.dispositivo_confirmar_comando(uuid, text, uuid, text) TO anon, authenticated;

REVOKE ALL ON FUNCTION public.rotar_token_dispositivo_cuna(uuid, integer) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.rotar_token_dispositivo_cuna(uuid, integer) TO authenticated;

-- -----------------------------------------------------------------------------
-- 4. Publicación Realtime garantizada
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
END;
$$;

-- -----------------------------------------------------------------------------
-- 5. RLS forzado en public.comandos y protección de columna token_dispositivo
-- -----------------------------------------------------------------------------
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

-- Proteger cunas.token_dispositivo: el rol authenticated puede leer todas las
-- columnas públicas de cunas (incluyendo ultimo_heartbeat y estado), pero NUNCA
-- el secreto token_dispositivo. El backend (service_role) y las RPC SECURITY DEFINER
-- mantienen acceso completo.
ALTER TABLE public.cunas ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.cunas FORCE ROW LEVEL SECURITY;

REVOKE SELECT ON public.cunas FROM authenticated;
GRANT SELECT (
    id,
    family_id,
    nombre,
    estado_conexion,
    estado,
    modo_operacion,
    modo_atencion_expira_en,
    luz_encendida,
    luz_nivel,
    escala_masa,
    ultimo_heartbeat,
    token_expira_en,
    creado_en
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

COMMIT;

-- -----------------------------------------------------------------------------
-- Verificación rápida (opcional, solo lectura después de ejecutar el script):
-- -----------------------------------------------------------------------------
-- SELECT tablename FROM pg_publication_tables
--  WHERE pubname = 'supabase_realtime'
--    AND tablename IN ('comandos', 'cunas', 'telemetria', 'eventos_alertas')
--  ORDER BY 1;
--
-- SELECT proname, prosecdef, proconfig
--   FROM pg_proc
--  WHERE pronamespace = 'public'::regnamespace
--    AND proname IN (
--        'dispositivo_comandos_pendientes',
--        'dispositivo_confirmar_comando',
--        'rotar_token_dispositivo_cuna'
--    )
--  ORDER BY 1;
--
-- SELECT column_name, privilege_type
--   FROM information_schema.column_privileges
--  WHERE table_schema = 'public' AND table_name = 'cunas' AND grantee = 'authenticated'
--  ORDER BY 1;
