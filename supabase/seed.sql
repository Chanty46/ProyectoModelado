-- =============================================================================
-- Semilla de datos de prueba (M1) — Dos familias aisladas (Familia A y Familia B)
-- Solo datos sintéticos de prueba. Todos los códigos/tokens se guardan como hash
-- SHA-256 de cadenas de demostración documentadas.
-- =============================================================================

BEGIN;

-- Usuarios sintéticos en auth.users para pruebas locales/SQL
INSERT INTO auth.users (
    id, instance_id, aud, role, email, encrypted_password,
    email_confirmed_at, raw_app_meta_data, raw_user_meta_data, created_at, updated_at
) VALUES
    (
        '11111111-1111-4111-8111-111111111101',
        '00000000-0000-0000-0000-000000000000',
        'authenticated', 'authenticated',
        'principal.a@ejemplo.local', '',
        now(), '{"provider":"google"}'::jsonb, '{"full_name":"Principal Familia A"}'::jsonb, now(), now()
    ),
    (
        '11111111-1111-4111-8111-111111111102',
        '00000000-0000-0000-0000-000000000000',
        'authenticated', 'authenticated',
        'secundario.a@ejemplo.local', '',
        now(), '{"provider":"google"}'::jsonb, '{"full_name":"Secundario Familia A"}'::jsonb, now(), now()
    ),
    (
        '22222222-2222-4222-8222-222222222201',
        '00000000-0000-0000-0000-000000000000',
        'authenticated', 'authenticated',
        'principal.b@ejemplo.local', '',
        now(), '{"provider":"google"}'::jsonb, '{"full_name":"Principal Familia B"}'::jsonb, now(), now()
    )
ON CONFLICT (id) DO NOTHING;

-- Familias A y B
INSERT INTO public.familias (id, nombre) VALUES
    ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'Familia A (Prueba)'),
    ('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'Familia B (Prueba)')
ON CONFLICT (id) DO NOTHING;

-- Membresía en public.usuarios
INSERT INTO public.usuarios (id, family_id, rol, email) VALUES
    ('11111111-1111-4111-8111-111111111101', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'principal', 'principal.a@ejemplo.local'),
    ('11111111-1111-4111-8111-111111111102', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'secundario', 'secundario.a@ejemplo.local'),
    ('22222222-2222-4222-8222-222222222201', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'principal', 'principal.b@ejemplo.local')
ON CONFLICT (id) DO NOTHING;

-- Perfiles de cuidador
INSERT INTO public.perfiles_cuidador (id, family_id, nombre, avatar, activo) VALUES
    ('aaaaaaaa-0001-4000-8000-000000000001', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'Mamá Ana', 'F1', true),
    ('aaaaaaaa-0001-4000-8000-000000000002', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'Papá Luis', 'M1', true),
    ('bbbbbbbb-0001-4000-8000-000000000001', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'Mamá Elena', 'F2', true)
ON CONFLICT (id) DO NOTHING;

-- Cunas
INSERT INTO public.cunas (id, family_id, nombre, estado_conexion, modo_operacion, escala_masa) VALUES
    ('aaaaaaaa-c001-4000-8000-000000000001', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'Cuna Habitación A', 'en_linea', 'monitoreo', 0.20),
    ('bbbbbbbb-c001-4000-8000-000000000001', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'Cuna Habitación B', 'en_linea', 'monitoreo', 0.20)
ON CONFLICT (id) DO NOTHING;

-- Umbrales por cuna
INSERT INTO public.umbrales (cuna_id, temp_min, temp_max, hum_min, hum_max, movimiento_umbral, ruido_masa_piso) VALUES
    ('aaaaaaaa-c001-4000-8000-000000000001', 18.0, 26.0, 40.0, 60.0, 0.150, 0.010),
    ('bbbbbbbb-c001-4000-8000-000000000001', 19.0, 25.0, 45.0, 60.0, 0.150, 0.010)
ON CONFLICT (cuna_id) DO NOTHING;

-- Bebés
INSERT INTO public.bebes (id, family_id, cuna_id, nombre, fecha_nacimiento) VALUES
    ('aaaaaaaa-b001-4000-8000-000000000001', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'aaaaaaaa-c001-4000-8000-000000000001', 'Bebé Leo', '2026-05-10'),
    ('bbbbbbbb-b001-4000-8000-000000000001', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'bbbbbbbb-c001-4000-8000-000000000001', 'Bebé Sofía', '2026-06-15')
ON CONFLICT (id) DO NOTHING;

-- Dispositivos de prueba (hashes SHA-256 de cadenas sintéticas de demostración)
INSERT INTO public.dispositivos (id, device_id, cuna_id, tipo, hash_reclamo, hash_token, estado, bateria_pct, fw_version) VALUES
    (
        'aaaaaaaa-d001-4000-8000-000000000001',
        'sim-central-a',
        'aaaaaaaa-c001-4000-8000-000000000001',
        'central',
        encode(extensions.digest('CLAIM-CENTRAL-A-DEMO', 'sha256'), 'hex'),
        encode(extensions.digest('TOKEN-CENTRAL-A-DEMO-000000000000', 'sha256'), 'hex'),
        'en_linea', NULL, '1.0.0-sim'
    ),
    (
        'aaaaaaaa-d001-4000-8000-000000000002',
        'sim-colchon-a',
        'aaaaaaaa-c001-4000-8000-000000000001',
        'colchon',
        encode(extensions.digest('CLAIM-COLCHON-A-DEMO', 'sha256'), 'hex'),
        encode(extensions.digest('TOKEN-COLCHON-A-DEMO-000000000000', 'sha256'), 'hex'),
        'en_linea', NULL, '1.0.0-sim'
    ),
    (
        'bbbbbbbb-d001-4000-8000-000000000001',
        'sim-central-b',
        'bbbbbbbb-c001-4000-8000-000000000001',
        'central',
        encode(extensions.digest('CLAIM-CENTRAL-B-DEMO', 'sha256'), 'hex'),
        encode(extensions.digest('TOKEN-CENTRAL-B-DEMO-000000000000', 'sha256'), 'hex'),
        'en_linea', NULL, '1.0.0-sim'
    )
ON CONFLICT (id) DO NOTHING;

-- Recordatorios por familia
INSERT INTO public.recordatorios (id, family_id, intervalo_h, activo) VALUES
    ('aaaaaaaa-ef01-4000-8000-000000000001', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 3, true),
    ('bbbbbbbb-ef01-4000-8000-000000000001', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 4, true)
ON CONFLICT (id) DO NOTHING;

-- Telemetría inicial para ambas familias
INSERT INTO public.telemetria (cuna_id, dispositivo_id, ts, masa_kg, indice_movimiento, temp_c, humedad, seq) VALUES
    ('aaaaaaaa-c001-4000-8000-000000000001', 'aaaaaaaa-d001-4000-8000-000000000001', now() - interval '2 minutes', 0.650, 0.020, 22.4, 51.0, 1),
    ('aaaaaaaa-c001-4000-8000-000000000001', 'aaaaaaaa-d001-4000-8000-000000000001', now() - interval '1 minute', 0.652, 0.015, 22.5, 51.2, 2),
    ('bbbbbbbb-c001-4000-8000-000000000001', 'bbbbbbbb-d001-4000-8000-000000000001', now() - interval '1 minute', 0.710, 0.010, 23.1, 49.5, 1);

COMMIT;
