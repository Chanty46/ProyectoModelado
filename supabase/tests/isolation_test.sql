-- =============================================================================
-- Pruebas SQL de Aislamiento entre Familias, Roles y Reglas de Negocio (M1)
-- Verifica:
--   1. Ninguna tabla tiene acceso para anon.
--   2. Ninguna política contiene USING (true) ni WITH CHECK (true).
--   3. Usuario de Familia A solo ve datos de Familia A y 0 filas de Familia B.
--   4. Usuario de Familia A no puede modificar ni insertar datos en Familia B.
--   5. Usuario secundario de Familia A no puede ejecutar acciones de Principal.
--   6. Límite de 6 perfiles activos (RF23) y cálculo de ml / alerta térmica (RF16/17).
-- Se ejecuta dentro de una transacción con ROLLBACK al final para no ensuciar BD.
-- =============================================================================

BEGIN;

-- 1. Verificación estructural del catálogo de políticas RLS y tablas
DO $$
DECLARE
    v_tablas_sin_rls integer;
    v_politicas_true integer;
    v_politicas_anon integer;
BEGIN
    SELECT count(*)
      INTO v_tablas_sin_rls
      FROM pg_class c
      JOIN pg_namespace n ON n.oid = c.relnamespace
     WHERE n.nspname = 'public'
       AND c.relkind = 'r'
       AND (c.relrowsecurity IS FALSE OR c.relforcerowsecurity IS FALSE);

    IF v_tablas_sin_rls > 0 THEN
        RAISE EXCEPTION 'Fallo de seguridad: existen % tablas en public sin RLS activo o forzado.', v_tablas_sin_rls;
    END IF;

    SELECT count(*)
      INTO v_politicas_true
      FROM pg_policies
     WHERE schemaname = 'public'
       AND (
           lower(btrim(coalesce(qual, ''))) = 'true'
           OR lower(btrim(coalesce(with_check, ''))) = 'true'
       );

    IF v_politicas_true > 0 THEN
        RAISE EXCEPTION 'Fallo de seguridad: existen % políticas con USING (true) o WITH CHECK (true).', v_politicas_true;
    END IF;

    SELECT count(*)
      INTO v_politicas_anon
      FROM pg_policies
     WHERE schemaname = 'public'
       AND 'anon' = ANY(roles);

    IF v_politicas_anon > 0 THEN
        RAISE EXCEPTION 'Fallo de seguridad: existen % políticas asignadas al rol anon.', v_politicas_anon;
    END IF;
END;
$$;

-- 2. Simulación de Principal de Familia A ('11111111-1111-4111-8111-111111111101')
SET LOCAL ROLE authenticated;
SET LOCAL "request.jwt.claims" = '{"sub":"11111111-1111-4111-8111-111111111101","role":"authenticated"}';

DO $$
DECLARE
    v_familias_vistas integer;
    v_familias_b_vistas integer;
    v_cunas_b_vistas integer;
    v_telemetria_b_vista integer;
    v_filas_afectadas integer;
