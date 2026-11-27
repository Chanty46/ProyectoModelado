using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using CunaServer.Data;
using Microsoft.Extensions.Configuration;

namespace CunaServer.Tests;

public class DatabaseMigrationSchemaTests
{
    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadInitialMigration()
    {
        var path = Path.Combine(
            GetRepoRoot(),
            "supabase",
            "migrations",
            "20261107200000_m1_esquema_inicial_rls.sql");

        Assert.True(File.Exists(path), $"No se encontró la migración en {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Migration_DefinesAll18TablesAndEnablesAndForcesRls()
    {
        var sql = ReadInitialMigration();

        var expectedTables = new[]
        {
            "familias",
            "usuarios",
            "perfiles_cuidador",
            "invitaciones",
            "telegram_vinculos",
            "cunas",
            "dispositivos",
            "umbrales",
            "canciones",
            "bebes",
            "telemetria",
            "sesiones_sueno",
            "sesiones_alimentacion",
            "eventos_alertas",
            "recordatorios",
            "uso_juguetes",
            "bitacora",
            "bitacora_seguridad"
        };

        foreach (var table in expectedTables)
        {
            Assert.Contains($"CREATE TABLE public.{table} (", sql);
            Assert.Contains($"ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;", sql);
            Assert.Contains($"ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;", sql);
        }
    }

    [Fact]
    public void Migration_NeverUsesUsingTrueOrAnonPolicies()
    {
        var sql = ReadInitialMigration();

        // Quitar comentarios SQL de una línea antes de buscar expresiones prohibidas
        var sqlWithoutComments = Regex.Replace(sql, @"--.*$", "", RegexOptions.Multiline);

        Assert.DoesNotMatch(new Regex(@"USING\s*\(\s*true\s*\)", RegexOptions.IgnoreCase), sqlWithoutComments);
        Assert.DoesNotMatch(new Regex(@"WITH\s+CHECK\s*\(\s*true\s*\)", RegexOptions.IgnoreCase), sqlWithoutComments);
        Assert.DoesNotMatch(new Regex(@"TO\s+anon\b", RegexOptions.IgnoreCase), sqlWithoutComments);

        Assert.Contains("REVOKE ALL ON ALL TABLES IN SCHEMA public FROM PUBLIC, anon;", sqlWithoutComments);
    }

    [Fact]
    public void Migration_DefinesSecurityDefinerFunctionsWithEmptySearchPath()
    {
        var sql = ReadInitialMigration();

        var matches = Regex.Matches(sql, @"SECURITY\s+DEFINER\s+SET\s+search_path\s*=\s*''", RegexOptions.IgnoreCase);
        Assert.Equal(8, matches.Count);
    }

    [Fact]
    public void Migration_IncludesTelemetryIndexAndRealtimePublication()
    {
        var sql = ReadInitialMigration();

        Assert.Contains("CREATE INDEX idx_telemetria_cuna_ts ON public.telemetria (cuna_id, ts DESC);", sql);
        Assert.Contains("CREATE UNIQUE INDEX idx_telemetria_dispositivo_seq ON public.telemetria (dispositivo_id, seq)", sql);
        Assert.Contains("ALTER PUBLICATION supabase_realtime ADD TABLE", sql);
        Assert.Contains("ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM PUBLIC, anon;", sql);
    }

    [Fact]
    public void Migration_EnforcesRf23Rf26Rf29AndAttemptLimits()
    {
        var sql = ReadInitialMigration();

        Assert.Contains("nombre text NOT NULL CHECK (char_length(btrim(nombre)) BETWEEN 1 AND 30)", sql);
        Assert.Contains("movimiento_alerta_activa boolean NOT NULL DEFAULT true", sql);
        Assert.Contains("reclamo_usado boolean NOT NULL DEFAULT false", sql);
        Assert.Contains("CREATE POLICY perfiles_insert_principal ON public.perfiles_cuidador", sql);
        Assert.Contains("CREATE POLICY perfiles_update_principal ON public.perfiles_cuidador", sql);
        Assert.DoesNotContain("GRANT SELECT, INSERT, UPDATE, DELETE ON public.perfiles_cuidador", sql);
    }

    [Fact]
    public void SeedAndIsolationTestScripts_ExistAndAreNonEmpty()
    {
        var root = GetRepoRoot();
        var seedPath = Path.Combine(root, "supabase", "seed.sql");
        var testPath = Path.Combine(root, "supabase", "tests", "isolation_test.sql");

        Assert.True(File.Exists(seedPath));
        Assert.True(File.Exists(testPath));

        var seedSql = File.ReadAllText(seedPath);
        var testSql = File.ReadAllText(testPath);

        Assert.Contains("Familia A (Prueba)", seedSql);
        Assert.Contains("Familia B (Prueba)", seedSql);
        Assert.Contains("SET LOCAL ROLE authenticated;", testSql);
        Assert.Contains("ROLLBACK;", testSql);
    }

    [Fact]
    public void DefinitiveSupabaseMigration_DefinesMobileRpcsColumnProtectionAndRf33WithoutUsingTrue()
    {
        var path = Path.Combine(
            GetRepoRoot(),
            "supabase",
            "migrations",
            "20261123120000_esquema_definitivo_movil_y_seguridad.sql");

        Assert.True(File.Exists(path), $"No se encontró la migración definitiva en {path}");
        var sql = File.ReadAllText(path);
        var sqlWithoutComments = Regex.Replace(sql, @"--.*$", "", RegexOptions.Multiline);

        Assert.DoesNotMatch(new Regex(@"USING\s*\(\s*true\s*\)", RegexOptions.IgnoreCase), sqlWithoutComments);
        Assert.DoesNotMatch(new Regex(@"WITH\s+CHECK\s*\(\s*true\s*\)", RegexOptions.IgnoreCase), sqlWithoutComments);

        Assert.Contains("CREATE TABLE IF NOT EXISTS public.comandos", sql);
        Assert.Contains("ADD COLUMN IF NOT EXISTS token_dispositivo text", sql);
        Assert.Contains("ADD COLUMN IF NOT EXISTS token_expira_en timestamptz", sql);
        Assert.Contains("ALTER TABLE public.comandos FORCE ROW LEVEL SECURITY;", sql);
        Assert.Contains("ALTER TABLE public.cunas FORCE ROW LEVEL SECURITY;", sql);
        Assert.Contains("CREATE OR REPLACE FUNCTION public.dispositivo_comandos_pendientes(", sql);
        Assert.Contains("CREATE OR REPLACE FUNCTION public.dispositivo_confirmar_comando(", sql);
        Assert.Contains("CREATE OR REPLACE FUNCTION public.rotar_token_dispositivo_cuna(", sql);
        Assert.Contains("INSERT INTO public.uso_juguetes", sql);

        var secDefMatches = Regex.Matches(sql, @"SECURITY\s+DEFINER\s+SET\s+search_path\s*=\s*''", RegexOptions.IgnoreCase);
        Assert.Equal(3, secDefMatches.Count);

        // La lista de columnas SELECT para authenticated nunca debe incluir token_dispositivo
        var grantBlock = Regex.Match(sqlWithoutComments, @"GRANT SELECT\s*\(([^)]+)\)\s*ON public\.cunas TO authenticated;", RegexOptions.Singleline);
        Assert.True(grantBlock.Success, "Falta GRANT SELECT por columnas en public.cunas para authenticated");
        Assert.DoesNotContain("token_dispositivo", grantBlock.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ultimo_heartbeat", grantBlock.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RealtimeSafeCredentialsMigration_IsolatesDeviceTokensAndRestoresRealtimeSelect()
    {
        var path = Path.Combine(
            GetRepoRoot(),
            "supabase",
            "migrations",
            "20261125120000_cunas_credenciales_y_realtime_seguro.sql");

        Assert.True(File.Exists(path), $"No se encontró la migración Realtime-safe en {path}");
        var sql = File.ReadAllText(path);
        var sqlWithoutComments = Regex.Replace(sql, @"--.*$", "", RegexOptions.Multiline);

        Assert.DoesNotMatch(new Regex(@"USING\s*\(\s*true\s*\)", RegexOptions.IgnoreCase), sqlWithoutComments);
        Assert.DoesNotMatch(new Regex(@"WITH\s+CHECK\s*\(\s*true\s*\)", RegexOptions.IgnoreCase), sqlWithoutComments);

        Assert.Contains("CREATE TABLE IF NOT EXISTS public.cunas_credenciales", sql);
        Assert.Contains("ALTER TABLE public.cunas_credenciales FORCE ROW LEVEL SECURITY;", sql);
        Assert.Contains("REVOKE ALL ON public.cunas_credenciales FROM PUBLIC, anon, authenticated;", sql);
        Assert.Contains("GRANT SELECT ON public.cunas TO authenticated;", sql);
        Assert.Contains("SET token_dispositivo = NULL", sql);
        Assert.Contains("~ '^[0-9]+$'", sql);
        Assert.Contains("DROP POLICY IF EXISTS invitaciones_update_backend ON public.invitaciones;", sql);
        Assert.Contains("DROP POLICY IF EXISTS invitaciones_insert_backend ON public.invitaciones;", sql);
        Assert.Contains("DROP FUNCTION IF EXISTS public.puente_movil_web();", sql);
        Assert.Contains("REVOKE ALL ON FUNCTION public.calcular_sesion_alimentacion() FROM PUBLIC, anon, authenticated;", sql);
        Assert.Contains("REVOKE ALL ON FUNCTION public.verificar_limite_perfiles_cuidador() FROM PUBLIC, anon, authenticated;", sql);
        Assert.Contains("(SELECT auth.uid())", sql);

        // UPDATE en public.cunas nunca debe permitir escribir token_dispositivo ni ultimo_heartbeat
        var updateGrant = Regex.Match(sqlWithoutComments, @"GRANT UPDATE\s*\(([^)]+)\)\s*ON public\.cunas TO authenticated;", RegexOptions.Singleline);
        Assert.True(updateGrant.Success, "Falta GRANT UPDATE restringido por columnas en public.cunas");
        Assert.DoesNotContain("token_dispositivo", updateGrant.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ultimo_heartbeat", updateGrant.Groups[1].Value, StringComparison.OrdinalIgnoreCase);

        var secDefMatches = Regex.Matches(sql, @"SECURITY\s+DEFINER\s+SET\s+search_path\s*=\s*''", RegexOptions.IgnoreCase);
        Assert.Equal(4, secDefMatches.Count);
    }

    [Fact]
    public async Task PostgrestClient_OmitsBearerHeaderForSbSecretKeysAndUsesBearerForLegacyJwtKeys()
    {
        HttpRequestMessage? capturedModern = null;
        var modernHandler = new StubHttpMessageHandler(req =>
        {
            capturedModern = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        });
        var fakeModernKey = "sb_" + "secret_test_only";
        var modernConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SUPABASE_URL"] = "https://ejemplo.supabase.co",
                ["SUPABASE_SECRET_KEY"] = fakeModernKey
            })
            .Build();
        var modernRepo = new PostgrestFamilyAccountRepository(new HttpClient(modernHandler), modernConfig);
        await modernRepo.GetUserMembershipAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(capturedModern);
        Assert.Equal(fakeModernKey, capturedModern!.Headers.GetValues("apikey").Single());
        Assert.Null(capturedModern.Headers.Authorization);

        HttpRequestMessage? capturedLegacy = null;
        var legacyHandler = new StubHttpMessageHandler(req =>
        {
            capturedLegacy = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        });
        const string fakeLegacyJwt = "eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoic2VydmljZV9yb2xlIn0.firmaficticia";
        var legacyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SUPABASE_URL"] = "https://ejemplo.supabase.co",
                ["SUPABASE_SECRET_KEY"] = fakeLegacyJwt
            })
            .Build();
        var legacyRepo = new PostgrestFamilyAccountRepository(new HttpClient(legacyHandler), legacyConfig);
        await legacyRepo.GetUserMembershipAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(capturedLegacy);
        Assert.Equal(fakeLegacyJwt, capturedLegacy!.Headers.GetValues("apikey").Single());
        Assert.Equal("Bearer", capturedLegacy.Headers.Authorization?.Scheme);
        Assert.Equal(fakeLegacyJwt, capturedLegacy.Headers.Authorization?.Parameter);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
