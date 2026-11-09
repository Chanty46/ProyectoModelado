using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using CunaServer.Data;
using CunaServer.Services;
using CunaServer.Tcp;
using CunaServer.Telegram;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CunaServer.Tests;

/// <summary>
/// Pruebas integrales de extremo a extremo (E2E) y auditoría automatizada de la lista
/// de verificación de <c>docs/SECURITY.md</c> para el cierre del proyecto (Hito M10).
/// </summary>
public sealed class EndToEndAndSecurityChecklistTests
{
    private static readonly Guid PrincipalUserId = Guid.Parse("55555555-5555-5555-5555-555555555501");

    private static WebApplicationFactory<Program> CreateTestFactory(
        out InMemoryFamilyAccountRepository accountRepo,
        out InMemoryDeviceTelemetryRepository deviceRepo,
        out InMemoryTelegramBotTransport telegramTransport)
    {
        var accRepo = new InMemoryFamilyAccountRepository();
        var devRepo = new InMemoryDeviceTelemetryRepository();
        var tgTransport = new InMemoryTelegramBotTransport();
        var liveOptions = new CribLiveControlOptions
        {
            RemovalGraceWindow = TimeSpan.FromMilliseconds(120),
            AttentionModeDuration = TimeSpan.FromSeconds(10),
            WatchdogTimeout = TimeSpan.FromSeconds(60)
        };
        accountRepo = accRepo;
        deviceRepo = devRepo;
        telegramTransport = tgTransport;

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IFamilyAccountRepository>(accRepo);
                services.AddSingleton<IDeviceTelemetryRepository>(devRepo);
                services.AddSingleton<ITelegramBotTransport>(tgTransport);
                services.AddSingleton<IJwtTokenValidator>(new FakeE2EJwtValidator());
                services.AddSingleton(liveOptions);
            });
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio (AGENTS.md).");
    }

    [Fact]
    public void SecurityChecklist_RepositoryHasNoSecretsAndProtectsEnvAndSecretsPy()
    {
        var root = FindRepositoryRoot();
        var gitignore = File.ReadAllText(Path.Combine(root, ".gitignore"));
        Assert.Contains(".env", gitignore);
        Assert.Contains("secrets.py", gitignore);

        // Ningún archivo real .env ni secrets.py debe existir en el árbol de trabajo
        Assert.False(File.Exists(Path.Combine(root, "backend", ".env")));
        Assert.False(File.Exists(Path.Combine(root, "frontend", ".env")));
        Assert.False(File.Exists(Path.Combine(root, "firmware", "secrets.py")));

        // Escaneo de patrones prohibidos en archivos fuente del repositorio
        var forbiddenPattern = "(sb_secret_[A-Za-z0-9]{10}|GOCSPX-[A-Za-z0-9]{5}|" + "AAGel" + "Nrz|" + "Pr0y" + "3ct0)";
        var forbiddenRegex = new Regex(forbiddenPattern, RegexOptions.IgnoreCase);
        var sourceDirs = new[] { "backend/CunaServer", "frontend/src", "simulators", "firmware", "supabase", "docs" };

        foreach (var relDir in sourceDirs)
        {
            var fullDir = Path.Combine(root, relDir);
            if (!Directory.Exists(fullDir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(fullDir, "*.*", SearchOption.AllDirectories))
            {
                if (file.Contains("/bin/") || file.Contains("/obj/") || file.Contains("/node_modules/") || file.Contains("/dist/"))
                {
                    continue;
                }

                var content = File.ReadAllText(file);
                Assert.False(
                    forbiddenRegex.IsMatch(content),
                    $"Se detectó un patrón de credencial prohibido en el archivo: {Path.GetRelativePath(root, file)}");
            }
        }
    }

    [Fact]
    public async Task EndToEndLifecycle_OnboardingPairingLiveAlarmTelegramFeedingAndExport_CompletesSecurely()
    {
        await using var factory = CreateTestFactory(out var accountRepo, out var deviceRepo, out var tgTransport);
        using var client = factory.CreateClient();
        var tcpServer = factory.Services.GetRequiredService<DeviceTcpServer>();
        var tcpPort = await tcpServer.WaitForBoundPortAsync();
        var botService = factory.Services.GetRequiredService<TelegramBotService>();

        // 1. M2: Bootstrap del Cuidador Principal (RF01, RF23)
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal-e2e");
        var bootResp = await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia E2E M10", "Mamá E2E", "F1"));
        Assert.Equal(HttpStatusCode.Created, bootResp.StatusCode);
        var bootCtx = await bootResp.Content.ReadFromJsonAsync<AccountContextResponse>();
        Assert.NotNull(bootCtx);
        var profileId = bootCtx!.Perfiles[0].Id;

        // 2. M4: Abrir ventana de 10 min y reclamar nodos Central + Colchón y registrar Bebé (RF24, RF25, RF27)
        var winResp = await client.PostAsync("/api/pairing/window", null);
        Assert.Equal(HttpStatusCode.OK, winResp.StatusCode);

        var claimCentralResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-central-nuevo",
            "RECLAMO-CENTRAL-1001",
            null,
            "Cuna Principal E2E"));
        Assert.Equal(HttpStatusCode.Created, claimCentralResp.StatusCode);
        var claimedCentral = await claimCentralResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();
        Assert.NotNull(claimedCentral);
        var cunaId = claimedCentral!.CunaId;

        var claimColchonResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-colchon-nuevo",
            "RECLAMO-COLCHON-1002",
            cunaId,
            null));
        Assert.Equal(HttpStatusCode.Created, claimColchonResp.StatusCode);
        var claimedColchon = await claimColchonResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();
        Assert.NotNull(claimedColchon);

        var babyResp = await client.PostAsJsonAsync("/api/babies", new UpsertBabyRequest(
            cunaId,
            "Lucía E2E",
            new DateOnly(2026, 4, 15),
            "Perfil integral de cierre M10"));
        Assert.Equal(HttpStatusCode.Created, babyResp.StatusCode);
        var baby = await babyResp.Content.ReadFromJsonAsync<BabyRecord>();
        Assert.NotNull(baby);

        // 3. M6: Vincular Telegram con código de un solo uso de 10 min (RF38)
        var tgCodeResp = await client.PostAsync("/api/telegram/link-code", null);
        Assert.Equal(HttpStatusCode.Created, tgCodeResp.StatusCode);
        var tgCode = await tgCodeResp.Content.ReadFromJsonAsync<GenerateTelegramCodeResponse>();
        Assert.NotNull(tgCode);

        var now = DateTimeOffset.UtcNow;
        await botService.ProcessInboundCommandAsync(77001122L, $"/vincular {tgCode!.Codigo}", now, CancellationToken.None);
        var linkReply = tgTransport.GetSentMessages().Last(m => m.ChatId == 77001122L);
        Assert.Contains("vinculad", linkReply.Text, StringComparison.OrdinalIgnoreCase);

        // 4. M3 + M5: Conectar Colchón por TCP y emitir 'removal' dejando vencer la ventana de gracia -> Alarma (RF04, RF19)
        using var colchonTcp = new TcpClient();
        await colchonTcp.ConnectAsync("127.0.0.1", tcpPort);
        await using var colchonStream = colchonTcp.GetStream();
        using var colchonReader = new StreamReader(colchonStream, new UTF8Encoding(false), leaveOpen: true);
        await using var colchonWriter = new StreamWriter(colchonStream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        await colchonWriter.WriteLineAsync(
            $$"""{"v":1,"t":"hello","seq":1,"ts":1762560000,"d":{"device_id":"pico-colchon-nuevo","device_type":"colchon","token":"{{claimedColchon!.DeviceTokenOnce}}","fw":"1.0.0"} }""");
        var colchonAck = await colchonReader.ReadLineAsync();
        Assert.Contains("\"t\":\"hello_ack\"", colchonAck);

        await colchonWriter.WriteLineAsync(
            """{"v":1,"t":"event","seq":1,"ts":1762560010,"d":{"kind":"removal","severity":"critica","msg":"Retiro E2E"} }""");
        _ = await colchonReader.ReadLineAsync();

        await Task.Delay(250);

        var cribAfterRemoval = await deviceRepo.GetCribByIdAsync(cunaId, CancellationToken.None);
        Assert.NotNull(cribAfterRemoval);
        Assert.Equal("alarma", cribAfterRemoval!.ModoOperacion);

        await botService.ProcessInboundCommandAsync(77001122L, "/estado", now.AddSeconds(5), CancellationToken.None);
        var statusReply = tgTransport.GetSentMessages().Last(m => m.ChatId == 77001122L);
        Assert.Contains("alarma", statusReply.Text, StringComparison.OrdinalIgnoreCase);

        // 5. M7: Sesión de alimentación (m_inicial = 216.3 g, m_final = 113.3 g -> 100.0 ml, RF15-RF17, RF37) + Exportación PDF (RF32)
        var startFeedResp = await client.PostAsJsonAsync("/api/feeding/sessions/start", new StartFeedingSessionRequest(
            baby!.Id,
            216.3m,
            37.0m,
            profileId));
        Assert.Equal(HttpStatusCode.Created, startFeedResp.StatusCode);
        var startedFeed = await startFeedResp.Content.ReadFromJsonAsync<FeedingSessionRecord>();
        Assert.NotNull(startedFeed);

        var finishFeedResp = await client.PostAsJsonAsync($"/api/feeding/sessions/{startedFeed!.Id}/finish", new FinishFeedingSessionRequest(
            113.3m,
            profileId));
        Assert.Equal(HttpStatusCode.OK, finishFeedResp.StatusCode);
        var finishedFeed = await finishFeedResp.Content.ReadFromJsonAsync<FeedingSessionRecord>();
        Assert.NotNull(finishedFeed);
        Assert.Equal(100.0m, finishedFeed!.MlConsumidos);

        var pdfExportResp = await client.PostAsJsonAsync($"/api/babies/{baby.Id}/history/export", new ExportHistoryRequest("pdf", null, null, profileId));
        Assert.Equal(HttpStatusCode.OK, pdfExportResp.StatusCode);
        var pdfExport = await pdfExportResp.Content.ReadFromJsonAsync<ExportHistoryResponse>();
        Assert.NotNull(pdfExport);
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(Convert.FromBase64String(pdfExport!.ContentBase64)));

        // Verificar registro en bitacora_seguridad (RF32 / SECURITY.md)
        var secAudits = await accountRepo.ListSecurityEventsAsync(bootCtx.Familia!.Id, CancellationToken.None);
        Assert.Contains(secAudits, e => e.Evento == "exportacion_historial_rf32");
    }

    private sealed class FakeE2EJwtValidator : IJwtTokenValidator
    {
        public Task<AuthenticatedIdentity?> ValidateBearerTokenAsync(string? authorizationHeader, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(authorizationHeader) || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<AuthenticatedIdentity?>(null);
            }

            var token = authorizationHeader["Bearer ".Length..].Trim();
            return token switch
            {
                "token-principal-e2e" => Task.FromResult<AuthenticatedIdentity?>(new AuthenticatedIdentity(PrincipalUserId, "mama.e2e@cuna.test")),
                _ => Task.FromResult<AuthenticatedIdentity?>(null)
            };
        }
    }
}
