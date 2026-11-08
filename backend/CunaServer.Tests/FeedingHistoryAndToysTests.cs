using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using CunaServer.Data;
using CunaServer.Services;
using CunaServer.Telegram;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CunaServer.Tests;

public class FeedingHistoryAndToysTests
{
    private static readonly Guid PrincipalUserId = Guid.Parse("44444444-4444-4444-4444-444444444401");

    private WebApplicationFactory<Program> CreateTestFactory(
        out InMemoryFamilyAccountRepository accountRepo,
        out InMemoryDeviceTelemetryRepository deviceRepo,
        out InMemoryTelegramBotTransport telegramTransport)
    {
        var accRepo = new InMemoryFamilyAccountRepository();
        var devRepo = new InMemoryDeviceTelemetryRepository();
        var tgTransport = new InMemoryTelegramBotTransport();
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
                services.AddSingleton<IJwtTokenValidator>(new FakeM7JwtValidator());
            });
        });
    }

    [Fact]
    public async Task RF37_RF16_RF17_FeedingSession_MinMass_TempAlert_VolumeCalculation_And_HistoryExport_RF30_RF32()
    {
        await using var factory = CreateTestFactory(out var accountRepo, out var deviceRepo, out _);
        using var client = factory.CreateClient();

        // 1. Bootstrap familia, reclamar Central + Juguete y registrar Bebé
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        var bootResp = await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia M7", "Mamá", "F1"));
        var bootCtx = await bootResp.Content.ReadFromJsonAsync<AccountContextResponse>();
        var profileId = bootCtx!.Perfiles[0].Id;

        await client.PostAsync("/api/pairing/window", null);
        var claimCentralResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-central-nuevo",
            "RECLAMO-CENTRAL-1001",
            null,
            "Cuna Nutrición"));
        var claimedCentral = await claimCentralResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();
        var cunaId = claimedCentral!.CunaId;

        var claimToyResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-juguete-nuevo",
            "RECLAMO-JUGUETE-1005",
            cunaId,
            null));
        var claimedToy = await claimToyResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();

        // Simular batería baja en el juguete de botones (< 20 %, RF36)
        await deviceRepo.RecordHeartbeatAsync(claimedToy!.DeviceRowId, cunaId, DeviceTypes.Juguete, 15, "1.0.0", DateTimeOffset.UtcNow, CancellationToken.None);

        var babyResp = await client.PostAsJsonAsync("/api/babies", new UpsertBabyRequest(
            cunaId,
            "Bebé Mateo",
            new DateOnly(2026, 4, 20),
            null));
        var baby = await babyResp.Content.ReadFromJsonAsync<BabyRecord>();
        Assert.NotNull(baby);

        // 2. RF37: Masa inicial < 20 g se rechaza con 400 Bad Request
        var lowMassResp = await client.PostAsJsonAsync("/api/feeding/sessions/start", new StartFeedingSessionRequest(
            baby.Id,
            15.0m,
            36.8m,
            profileId));
        Assert.Equal(HttpStatusCode.BadRequest, lowMassResp.StatusCode);

        // 3. RF37 + RF16: Iniciar sesión con 216.3 g y temperatura fuera de rango (39.4 °C > 38.5 °C)
        var startResp = await client.PostAsJsonAsync("/api/feeding/sessions/start", new StartFeedingSessionRequest(
            baby.Id,
            216.3m,
            39.4m,
            profileId));
        Assert.Equal(HttpStatusCode.Created, startResp.StatusCode);
        var startedSession = await startResp.Content.ReadFromJsonAsync<FeedingSessionRecord>();
        Assert.NotNull(startedSession);
        Assert.True(startedSession.AlertaTemperatura);

        // 4. RF17: Finalizar con 113.3 g -> ml = (216.3 - 113.3) / 1.03 = 100.0 ml exactos
        var finishResp = await client.PostAsJsonAsync($"/api/feeding/sessions/{startedSession.Id}/finish", new FinishFeedingSessionRequest(
            113.3m,
            profileId));
        Assert.Equal(HttpStatusCode.OK, finishResp.StatusCode);
        var finishedSession = await finishResp.Content.ReadFromJsonAsync<FeedingSessionRecord>();
        Assert.NotNull(finishedSession);
        Assert.Equal(100.0m, finishedSession.MlConsumidos);

        // 5. RF30: Registrar sesión de sueño y verificar consulta < 3 s
        var sleepStart = DateTimeOffset.UtcNow.AddHours(-2);
        var sleepEnd = DateTimeOffset.UtcNow;
        var createSleepResp = await client.PostAsJsonAsync($"/api/babies/{baby.Id}/history/sleep", new CreateSleepSessionRequest(
            cunaId,
            "siesta",
            sleepStart,
            sleepEnd,
            0.035m,
            1,
            profileId));
        Assert.Equal(HttpStatusCode.Created, createSleepResp.StatusCode);

        var swQuery = Stopwatch.StartNew();
        var sleepList = await client.GetFromJsonAsync<IReadOnlyList<SleepSessionRecord>>($"/api/babies/{baby.Id}/history/sleep");
        var feedList = await client.GetFromJsonAsync<IReadOnlyList<FeedingSessionRecord>>($"/api/babies/{baby.Id}/history/feeding");
        swQuery.Stop();
        Assert.True(swQuery.ElapsedMilliseconds < 3000);
        Assert.Single(sleepList!);
        Assert.Single(feedList!);

        // 6. RF12 + RF13 + RF33 + RF36: Canciones, Móvil de 9 LEDs y aviso de batería baja del juguete
        var songs = await client.GetFromJsonAsync<IReadOnlyList<SongCatalogItem>>($"/api/cribs/{cunaId}/songs");
        Assert.NotEmpty(songs!);

        var playSongResp = await client.PostAsJsonAsync($"/api/cribs/{cunaId}/songs/play", new PlaySongRequest("estrellita", "play", profileId));
        Assert.Equal(HttpStatusCode.OK, playSongResp.StatusCode);

        var playMobileResp = await client.PostAsJsonAsync($"/api/cribs/{cunaId}/mobile/play", new PlayMobileRequest("ola_circular", "media", 60, profileId));
        Assert.Equal(HttpStatusCode.Created, playMobileResp.StatusCode);

        var toysStatus = await client.GetFromJsonAsync<ToysAndPeripheralsStatusResponse>($"/api/cribs/{cunaId}/toys");
        Assert.NotNull(toysStatus);
        Assert.Single(toysStatus.HistorialUso);
        Assert.True(toysStatus.AlertaBateriaBajaJuguete);
        Assert.Equal(15, toysStatus.BateriaJuguetePct);

        // 7. RF32: Exportar historial en CSV y PDF (< 10 s) y verificar auditoría en bitácora y bitacora_seguridad
        var csvExportResp = await client.PostAsJsonAsync($"/api/babies/{baby.Id}/history/export", new ExportHistoryRequest("csv", null, null, profileId));
        Assert.Equal(HttpStatusCode.OK, csvExportResp.StatusCode);
        var csvExport = await csvExportResp.Content.ReadFromJsonAsync<ExportHistoryResponse>();
        Assert.NotNull(csvExport);
        Assert.True(csvExport.ElapsedMs < 10000);
        var csvText = Encoding.UTF8.GetString(Convert.FromBase64String(csvExport.ContentBase64));
        Assert.Contains("ml=100.0", csvText);

        var pdfExportResp = await client.PostAsJsonAsync($"/api/babies/{baby.Id}/history/export", new ExportHistoryRequest("pdf", null, null, profileId));
        Assert.Equal(HttpStatusCode.OK, pdfExportResp.StatusCode);
        var pdfExport = await pdfExportResp.Content.ReadFromJsonAsync<ExportHistoryResponse>();
        Assert.NotNull(pdfExport);
        Assert.True(pdfExport.ElapsedMs < 10000);
        var pdfText = Encoding.ASCII.GetString(Convert.FromBase64String(pdfExport.ContentBase64));
        Assert.StartsWith("%PDF-1.4", pdfText);

        var secAudit = await accountRepo.ListSecurityEventsAsync(bootCtx.Familia!.Id, CancellationToken.None);
        Assert.Contains(secAudit, e => e.Evento == "exportacion_historial_rf32");

        var caregiverAudit = await deviceRepo.ListCaregiverActionsAsync(bootCtx.Familia.Id, CancellationToken.None);
        Assert.Contains(caregiverAudit, a => a.Accion == "exportar_historial_rf32" && a.PerfilId == profileId);
    }

    [Fact]
    public async Task RF32_CsvExport_SanitizesFormulaInjection()
    {
        await using var factory = CreateTestFactory(out _, out _, out _);
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        var bootResp = await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia Formulas", "Papá", "P1"));
        var bootCtx = await bootResp.Content.ReadFromJsonAsync<AccountContextResponse>();
        var profileId = bootCtx!.Perfiles[0].Id;

        await client.PostAsync("/api/pairing/window", null);
        var claimCentralResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-central-nuevo",
            "RECLAMO-CENTRAL-1001",
            null,
            "Cuna Seguridad"));
        var claimedCentral = await claimCentralResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();
        var cunaId = claimedCentral!.CunaId;

        var babyResp = await client.PostAsJsonAsync("/api/babies", new UpsertBabyRequest(
            cunaId,
            "=cmd|' /C calc'!A0",
            new DateOnly(2026, 1, 1),
            "Notas de prueba"));
        Assert.Equal(HttpStatusCode.Created, babyResp.StatusCode);
        var baby = await babyResp.Content.ReadFromJsonAsync<BabyRecord>();

        // Crear una sesión de sueño para incluir al bebé en las líneas del CSV
        var sleepResp = await client.PostAsJsonAsync($"/api/babies/{baby!.Id}/history/sleep", new CreateSleepSessionRequest(
            cunaId,
            "siesta",
            DateTimeOffset.UtcNow.AddMinutes(-30),
            DateTimeOffset.UtcNow,
            0.05m,
            0,
            profileId));
        Assert.Equal(HttpStatusCode.Created, sleepResp.StatusCode);

        var exportResp = await client.PostAsJsonAsync($"/api/babies/{baby.Id}/history/export", new ExportHistoryRequest("csv", null, null, profileId));
        Assert.Equal(HttpStatusCode.OK, exportResp.StatusCode);
        var export = await exportResp.Content.ReadFromJsonAsync<ExportHistoryResponse>();
        var csv = Encoding.UTF8.GetString(Convert.FromBase64String(export!.ContentBase64));

        Assert.Contains("bebe=\"'=cmd|' /C calc'!A0\"", csv);
    }

    private sealed class FakeM7JwtValidator : IJwtTokenValidator
    {
        public Task<AuthenticatedIdentity?> ValidateBearerTokenAsync(string? authorizationHeader, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(authorizationHeader) || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<AuthenticatedIdentity?>(null);
            }

            var bearerToken = authorizationHeader["Bearer ".Length..].Trim();
            return bearerToken switch
            {
                "token-principal" => Task.FromResult<AuthenticatedIdentity?>(
                    new AuthenticatedIdentity(PrincipalUserId, "principal@cuna.local")),
                _ => Task.FromResult<AuthenticatedIdentity?>(null)
            };
        }
    }
}