BEGIN
    SELECT count(*) INTO v_familias_vistas FROM public.familias;
    SELECT count(*) INTO v_familias_b_vistas
      FROM public.familias
     WHERE id = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
    SELECT count(*) INTO v_cunas_b_vistas
      FROM public.cunas
     WHERE family_id = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
    SELECT count(*) INTO v_telemetria_b_vista
      FROM public.telemetria
     WHERE cuna_id = 'bbbbbbbb-c001-4000-8000-000000000001';

    IF v_familias_vistas <> 1 OR v_familias_b_vistas <> 0 OR v_cunas_b_vistas <> 0 OR v_telemetria_b_vista <> 0 THEN
        RAISE EXCEPTION 'Fallo de aislamiento en lectura: Familia A vio datos de Familia B.';
    END IF;

    -- Intento de actualizar cuna de Familia B desde sesión de Familia A (debe afectar 0 filas)
    UPDATE public.cunas
       SET nombre = 'Intrusión desde A'
     WHERE id = 'bbbbbbbb-c001-4000-8000-000000000001';
    GET DIAGNOSTICS v_filas_afectadas = ROW_COUNT;

    IF v_filas_afectadas <> 0 THEN
        RAISE EXCEPTION 'Fallo de aislamiento en escritura: Familia A modificó una cuna de Familia B.';
    END IF;

    -- Intento de insertar perfil en Familia B desde sesión de Familia A (debe lanzar excepción RLS)
    BEGIN
        INSERT INTO public.perfiles_cuidador (family_id, nombre)
        VALUES ('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'Intruso');
        RAISE EXCEPTION 'Fallo de aislamiento: se permitió INSERT cruzado en perfiles_cuidador.';
    EXCEPTION
        WHEN insufficient_privilege THEN
            NULL; -- Esperado por RLS
    END;
END;
$$;

-- 3. Verificación de restricción de Usuario Secundario de Familia A ('11111111-1111-4111-8111-111111111102')
SET LOCAL "request.jwt.claims" = '{"sub":"11111111-1111-4111-8111-111111111102","role":"authenticated"}';

DO $$
DECLARE
    v_filas_afectadas integer;
BEGIN
    -- Secundario NO puede modificar umbrales de la cuna de su propia familia (RF03)
    UPDATE public.umbrales
       SET temp_max = 28.0
     WHERE cuna_id = 'aaaaaaaa-c001-4000-8000-000000000001';
    GET DIAGNOSTICS v_filas_afectadas = ROW_COUNT;

    IF v_filas_afectadas <> 0 THEN
        RAISE EXCEPTION 'Fallo RF03: usuario secundario pudo modificar umbrales.';
    END IF;

    -- Secundario NO puede eliminar el perfil del bebé (RF03)
    DELETE FROM public.bebes
     WHERE id = 'aaaaaaaa-b001-4000-8000-000000000001';
    GET DIAGNOSTICS v_filas_afectadas = ROW_COUNT;

    IF v_filas_afectadas <> 0 THEN
        RAISE EXCEPTION 'Fallo RF03: usuario secundario pudo eliminar un bebé.';
    END IF;

    -- Secundario NO puede crear perfiles de cuidador (RF23 reservado a Principal)
    BEGIN
        INSERT INTO public.perfiles_cuidador (family_id, nombre)
        VALUES ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'Perfil no autorizado');
        RAISE EXCEPTION 'Fallo RF23: usuario secundario pudo crear un perfil de cuidador.';
    EXCEPTION
        WHEN insufficient_privilege THEN
            NULL; -- Esperado por RLS
    END;
END;
$$;

-- 4. Verificación de reglas RF23 (máx 6 perfiles activos) y RF16/RF17 (cálculo de ml y alerta térmica)
SET LOCAL "request.jwt.claims" = '{"sub":"11111111-1111-4111-8111-111111111101","role":"authenticated"}';

DO $$
DECLARE
    v_ml numeric(6,1);
    v_alerta boolean;
BEGIN
    -- Familia A ya tiene 2 perfiles activos en la semilla; agregamos 4 más para llegar a 6
    INSERT INTO public.perfiles_cuidador (family_id, nombre) VALUES
        ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'Cuidador 3'),
        ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'Cuidador 4'),
        ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'Cuidador 5'),
        ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'Cuidador 6');

    -- El 7.º perfil activo debe fallar por el trigger RF23
    BEGIN
        INSERT INTO public.perfiles_cuidador (family_id, nombre)
        VALUES ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'Cuidador 7 Exceso');
        RAISE EXCEPTION 'Fallo RF23: se permitió un 7.º perfil de cuidador activo.';
    EXCEPTION
        WHEN raise_exception THEN
            NULL; -- Esperado por trigger RF23
    END;

    -- Prueba de sesión de alimentación (RF16 y RF17: densidad 1.03 g/ml)
    INSERT INTO public.sesiones_alimentacion (bebe_id, perfil_id, m_inicial, m_final, temp_c)
    VALUES (
        'aaaaaaaa-b001-4000-8000-000000000001',
        'aaaaaaaa-0001-4000-8000-000000000001',
        153.0,
        50.0,
        39.2 -- > 38.5 °C debe activar alerta_temperatura
    )
    RETURNING ml, alerta_temperatura INTO v_ml, v_alerta;

    -- (153.0 - 50.0) / 1.03 = 100.0 ml
    IF v_ml <> 100.0 OR v_alerta IS NOT TRUE THEN
        RAISE EXCEPTION 'Fallo RF16/RF17: ml calculado=% (esperado 100.0), alerta=% (esperado true).', v_ml, v_alerta;
    END IF;
END;
$$;

ROLLBACK;
