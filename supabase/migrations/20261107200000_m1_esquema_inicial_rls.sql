-- =============================================================================
-- Migración M1: Esquema inicial, funciones de membresía, RLS estricto, índices
-- y publicación Realtime para el proyecto Cuna Inteligente.
-- Reglas aplicadas (AGENTS.md y docs/SECURITY.md):
--   1. Toda tabla de public tiene RLS activado y forzado.
--   2. Prohibido USING (true) / WITH CHECK (true).
--   3. Ningún permiso para el rol anon.
--   4. Autorización basada exclusivamente en la tabla public.usuarios (D9).
--   5. Índice (cuna_id, ts) en telemetria.
-- =============================================================================

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- -----------------------------------------------------------------------------
-- 1. Tablas de Familia y Acceso
-- -----------------------------------------------------------------------------

CREATE TABLE public.familias (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    nombre text NOT NULL CHECK (char_length(btrim(nombre)) BETWEEN 1 AND 80),
    creado_en timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE public.usuarios (
    id uuid PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    family_id uuid NOT NULL REFERENCES public.familias(id) ON DELETE CASCADE,
    rol text NOT NULL CHECK (rol IN ('principal', 'secundario')),
    email text NOT NULL CHECK (char_length(btrim(email)) BETWEEN 3 AND 254),
    creado_en timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE public.perfiles_cuidador (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    family_id uuid NOT NULL REFERENCES public.familias(id) ON DELETE CASCADE,
    nombre text NOT NULL CHECK (char_length(btrim(nombre)) BETWEEN 1 AND 30),
    avatar text NOT NULL DEFAULT 'default',
    activo boolean NOT NULL DEFAULT true,
    eliminado_en timestamptz,
    creado_en timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE public.invitaciones (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    family_id uuid NOT NULL REFERENCES public.familias(id) ON DELETE CASCADE,
    creado_por uuid REFERENCES public.usuarios(id) ON DELETE SET NULL,
    hash_codigo text NOT NULL UNIQUE CHECK (char_length(hash_codigo) >= 32),
    expira_en timestamptz NOT NULL,
    intentos integer NOT NULL DEFAULT 0 CHECK (intentos >= 0 AND intentos <= 10),
    usada boolean NOT NULL DEFAULT false,
    usada_por uuid REFERENCES public.usuarios(id) ON DELETE SET NULL,
    usada_en timestamptz,
    creado_en timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE public.telegram_vinculos (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    usuario_id uuid NOT NULL UNIQUE REFERENCES public.usuarios(id) ON DELETE CASCADE,
    chat_id bigint UNIQUE,
    hash_codigo text CHECK (hash_codigo IS NULL OR char_length(hash_codigo) >= 32),
    codigo_expira_en timestamptz,
    codigo_usado boolean NOT NULL DEFAULT false,
    intentos integer NOT NULL DEFAULT 0 CHECK (intentos >= 0 AND intentos <= 10),
    vinculado_en timestamptz,
    creado_en timestamptz NOT NULL DEFAULT now()
);

-- -----------------------------------------------------------------------------
-- 2. Tablas de Cunas, Dispositivos, Umbrales y Canciones
-- -----------------------------------------------------------------------------

CREATE TABLE public.cunas (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    family_id uuid NOT NULL REFERENCES public.familias(id) ON DELETE CASCADE,
    nombre text NOT NULL CHECK (char_length(btrim(nombre)) BETWEEN 1 AND 60),
    estado_conexion text NOT NULL DEFAULT 'desconectada'
        CHECK (estado_conexion IN ('en_linea', 'desconectada')),
    modo_operacion text NOT NULL DEFAULT 'monitoreo'
        CHECK (modo_operacion IN ('monitoreo', 'atencion', 'alarma')),
    modo_atencion_expira_en timestamptz,
    luz_encendida boolean NOT NULL DEFAULT false,
    luz_nivel integer NOT NULL DEFAULT 0 CHECK (luz_nivel BETWEEN 0 AND 100),
    escala_masa numeric(4,2) NOT NULL DEFAULT 0.20 CHECK (escala_masa > 0 AND escala_masa <= 1.00),
    ultimo_heartbeat timestamptz,
    creado_en timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE public.dispositivos (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    device_id text NOT NULL UNIQUE CHECK (char_length(btrim(device_id)) BETWEEN 3 AND 64),
    cuna_id uuid REFERENCES public.cunas(id) ON DELETE SET NULL,
    tipo text NOT NULL CHECK (tipo IN ('central', 'colchon', 'movil', 'biberon', 'juguete')),
    hash_reclamo text CHECK (hash_reclamo IS NULL OR char_length(hash_reclamo) >= 32),
    reclamo_expira_en timestamptz,
    intentos_reclamo integer NOT NULL DEFAULT 0 CHECK (intentos_reclamo BETWEEN 0 AND 5),
    hash_token text CHECK (hash_token IS NULL OR char_length(hash_token) >= 32),
    estado text NOT NULL DEFAULT 'sin_dueno'
        CHECK (estado IN ('sin_dueno', 'vinculado', 'en_linea', 'desconectado', 'revocado')),
    bateria_pct integer CHECK (bateria_pct IS NULL OR (bateria_pct BETWEEN 0 AND 100)),
    ultimo_seq bigint NOT NULL DEFAULT 0 CHECK (ultimo_seq >= 0),
    ultimo_heartbeat timestamptz,
    fw_version text,
    creado_en timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE public.umbrales (
    cuna_id uuid PRIMARY KEY REFERENCES public.cunas(id) ON DELETE CASCADE,
    temp_min numeric(4,1) NOT NULL DEFAULT 18.0 CHECK (temp_min BETWEEN 10.0 AND 30.0),
    temp_max numeric(4,1) NOT NULL DEFAULT 26.0 CHECK (temp_max BETWEEN 15.0 AND 38.0 AND temp_max > temp_min),
    hum_min numeric(4,1) NOT NULL DEFAULT 40.0 CHECK (hum_min BETWEEN 10.0 AND 80.0),
    hum_max numeric(4,1) NOT NULL DEFAULT 60.0 CHECK (hum_max BETWEEN 20.0 AND 95.0 AND hum_max > hum_min),
    movimiento_umbral numeric(6,3) NOT NULL DEFAULT 0.150 CHECK (movimiento_umbral > 0 AND movimiento_umbral <= 10.000),
    ruido_masa_piso numeric(6,3) NOT NULL DEFAULT 0.010 CHECK (ruido_masa_piso >= 0 AND ruido_masa_piso <= 1.000),
    actualizado_en timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE public.canciones (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    codigo text NOT NULL UNIQUE CHECK (char_length(btrim(codigo)) BETWEEN 2 AND 40),
    nombre text NOT NULL CHECK (char_length(btrim(nombre)) BETWEEN 1 AND 80),
    storage_path text,
    notas_json jsonb NOT NULL DEFAULT '[]'::jsonb,
    duracion_s integer NOT NULL DEFAULT 30 CHECK (duracion_s BETWEEN 5 AND 600)
);

-- -----------------------------------------------------------------------------
-- 3. Tablas de Bebé, Telemetría y Sesiones
-- -----------------------------------------------------------------------------

CREATE TABLE public.bebes (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    family_id uuid NOT NULL REFERENCES public.familias(id) ON DELETE CASCADE,
    cuna_id uuid REFERENCES public.cunas(id) ON DELETE SET NULL,
    nombre text NOT NULL CHECK (char_length(btrim(nombre)) BETWEEN 1 AND 60),
    fecha_nacimiento date NOT NULL CHECK (fecha_nacimiento <= CURRENT_DATE),
    notas text,
    creado_en timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE public.telemetria (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    cuna_id uuid NOT NULL REFERENCES public.cunas(id) ON DELETE CASCADE,
    dispositivo_id uuid REFERENCES public.dispositivos(id) ON DELETE SET NULL,
    ts timestamptz NOT NULL,
    masa_kg numeric(6,3) CHECK (masa_kg IS NULL OR (masa_kg >= 0 AND masa_kg <= 30.000)),
    indice_movimiento numeric(6,3) CHECK (indice_movimiento IS NULL OR indice_movimiento >= 0),
    temp_c numeric(5,2) CHECK (temp_c IS NULL OR (temp_c BETWEEN -10.00 AND 60.00)),
    humedad numeric(5,2) CHECK (humedad IS NULL OR (humedad BETWEEN 0.00 AND 100.00)),
    masa_biberon_g numeric(6,1) CHECK (masa_biberon_g IS NULL OR (masa_biberon_g BETWEEN 0.0 AND 2000.0)),
    temp_biberon_c numeric(5,2) CHECK (temp_biberon_c IS NULL OR (temp_biberon_c BETWEEN 0.00 AND 100.00)),
    bateria_pct integer CHECK (bateria_pct IS NULL OR (bateria_pct BETWEEN 0 AND 100)),
    seq bigint CHECK (seq IS NULL OR seq >= 0)
);

CREATE TABLE public.sesiones_sueno (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    bebe_id uuid NOT NULL REFERENCES public.bebes(id) ON DELETE CASCADE,
    cuna_id uuid NOT NULL REFERENCES public.cunas(id) ON DELETE CASCADE,
    tipo text NOT NULL DEFAULT 'noche' CHECK (tipo IN ('siesta', 'noche')),
    inicio timestamptz NOT NULL,
    fin timestamptz CHECK (fin IS NULL OR fin >= inicio),
    movimiento_promedio numeric(6,3) CHECK (movimiento_promedio IS NULL OR movimiento_promedio >= 0),
    interrupciones integer NOT NULL DEFAULT 0 CHECK (interrupciones >= 0)
);

CREATE TABLE public.sesiones_alimentacion (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    bebe_id uuid NOT NULL REFERENCES public.bebes(id) ON DELETE CASCADE,
    perfil_id uuid REFERENCES public.perfiles_cuidador(id) ON DELETE SET NULL,
    m_inicial numeric(6,1) NOT NULL CHECK (m_inicial >= 20.0 AND m_inicial <= 2000.0),
    m_final numeric(6,1) CHECK (m_final IS NULL OR (m_final >= 0.0 AND m_final <= m_inicial)),
    ml numeric(6,1) CHECK (ml IS NULL OR ml >= 0.0),
    temp_c numeric(5,2) CHECK (temp_c IS NULL OR (temp_c BETWEEN 0.00 AND 100.00)),
    alerta_temperatura boolean NOT NULL DEFAULT false,
    inicio timestamptz NOT NULL DEFAULT now(),
    fin timestamptz CHECK (fin IS NULL OR fin >= inicio)
);

-- -----------------------------------------------------------------------------
-- 4. Tablas de Eventos, Recordatorios, Juguetes y Bitácoras
-- -----------------------------------------------------------------------------

CREATE TABLE public.eventos_alertas (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    cuna_id uuid NOT NULL REFERENCES public.cunas(id) ON DELETE CASCADE,
    dispositivo_id uuid REFERENCES public.dispositivos(id) ON DELETE SET NULL,
    tipo text NOT NULL CHECK (char_length(btrim(tipo)) BETWEEN 2 AND 50),
    severidad text NOT NULL CHECK (severidad IN ('info', 'advertencia', 'critica')),
    mensaje text,
    datos jsonb,
    ts timestamptz NOT NULL DEFAULT now(),
    atendida boolean NOT NULL DEFAULT false,
    atendida_por uuid REFERENCES public.perfiles_cuidador(id) ON DELETE SET NULL,
    atendida_en timestamptz
);

CREATE TABLE public.recordatorios (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    family_id uuid NOT NULL UNIQUE REFERENCES public.familias(id) ON DELETE CASCADE,
    intervalo_h integer NOT NULL DEFAULT 3 CHECK (intervalo_h BETWEEN 1 AND 8),
    activo boolean NOT NULL DEFAULT true,
    ultimo_envio timestamptz,
    actualizado_en timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE public.uso_juguetes (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    cuna_id uuid NOT NULL REFERENCES public.cunas(id) ON DELETE CASCADE,
    tipo text NOT NULL CHECK (tipo IN ('movil', 'botones')),
    perfil_id uuid REFERENCES public.perfiles_cuidador(id) ON DELETE SET NULL,
    inicio timestamptz NOT NULL DEFAULT now(),
    duracion integer NOT NULL CHECK (duracion >= 0 AND duracion <= 86400),
    aciertos integer CHECK (aciertos IS NULL OR aciertos >= 0),
    patron text,
    velocidad integer CHECK (velocidad IS NULL OR (velocidad BETWEEN 1 AND 10))
);

CREATE TABLE public.bitacora (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    family_id uuid NOT NULL REFERENCES public.familias(id) ON DELETE CASCADE,
    usuario_id uuid REFERENCES public.usuarios(id) ON DELETE SET NULL,
    perfil_id uuid REFERENCES public.perfiles_cuidador(id) ON DELETE SET NULL,
    accion text NOT NULL CHECK (char_length(btrim(accion)) BETWEEN 2 AND 100),
    detalle jsonb,
    ts timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE public.bitacora_seguridad (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    family_id uuid REFERENCES public.familias(id) ON DELETE SET NULL,
    usuario_id uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    dispositivo_id uuid REFERENCES public.dispositivos(id) ON DELETE SET NULL,
    evento text NOT NULL CHECK (char_length(btrim(evento)) BETWEEN 2 AND 100),
    ip_origen text,
    detalle jsonb,
    ts timestamptz NOT NULL DEFAULT now()
);

-- -----------------------------------------------------------------------------
-- 5. Triggers de Reglas de Negocio (RF23 máx 6 perfiles, RF16/RF17 alimentación)
-- -----------------------------------------------------------------------------

CREATE OR REPLACE FUNCTION public.verificar_limite_perfiles_cuidador()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
DECLARE
    v_activos integer;
BEGIN
    IF NEW.activo IS TRUE THEN
        SELECT count(*)
          INTO v_activos
          FROM public.perfiles_cuidador p
         WHERE p.family_id = NEW.family_id
           AND p.activo IS TRUE
           AND (TG_OP = 'INSERT' OR p.id <> NEW.id);

        IF v_activos >= 6 THEN
            RAISE EXCEPTION 'RF23: No se permiten más de 6 perfiles de cuidador activos por familia.';
        END IF;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_limite_perfiles_cuidador
BEFORE INSERT OR UPDATE ON public.perfiles_cuidador
FOR EACH ROW
EXECUTE FUNCTION public.verificar_limite_perfiles_cuidador();

CREATE OR REPLACE FUNCTION public.calcular_sesion_alimentacion()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
BEGIN
    IF NEW.m_final IS NOT NULL THEN
        NEW.ml := round((NEW.m_inicial - NEW.m_final) / 1.03, 1);
    END IF;
    IF NEW.temp_c IS NOT NULL THEN
        NEW.alerta_temperatura := (NEW.temp_c < 35.0 OR NEW.temp_c > 38.5);
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_calcular_sesion_alimentacion
BEFORE INSERT OR UPDATE ON public.sesiones_alimentacion
FOR EACH ROW
EXECUTE FUNCTION public.calcular_sesion_alimentacion();

-- -----------------------------------------------------------------------------
-- 6. Índices (incluye índice obligatorio (cuna_id, ts) en telemetria)
-- -----------------------------------------------------------------------------

CREATE INDEX idx_telemetria_cuna_ts ON public.telemetria (cuna_id, ts DESC);
CREATE INDEX idx_usuarios_family_id ON public.usuarios (family_id);
CREATE INDEX idx_perfiles_family_id ON public.perfiles_cuidador (family_id) WHERE activo IS TRUE;
CREATE INDEX idx_cunas_family_id ON public.cunas (family_id);
CREATE INDEX idx_dispositivos_cuna_id ON public.dispositivos (cuna_id);
CREATE INDEX idx_bebes_family_id ON public.bebes (family_id);
CREATE INDEX idx_eventos_alertas_cuna_ts ON public.eventos_alertas (cuna_id, ts DESC);
CREATE INDEX idx_sesiones_sueno_bebe_inicio ON public.sesiones_sueno (bebe_id, inicio DESC);
CREATE INDEX idx_sesiones_alimentacion_bebe_inicio ON public.sesiones_alimentacion (bebe_id, inicio DESC);
CREATE INDEX idx_bitacora_family_ts ON public.bitacora (family_id, ts DESC);
CREATE INDEX idx_bitacora_seguridad_family_ts ON public.bitacora_seguridad (family_id, ts DESC);

-- -----------------------------------------------------------------------------
-- 7. Funciones Auxiliares de Membresía y Rol (D9 — solo consultan public.usuarios)
-- -----------------------------------------------------------------------------

CREATE OR REPLACE FUNCTION public.obtener_mi_family_id()
RETURNS uuid
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = ''
AS $$
    SELECT u.family_id
      FROM public.usuarios u
     WHERE u.id = auth.uid()
     LIMIT 1;
$$;

CREATE OR REPLACE FUNCTION public.es_miembro_familia(p_family_id uuid)
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = ''
AS $$
    SELECT EXISTS (
        SELECT 1
          FROM public.usuarios u
         WHERE u.id = auth.uid()
           AND u.family_id = p_family_id
    );
$$;

CREATE OR REPLACE FUNCTION public.es_principal_familia(p_family_id uuid)
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = ''
AS $$
    SELECT EXISTS (
        SELECT 1
          FROM public.usuarios u
         WHERE u.id = auth.uid()
           AND u.family_id = p_family_id
           AND u.rol = 'principal'
    );
$$;

CREATE OR REPLACE FUNCTION public.es_miembro_cuna(p_cuna_id uuid)
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = ''
AS $$
    SELECT EXISTS (
        SELECT 1
          FROM public.cunas c
          JOIN public.usuarios u ON u.family_id = c.family_id
         WHERE c.id = p_cuna_id
           AND u.id = auth.uid()
    );
$$;

CREATE OR REPLACE FUNCTION public.es_principal_cuna(p_cuna_id uuid)
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = ''
AS $$
    SELECT EXISTS (
        SELECT 1
          FROM public.cunas c
          JOIN public.usuarios u ON u.family_id = c.family_id
         WHERE c.id = p_cuna_id
           AND u.id = auth.uid()
           AND u.rol = 'principal'
    );
$$;

CREATE OR REPLACE FUNCTION public.es_miembro_bebe(p_bebe_id uuid)
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = ''
AS $$
    SELECT EXISTS (
        SELECT 1
          FROM public.bebes b
          JOIN public.usuarios u ON u.family_id = b.family_id
         WHERE b.id = p_bebe_id
           AND u.id = auth.uid()
    );
$$;

REVOKE ALL ON FUNCTION public.obtener_mi_family_id() FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.es_miembro_familia(uuid) FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.es_principal_familia(uuid) FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.es_miembro_cuna(uuid) FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.es_principal_cuna(uuid) FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.es_miembro_bebe(uuid) FROM PUBLIC, anon;

GRANT EXECUTE ON FUNCTION public.obtener_mi_family_id() TO authenticated;
GRANT EXECUTE ON FUNCTION public.es_miembro_familia(uuid) TO authenticated;
GRANT EXECUTE ON FUNCTION public.es_principal_familia(uuid) TO authenticated;
GRANT EXECUTE ON FUNCTION public.es_miembro_cuna(uuid) TO authenticated;
GRANT EXECUTE ON FUNCTION public.es_principal_cuna(uuid) TO authenticated;
GRANT EXECUTE ON FUNCTION public.es_miembro_bebe(uuid) TO authenticated;

-- -----------------------------------------------------------------------------
-- 8. Permisos de Rol (Nada para anon; mínimo privilegio para authenticated)
-- -----------------------------------------------------------------------------

REVOKE ALL ON ALL TABLES IN SCHEMA public FROM PUBLIC, anon;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM PUBLIC, anon;

GRANT SELECT, UPDATE ON public.familias TO authenticated;
GRANT SELECT, DELETE ON public.usuarios TO authenticated;
GRANT SELECT, INSERT, UPDATE, DELETE ON public.perfiles_cuidador TO authenticated;
GRANT SELECT ON public.invitaciones TO authenticated;
GRANT SELECT, DELETE ON public.telegram_vinculos TO authenticated;
GRANT SELECT, UPDATE ON public.cunas TO authenticated;
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
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO authenticated;

-- -----------------------------------------------------------------------------
-- 9. Activación y Forzado de RLS en todas las tablas
-- -----------------------------------------------------------------------------

ALTER TABLE public.familias ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.familias FORCE ROW LEVEL SECURITY;

ALTER TABLE public.usuarios ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.usuarios FORCE ROW LEVEL SECURITY;

ALTER TABLE public.perfiles_cuidador ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.perfiles_cuidador FORCE ROW LEVEL SECURITY;

ALTER TABLE public.invitaciones ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.invitaciones FORCE ROW LEVEL SECURITY;

ALTER TABLE public.telegram_vinculos ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.telegram_vinculos FORCE ROW LEVEL SECURITY;

ALTER TABLE public.cunas ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.cunas FORCE ROW LEVEL SECURITY;

ALTER TABLE public.dispositivos ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.dispositivos FORCE ROW LEVEL SECURITY;

ALTER TABLE public.umbrales ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.umbrales FORCE ROW LEVEL SECURITY;

ALTER TABLE public.canciones ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.canciones FORCE ROW LEVEL SECURITY;

ALTER TABLE public.bebes ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.bebes FORCE ROW LEVEL SECURITY;

ALTER TABLE public.telemetria ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.telemetria FORCE ROW LEVEL SECURITY;

ALTER TABLE public.sesiones_sueno ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.sesiones_sueno FORCE ROW LEVEL SECURITY;

ALTER TABLE public.sesiones_alimentacion ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.sesiones_alimentacion FORCE ROW LEVEL SECURITY;

ALTER TABLE public.eventos_alertas ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.eventos_alertas FORCE ROW LEVEL SECURITY;

ALTER TABLE public.recordatorios ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.recordatorios FORCE ROW LEVEL SECURITY;

ALTER TABLE public.uso_juguetes ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.uso_juguetes FORCE ROW LEVEL SECURITY;

ALTER TABLE public.bitacora ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.bitacora FORCE ROW LEVEL SECURITY;

ALTER TABLE public.bitacora_seguridad ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.bitacora_seguridad FORCE ROW LEVEL SECURITY;

-- -----------------------------------------------------------------------------
-- 10. Políticas RLS Explícitas por Operación (solo authenticated, sin USING(true))
-- -----------------------------------------------------------------------------

-- familias
CREATE POLICY familias_select_miembro ON public.familias
    FOR SELECT TO authenticated
    USING (public.es_miembro_familia(id));

CREATE POLICY familias_update_principal ON public.familias
    FOR UPDATE TO authenticated
    USING (public.es_principal_familia(id))
    WITH CHECK (public.es_principal_familia(id));

-- usuarios
CREATE POLICY usuarios_select_miembro ON public.usuarios
    FOR SELECT TO authenticated
    USING (public.es_miembro_familia(family_id));

CREATE POLICY usuarios_delete_principal ON public.usuarios
    FOR DELETE TO authenticated
    USING (public.es_principal_familia(family_id) AND id <> auth.uid() AND rol = 'secundario');

-- perfiles_cuidador
CREATE POLICY perfiles_select_miembro ON public.perfiles_cuidador
    FOR SELECT TO authenticated
    USING (public.es_miembro_familia(family_id));

CREATE POLICY perfiles_insert_miembro ON public.perfiles_cuidador
    FOR INSERT TO authenticated
    WITH CHECK (public.es_miembro_familia(family_id));

CREATE POLICY perfiles_update_miembro ON public.perfiles_cuidador
    FOR UPDATE TO authenticated
    USING (public.es_miembro_familia(family_id))
    WITH CHECK (public.es_miembro_familia(family_id));

CREATE POLICY perfiles_delete_principal ON public.perfiles_cuidador
    FOR DELETE TO authenticated
    USING (public.es_principal_familia(family_id));

-- invitaciones (solo el Principal ve las invitaciones de su familia; backend inserta/consume)
CREATE POLICY invitaciones_select_principal ON public.invitaciones
    FOR SELECT TO authenticated
    USING (public.es_principal_familia(family_id));

-- telegram_vinculos (cada usuario ve y puede desvincular solo su propio vínculo)
CREATE POLICY telegram_select_propio ON public.telegram_vinculos
    FOR SELECT TO authenticated
    USING (usuario_id = auth.uid() AND public.obtener_mi_family_id() IS NOT NULL);

CREATE POLICY telegram_delete_propio ON public.telegram_vinculos
    FOR DELETE TO authenticated
    USING (usuario_id = auth.uid() AND public.obtener_mi_family_id() IS NOT NULL);

-- cunas
CREATE POLICY cunas_select_miembro ON public.cunas
    FOR SELECT TO authenticated
    USING (public.es_miembro_familia(family_id));

CREATE POLICY cunas_update_principal ON public.cunas
    FOR UPDATE TO authenticated
    USING (public.es_principal_familia(family_id))
    WITH CHECK (public.es_principal_familia(family_id));

-- dispositivos (lectura para miembros; vinculación/estado la escribe el backend)
CREATE POLICY dispositivos_select_miembro ON public.dispositivos
    FOR SELECT TO authenticated
    USING (cuna_id IS NOT NULL AND public.es_miembro_cuna(cuna_id));

-- umbrales
CREATE POLICY umbrales_select_miembro ON public.umbrales
    FOR SELECT TO authenticated
    USING (public.es_miembro_cuna(cuna_id));

CREATE POLICY umbrales_insert_principal ON public.umbrales
    FOR INSERT TO authenticated
    WITH CHECK (public.es_principal_cuna(cuna_id));

CREATE POLICY umbrales_update_principal ON public.umbrales
    FOR UPDATE TO authenticated
    USING (public.es_principal_cuna(cuna_id))
    WITH CHECK (public.es_principal_cuna(cuna_id));

-- canciones (catálogo legible solo para usuarios autenticados que pertenecen a una familia)
CREATE POLICY canciones_select_miembro ON public.canciones
    FOR SELECT TO authenticated
    USING (auth.uid() IS NOT NULL AND public.obtener_mi_family_id() IS NOT NULL);

-- bebes
CREATE POLICY bebes_select_miembro ON public.bebes
    FOR SELECT TO authenticated
    USING (public.es_miembro_familia(family_id));

CREATE POLICY bebes_insert_principal ON public.bebes
    FOR INSERT TO authenticated
    WITH CHECK (public.es_principal_familia(family_id));

CREATE POLICY bebes_update_principal ON public.bebes
    FOR UPDATE TO authenticated
    USING (public.es_principal_familia(family_id))
    WITH CHECK (public.es_principal_familia(family_id));

CREATE POLICY bebes_delete_principal ON public.bebes
    FOR DELETE TO authenticated
    USING (public.es_principal_familia(family_id));

-- telemetria (solo lectura para miembros; solo el backend inserta con clave secreta)
CREATE POLICY telemetria_select_miembro ON public.telemetria
    FOR SELECT TO authenticated
    USING (public.es_miembro_cuna(cuna_id));

-- sesiones_sueno (solo lectura para miembros; las calcula/inserta el backend)
CREATE POLICY sesiones_sueno_select_miembro ON public.sesiones_sueno
    FOR SELECT TO authenticated
    USING (public.es_miembro_bebe(bebe_id));

-- sesiones_alimentacion (miembros consultan e inician/finalizan sesiones de su bebé)
CREATE POLICY sesiones_alimentacion_select_miembro ON public.sesiones_alimentacion
    FOR SELECT TO authenticated
    USING (public.es_miembro_bebe(bebe_id));

CREATE POLICY sesiones_alimentacion_insert_miembro ON public.sesiones_alimentacion
    FOR INSERT TO authenticated
    WITH CHECK (public.es_miembro_bebe(bebe_id));

CREATE POLICY sesiones_alimentacion_update_miembro ON public.sesiones_alimentacion
    FOR UPDATE TO authenticated
    USING (public.es_miembro_bebe(bebe_id))
    WITH CHECK (public.es_miembro_bebe(bebe_id));

-- eventos_alertas (miembros leen y marcan alertas como atendidas; backend las inserta)
CREATE POLICY eventos_alertas_select_miembro ON public.eventos_alertas
    FOR SELECT TO authenticated
    USING (public.es_miembro_cuna(cuna_id));

CREATE POLICY eventos_alertas_update_miembro ON public.eventos_alertas
    FOR UPDATE TO authenticated
    USING (public.es_miembro_cuna(cuna_id))
    WITH CHECK (public.es_miembro_cuna(cuna_id));

-- recordatorios
CREATE POLICY recordatorios_select_miembro ON public.recordatorios
    FOR SELECT TO authenticated
    USING (public.es_miembro_familia(family_id));

CREATE POLICY recordatorios_insert_miembro ON public.recordatorios
    FOR INSERT TO authenticated
    WITH CHECK (public.es_miembro_familia(family_id));

CREATE POLICY recordatorios_update_miembro ON public.recordatorios
    FOR UPDATE TO authenticated
    USING (public.es_miembro_familia(family_id))
    WITH CHECK (public.es_miembro_familia(family_id));

-- uso_juguetes (solo lectura para miembros; backend inserta eventos del móvil y juguete)
CREATE POLICY uso_juguetes_select_miembro ON public.uso_juguetes
    FOR SELECT TO authenticated
    USING (public.es_miembro_cuna(cuna_id));

-- bitacora
CREATE POLICY bitacora_select_miembro ON public.bitacora
    FOR SELECT TO authenticated
    USING (public.es_miembro_familia(family_id));

CREATE POLICY bitacora_insert_miembro ON public.bitacora
    FOR INSERT TO authenticated
    WITH CHECK (public.es_miembro_familia(family_id));

-- bitacora_seguridad (solo el Principal puede consultar los eventos de su familia; backend inserta)
CREATE POLICY bitacora_seguridad_select_principal ON public.bitacora_seguridad
    FOR SELECT TO authenticated
    USING (family_id IS NOT NULL AND public.es_principal_familia(family_id));

-- -----------------------------------------------------------------------------
-- 11. Publicación Realtime (supabase_realtime)
-- -----------------------------------------------------------------------------

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_publication WHERE pubname = 'supabase_realtime') THEN
        CREATE PUBLICATION supabase_realtime;
    END IF;
END;
$$;

ALTER PUBLICATION supabase_realtime ADD TABLE
    public.cunas,
    public.dispositivos,
    public.umbrales,
    public.telemetria,
    public.eventos_alertas,
    public.sesiones_alimentacion,
    public.sesiones_sueno,
    public.perfiles_cuidador,
    public.usuarios;

-- -----------------------------------------------------------------------------
-- 12. Catálogo inicial de canciones por notas PWM (RF12 / D12)
-- -----------------------------------------------------------------------------

INSERT INTO public.canciones (codigo, nombre, notas_json, duracion_s) VALUES
    ('estrellita', 'Estrellita dónde estás', '[{"freq":262,"ms":400},{"freq":262,"ms":400},{"freq":392,"ms":400},{"freq":392,"ms":400},{"freq":440,"ms":400},{"freq":440,"ms":400},{"freq":392,"ms":800}]'::jsonb, 30),
    ('brahms', 'Canción de cuna de Brahms', '[{"freq":330,"ms":300},{"freq":330,"ms":300},{"freq":392,"ms":600},{"freq":330,"ms":300},{"freq":330,"ms":300},{"freq":392,"ms":600}]'::jsonb, 45),
    ('arrorro', 'Arrorró mi niño', '[{"freq":294,"ms":400},{"freq":330,"ms":400},{"freq":349,"ms":400},{"freq":392,"ms":600},{"freq":349,"ms":400},{"freq":330,"ms":600}]'::jsonb, 40);

COMMIT;
