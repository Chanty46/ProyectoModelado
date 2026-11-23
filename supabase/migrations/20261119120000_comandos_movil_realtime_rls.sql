-- =============================================================================
-- Comandos del móvil colgante desde la web + Realtime + RLS
--
-- Contexto: la Pico W del móvil consulta cada segundo las funciones
-- public.dispositivo_comandos_pendientes y public.dispositivo_confirmar_comando
-- (creadas a mano en Supabase; no se modifican aquí). Esas funciones leen las filas
-- 'pendiente' de public.comandos, las marcan 'confirmado'/'error' y actualizan
-- public.cunas.ultimo_heartbeat / public.cunas.estado.
--
-- Esta migración:
--   1. Agrega comandos y cunas a la publicación supabase_realtime (si faltan).
--   2. Activa RLS en comandos y deja solo dos políticas:
--        - leer comandos de las cunas de mi familia (Realtime la necesita);
--        - insertar movil_on / movil_off válidos para las cunas de mi familia.
--      Los usuarios NO pueden actualizar ni borrar comandos: solo la Pico (vía RPC)
--      los confirma.
--   3. Se asegura de que exista la política de lectura de cunas por miembro.
--
-- Es idempotente: se puede ejecutar más de una vez.
-- No cambia nombres de columnas, tipos ni las funciones dispositivo_*.
-- =============================================================================

BEGIN;

-- -----------------------------------------------------------------------------
-- 1. Publicación Realtime
-- -----------------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_publication_tables
         WHERE pubname = 'supabase_realtime' AND schemaname = 'public' AND tablename = 'comandos'
    ) THEN
        ALTER PUBLICATION supabase_realtime ADD TABLE public.comandos;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_publication_tables
         WHERE pubname = 'supabase_realtime' AND schemaname = 'public' AND tablename = 'cunas'
    ) THEN
        ALTER PUBLICATION supabase_realtime ADD TABLE public.cunas;
    END IF;
END;
$$;

-- -----------------------------------------------------------------------------
-- 2. comandos: RLS, permisos mínimos y políticas
-- -----------------------------------------------------------------------------
ALTER TABLE public.comandos ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.comandos FORCE ROW LEVEL SECURITY;

-- La tabla se creó desde el panel: Supabase le da todos los permisos a authenticated.
-- Se dejan solo SELECT e INSERT; anon no tiene nada.
REVOKE ALL ON public.comandos FROM PUBLIC, anon, authenticated;
GRANT SELECT, INSERT ON public.comandos TO authenticated;

-- Quitar cualquier política previa (creadas a mano, nombres desconocidos):
-- una política permisiva olvidada se sumaría a las nuevas con OR.
DO $$
DECLARE
    pol record;
BEGIN
    FOR pol IN
        SELECT policyname FROM pg_policies WHERE schemaname = 'public' AND tablename = 'comandos'
    LOOP
        EXECUTE format('DROP POLICY %I ON public.comandos', pol.policyname);
    END LOOP;
END;
$$;

-- Lectura: solo comandos de cunas de mi familia (necesaria para Realtime).
CREATE POLICY comandos_select_miembro ON public.comandos
    FOR SELECT TO authenticated
    USING (cuna_id IS NOT NULL AND public.es_miembro_cuna(cuna_id));

-- Inserción: solo para cunas de mi familia, siempre 'pendiente' y con payload válido.
--   movil_on : {"patron": secuencia|parpadeo|columnas|filas|espiral,
--               "velocidad": lenta|media|rapida,
--               "duracion_min": entero 1..30}
--   movil_off: {}
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

-- Sin políticas de UPDATE/DELETE: los usuarios no pueden confirmar ni borrar comandos.

-- -----------------------------------------------------------------------------
-- 3. cunas: lectura por miembros de la familia (ya existe en la migración M1)
-- -----------------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_policies
         WHERE schemaname = 'public' AND tablename = 'cunas' AND policyname = 'cunas_select_miembro'
    ) THEN
        CREATE POLICY cunas_select_miembro ON public.cunas
            FOR SELECT TO authenticated
            USING (public.es_miembro_familia(family_id));
    END IF;
END;
$$;

ALTER TABLE public.cunas ENABLE ROW LEVEL SECURITY;

COMMIT;

-- -----------------------------------------------------------------------------
-- Verificación (ejecutar después; solo lectura)
-- -----------------------------------------------------------------------------
-- SELECT tablename FROM pg_publication_tables
--  WHERE pubname = 'supabase_realtime' AND tablename IN ('comandos', 'cunas');
--
-- SELECT tablename, policyname, cmd, roles FROM pg_policies
--  WHERE schemaname = 'public' AND tablename IN ('comandos', 'cunas') ORDER BY 1, 2;
--
-- SELECT relname, relrowsecurity AS rls, relforcerowsecurity AS rls_forzado
--   FROM pg_class WHERE oid IN ('public.comandos'::regclass, 'public.cunas'::regclass);
