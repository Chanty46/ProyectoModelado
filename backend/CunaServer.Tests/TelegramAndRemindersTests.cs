using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CunaServer.Data;
using CunaServer.Services;
using CunaServer.Telegram;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CunaServer.Tests;

public class TelegramAndRemindersTests
{
    private static readonly Guid PrincipalUserId = Guid.Parse("33333333-3333-3333-3333-333333333301");
    private static readonly Guid SecondaryUserId = Guid.Parse("33333333-3333-3333-3333-333333333302");

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
                services.AddSingleton<IJwtTokenValidator>(new FakeTelegramJwtValidator());
            });
        });
    }

    [Fact]
    public async Task RF38_RF39_RF19_RF22_LinkPerUser_EstadoOnlyLinked_BroadcastAlerts_And_RevokeDeletesLink()
    {
        await using var factory = CreateTestFactory(
            out var accountRepo,
            out _,
            out var tgTransport);
        using var client = factory.CreateClient();
        var botService = factory.Services.GetRequiredService<TelegramBotService>();

        // 1. Bootstrap familia con Principal, crear cuna e invitar Secundario
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia Telegram", "Mamá", "F1"));

        await client.PostAsync("/api/pairing/window", null);
        var claimResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-central-nuevo",
            "RECLAMO-CENTRAL-1001",
            null,
            "Cuna Bebé"));
        var claimed = await claimResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();
        var cunaId = claimed!.CunaId;

        var invResp = await client.PostAsync("/api/family/invitations", null);
        var inv = await invResp.Content.ReadFromJsonAsync<CreateInvitationResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-secundario");
        await client.PostAsJsonAsync("/api/family/invitations/accept", new AcceptInvitationRequest(inv!.Token, "Papá", "M1"));

        // 2. RF39: Chat no vinculado (99999) envía /estado -> rechazado sin filtrar datos de la cuna
        var now = DateTimeOffset.UtcNow;
        await botService.ProcessInboundCommandAsync(99999, "/estado", now, CancellationToken.None);
        var msgUnlinked = tgTransport.GetSentMessages().Last(m => m.ChatId == 99999);
        Assert.Contains("Acceso denegado", msgUnlinked.Text);
        Assert.DoesNotContain("Cuna Bebé", msgUnlinked.Text);

        // 3. RF38: Principal (chat 10001) y Secundario (chat 10002) generan sus códigos de 10 min y vinculan
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        var codeRespP = await client.PostAsync("/api/telegram/link-code", null);
        Assert.Equal(HttpStatusCode.Created, codeRespP.StatusCode);
        var codeP = await codeRespP.Content.ReadFromJsonAsync<GenerateTelegramCodeResponse>();
        Assert.NotNull(codeP);

        // Verificar que en BD solo se guardó el hash SHA-256
        var storedLinkP = await accountRepo.GetTelegramLinkByUserAsync(PrincipalUserId, CancellationToken.None);
        Assert.NotNull(storedLinkP);
        Assert.Equal(CryptoTokenHelper.ComputeSha256Hex(codeP.Codigo), storedLinkP.HashCodigo);

        await botService.ProcessInboundCommandAsync(10001, $"/vincular {codeP.Codigo}", now, CancellationToken.None);

        // Reutilizar el mismo código falla (un solo uso)
        await botService.ProcessInboundCommandAsync(77777, $"/vincular {codeP.Codigo}", now, CancellationToken.None);
        Assert.Contains("inválido", tgTransport.GetSentMessages().Last(m => m.ChatId == 77777).Text);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-secundario");
        var codeRespS = await client.PostAsync("/api/telegram/link-code", null);
        var codeS = await codeRespS.Content.ReadFromJsonAsync<GenerateTelegramCodeResponse>();
        await botService.ProcessInboundCommandAsync(10002, $"/vincular {codeS!.Codigo}", now, CancellationToken.None);

        // 4. RF39: Chat vinculado (10001) consulta /estado -> responde en < 2 s con estado de 'Cuna Bebé'
        var sw = Stopwatch.StartNew();
        await botService.ProcessInboundCommandAsync(10001, "/estado", now, CancellationToken.None);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2000);
        var estadoMsg = tgTransport.GetSentMessages().Last(m => m.ChatId == 10001);
        Assert.Contains("Cuna Bebé", estadoMsg.Text);
        Assert.Contains("Temperatura", estadoMsg.Text);
        Assert.Contains("Última toma", estadoMsg.Text);
        Assert.DoesNotContain("RF39", estadoMsg.Text);

        // 5. RF19 / D6: Alerta en la cuna llega a TODOS los miembros vinculados de la familia (10001 y 10002)
        var delivered = await botService.DispatchAlertForCribAsync(
            cunaId,
            new EventAlertRecord(
                Guid.NewGuid(),
                cunaId,
                Guid.Empty,
                "removal_alarm",
                "critica",
                "Alarma crítica de retiro",
                null,
                now),
            CancellationToken.None);
        Assert.Equal(2, delivered);
        Assert.Contains(tgTransport.GetSentMessages(), m => m.ChatId == 10001 && m.Text.Contains("el bebé salió de la cuna"));
        Assert.Contains(tgTransport.GetSentMessages(), m => m.ChatId == 10002 && m.Text.Contains("el bebé salió de la cuna"));

        // 6. RF22 + D6: Principal revoca al Secundario -> borra su vínculo de Telegram al instante
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        var revokeResp = await client.DeleteAsync($"/api/family/members/{SecondaryUserId}");
        Assert.Equal(HttpStatusCode.OK, revokeResp.StatusCode);

        var secLinkAfterRevoke = await accountRepo.GetTelegramLinkByUserAsync(SecondaryUserId, CancellationToken.None);
        Assert.Null(secLinkAfterRevoke);

        await botService.ProcessInboundCommandAsync(10002, "/estado", now, CancellationToken.None);
        var revokedEstadoMsg = tgTransport.GetSentMessages().Last(m => m.ChatId == 10002);
        Assert.Contains("Acceso denegado", revokedEstadoMsg.Text);
    }

    [Fact]
    public async Task RF18_FeedingReminders_ValidateRange_And_DispatchWhenIntervalElapsed()
    {
        await using var factory = CreateTestFactory(
            out _,
            out _,
            out var tgTransport);
        using var client = factory.CreateClient();
        var botService = factory.Services.GetRequiredService<TelegramBotService>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia Tomas", "Mamá", "F1"));

        var codeResp = await client.PostAsync("/api/telegram/link-code", null);
        var code = await codeResp.Content.ReadFromJsonAsync<GenerateTelegramCodeResponse>();
        var t0 = DateTimeOffset.UtcNow;
        await botService.ProcessInboundCommandAsync(20001, $"/vincular {code!.Codigo}", t0, CancellationToken.None);

        // Intervalo fuera de rango (0 o 9 h) -> 400 Bad Request
        var badRemResp = await client.PutAsJsonAsync("/api/reminders", new UpdateFeedingReminderRequest(9, true));
        Assert.Equal(HttpStatusCode.BadRequest, badRemResp.StatusCode);

        // Configurar intervalo de 2 horas
        var okRemResp = await client.PutAsJsonAsync("/api/reminders", new UpdateFeedingReminderRequest(2, true));
        Assert.Equal(HttpStatusCode.OK, okRemResp.StatusCode);

        // A la 1 hora aún no dispara
        var sentAfter1h = await botService.EvaluateFeedingRemindersAsync(t0.AddHours(1), CancellationToken.None);
        Assert.Equal(0, sentAfter1h);

        // A las 2 horas y 1 minuto dispara el recordatorio por Telegram
        var sentAfter2h = await botService.EvaluateFeedingRemindersAsync(t0.AddHours(2).AddMinutes(1), CancellationToken.None);
        Assert.Equal(1, sentAfter2h);
        Assert.Contains(tgTransport.GetSentMessages(), m => m.ChatId == 20001 && m.Text.Contains("Recordatorio de alimentación") && m.Text.Contains("2 horas"));
    }

    private sealed class FakeTelegramJwtValidator : IJwtTokenValidator
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
                "token-secundario" => Task.FromResult<AuthenticatedIdentity?>(
                    new AuthenticatedIdentity(SecondaryUserId, "secundario@cuna.local")),
                _ => Task.FromResult<AuthenticatedIdentity?>(null)
            };
        }
    }
}
