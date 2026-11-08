using System.Text.RegularExpressions;

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
        Assert.Contains("ALTER PUBLICATION supabase_realtime ADD TABLE", sql);
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
}
