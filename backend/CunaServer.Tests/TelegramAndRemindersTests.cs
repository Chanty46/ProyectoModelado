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
using Microsoft.Extensions.Logging.Abstractions;

namespace CunaServer.Tests;

public class TelegramAndRemindersTests
{
    private static readonly Guid PrincipalUserId = Guid.Parse("33333333-3333-3333-3333-333333333301");
    private static readonly Guid SecondaryUserId = Guid.Parse("33333333-3333-3333-3333-333333333302");
    private static readonly Guid ThirdUserId = Guid.Parse("33333333-3333-3333-3333-333333333303");

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
        Assert.Contains(tgTransport.GetSentMessages(), m => m.ChatId == 10001 && m.Text.Contains("Alarma crítica de retiro"));
        Assert.Contains(tgTransport.GetSentMessages(), m => m.ChatId == 10002 && m.Text.Contains("Alarma crítica de retiro"));

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
        Assert.Contains(tgTransport.GetSentMessages(), m => m.ChatId == 20001 && m.Text.Contains("Recordatorio de alimentación (RF18)"));
    }

    [Fact]
    public async Task RF38_DeepLinkStart_And_StatusTestEndpoints_WorkEndToEnd()
    {
        await using var factory = CreateTestFactory(out _, out _, out var tgTransport);
        using var client = factory.CreateClient();
        var botService = factory.Services.GetRequiredService<TelegramBotService>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia DeepLink", "Mamá", "F1"));

        var statusBefore = await client.GetFromJsonAsync<TelegramBotStatusResponse>("/api/telegram/status");
        Assert.NotNull(statusBefore);
        Assert.False(statusBefore!.UserLinked);
        Assert.Equal("inmemory", statusBefore.Mode);

        // Enviar prueba antes de vincular -> 400 BadRequest
        var unlinkedTestResp = await client.PostAsync("/api/telegram/test", null);
        Assert.Equal(HttpStatusCode.BadRequest, unlinkedTestResp.StatusCode);

        var codeResp = await client.PostAsync("/api/telegram/link-code", null);
        var code = await codeResp.Content.ReadFromJsonAsync<GenerateTelegramCodeResponse>();
        Assert.NotNull(code);
        Assert.Contains("https://t.me/", code!.DeepLinkUrl);
        Assert.Contains(code.Codigo, code.DeepLinkUrl);

        // Deep-link /start CODIGO vincula automáticamente sin exigir segundo comando
        var now = DateTimeOffset.UtcNow;
        await botService.ProcessInboundCommandAsync(990001234, $"/start {code.Codigo}", now, CancellationToken.None);
        Assert.Contains(tgTransport.GetSentMessages(), m =>
            m.ChatId == 990001234 && m.Text.Contains("vinculado correctamente"));

        await botService.ProcessInboundCommandAsync(990001234, "/mi_chat", now, CancellationToken.None);
        Assert.Contains(tgTransport.GetSentMessages(), m =>
            m.ChatId == 990001234 && m.Text.Contains("990001234") && m.Text.Contains("principal@cuna.local"));

        var testResp = await client.PostAsync("/api/telegram/test", null);
        Assert.Equal(HttpStatusCode.OK, testResp.StatusCode);
        var testBody = await testResp.Content.ReadFromJsonAsync<TelegramTestMessageResponse>();
        Assert.NotNull(testBody);
        Assert.True(testBody!.Sent);
        Assert.Equal(990001234, testBody.ChatId);
        Assert.Contains(tgTransport.GetSentMessages(), m =>
            m.ChatId == 990001234 && m.Text.Contains("[PRUEBA]"));
    }

    [Fact]
    public async Task HttpTelegramBotTransport_HandlesGetMeUpdatesSendMessage_And429Retry()
    {
        var sendAttempts = 0;
        var webhookDeleted = false;
        var seenOffsets = new List<string>();
        using var handler = new StubHttpMessageHandler((req, _) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/deleteWebhook", StringComparison.Ordinal))
            {
                webhookDeleted = true;
                return JsonResponse(HttpStatusCode.OK, """{"ok":true,"result":true}""");
            }

            if (path.EndsWith("/getMe", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, """{"ok":true,"result":{"id":1001,"is_bot":true,"username":"CunasInteligentes_Modelado_Bot"}}""");
            }

            if (path.EndsWith("/getUpdates", StringComparison.Ordinal))
            {
                seenOffsets.Add(req.RequestUri?.Query ?? "");
                if (seenOffsets.Count == 1)
                {
                    // Incluye un update no textual (41) antes del comando (42) y otro no textual después (43)
                    return JsonResponse(HttpStatusCode.OK, """
                        {
                          "ok": true,
                          "result": [
                            {"update_id": 41, "message": {"chat": {"id": 990001234}}},
                            {"update_id": 42, "message": {"chat": {"id": 990001234}, "text": "/estado"}},
                            {"update_id": 43, "message": {"chat": {"id": 990001234}}}
                          ]
                        }
                        """);
                }
                return JsonResponse(HttpStatusCode.OK, """{"ok":true,"result":[]}""");
            }

            if (path.EndsWith("/sendMessage", StringComparison.Ordinal))
            {
                sendAttempts++;
                if (sendAttempts == 1)
                {
                    return JsonResponse((HttpStatusCode)429, """{"ok":false,"parameters":{"retry_after":1}}""");
                }
                return JsonResponse(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":7}}""");
            }

            return JsonResponse(HttpStatusCode.NotFound, """{"ok":false}""");
        });

        using var http = new HttpClient(handler);
        var transport = new HttpTelegramBotTransport(
            http,
            "bot000000:TEST-TOKEN-NOT-REAL",
            NullLogger<HttpTelegramBotTransport>.Instance,
            "https://mock.telegram.local");

        var probe = await transport.ProbeBotAsync(CancellationToken.None);
        Assert.True(probe.TokenConfigured);
        Assert.True(probe.Reachable);
        Assert.Equal("CunasInteligentes_Modelado_Bot", probe.ResolvedUsername);

        var updates = await transport.GetUpdatesAsync(1, CancellationToken.None);
        Assert.True(webhookDeleted);
        Assert.Single(updates);
        Assert.Equal(990001234, updates[0].ChatId);
        Assert.Equal("/estado", updates[0].Text);

        // La segunda consulta debe pedir offset=44 (avanzando más allá del update no textual 43)
        await transport.GetUpdatesAsync(43, CancellationToken.None);
        Assert.Equal(2, seenOffsets.Count);
        Assert.Contains("offset=44", seenOffsets[1]);

        var delivered = await transport.SendMessageAsync(990001234, "Alerta prueba", CancellationToken.None);
        Assert.True(delivered);
        Assert.Equal(2, sendAttempts);
    }

    [Fact]
    public async Task RF39_StatusCommand_ConsolidatesMultiDeviceTelemetryIncludingBottle()
    {
        await using var factory = CreateTestFactory(
            out _,
            out var deviceRepo,
            out var tgTransport);
        using var client = factory.CreateClient();
        var botService = factory.Services.GetRequiredService<TelegramBotService>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia MultiTG", "Mamá", "M1"));
        await client.PostAsync("/api/pairing/window", null);

        var claimCentralResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-central-nuevo",
            "RECLAMO-CENTRAL-1001",
            null,
            "Cuna Consolidada"));
        var claimedCentral = await claimCentralResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();
        var cunaId = claimedCentral!.CunaId;

        var claimColchonResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-colchon-nuevo",
            "RECLAMO-COLCHON-1002",
            cunaId,
            null));
        var claimedColchon = await claimColchonResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();

        var claimBiberonResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-biberon-nuevo",
            "RECLAMO-BIBERON-1004",
            cunaId,
            null));
        var claimedBiberon = await claimBiberonResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();

        var t0 = DateTimeOffset.UtcNow.AddSeconds(-30);
        await deviceRepo.SaveTelemetryAsync(new TelemetryRecord(
            cunaId, claimedColchon!.DeviceRowId, t0, 3.450m, 0.031m, null, null, null, null, null, 1), CancellationToken.None);
        await deviceRepo.SaveTelemetryAsync(new TelemetryRecord(
            cunaId, claimedCentral.DeviceRowId, t0.AddSeconds(5), null, null, 22.4m, 48.0m, null, null, null, 1), CancellationToken.None);
        await deviceRepo.SaveTelemetryAsync(new TelemetryRecord(
            cunaId, claimedBiberon!.DeviceRowId, t0.AddSeconds(10), null, null, null, null, 185m, 36.9m, 92, 1), CancellationToken.None);

        var codeResp = await client.PostAsync("/api/telegram/link-code", null);
        var code = await codeResp.Content.ReadFromJsonAsync<GenerateTelegramCodeResponse>();
        var now = DateTimeOffset.UtcNow;
        await botService.ProcessInboundCommandAsync(45001, $"/vincular {code!.Codigo}", now, CancellationToken.None);

        await botService.ProcessInboundCommandAsync(45001, "/estado", now, CancellationToken.None);
        var msg = tgTransport.GetSentMessages().Last(m => m.ChatId == 45001);
        Assert.Contains("Cuna Consolidada", msg.Text);
        Assert.Contains("T=22.4 °C", msg.Text);
        Assert.Contains("H=48.0 %", msg.Text);
        Assert.Contains("Masa=3.450 kg", msg.Text);
        Assert.Contains("Biberón=36.9 °C / 185 g", msg.Text);
    }

    [Fact]
    public async Task RF38_FailedLinkAttempts_RateLimitAttackerChat_WithoutLockingOtherFamilies()
    {
        await using var factory = CreateTestFactory(
            out var accountRepo,
            out _,
            out var tgTransport);
        using var client = factory.CreateClient();
        var botService = factory.Services.GetRequiredService<TelegramBotService>();

        // Familia A (PrincipalUserId) genera su código
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia A", "Mamá A", "FA"));
        var codeAResp = await client.PostAsync("/api/telegram/link-code", null);
        var codeA = await codeAResp.Content.ReadFromJsonAsync<GenerateTelegramCodeResponse>();

        // Familia B (ThirdUserId) genera su código en paralelo
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-tercero");
        await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia B", "Papá B", "FB"));
        var codeBResp = await client.PostAsync("/api/telegram/link-code", null);
        var codeB = await codeBResp.Content.ReadFromJsonAsync<GenerateTelegramCodeResponse>();

        var now = DateTimeOffset.UtcNow;
        // Atacante (chat 888001) intenta 5 códigos inválidos -> bloqueado por chat_id
        for (var i = 0; i < TelegramBotService.MaxFailedLinkAttemptsPerChat; i++)
        {
            await botService.ProcessInboundCommandAsync(888001, $"/vincular INVALIDO{i}", now.AddSeconds(i), CancellationToken.None);
        }

        var blockedReply = tgTransport.GetSentMessages().Last(m => m.ChatId == 888001);
        Assert.Contains("Demasiados intentos", blockedReply.Text);

        // El atacante sigue bloqueado incluso si probara un código real dentro de la ventana de 10 min
        await botService.ProcessInboundCommandAsync(888001, $"/vincular {codeA!.Codigo}", now.AddSeconds(30), CancellationToken.None);
        var stillBlocked = tgTransport.GetSentMessages().Last(m => m.ChatId == 888001);
        Assert.Contains("Demasiados intentos", stillBlocked.Text);

        // Los códigos de Familia A y Familia B NO se vieron afectados y vinculan correctamente desde chats legítimos
        await botService.ProcessInboundCommandAsync(999001, $"/vincular {codeA.Codigo}", now.AddSeconds(35), CancellationToken.None);
        await botService.ProcessInboundCommandAsync(999002, $"/vincular {codeB!.Codigo}", now.AddSeconds(36), CancellationToken.None);

        var linkA = await accountRepo.GetTelegramLinkByUserAsync(PrincipalUserId, CancellationToken.None);
        var linkB = await accountRepo.GetTelegramLinkByUserAsync(ThirdUserId, CancellationToken.None);
        Assert.Equal(999001, linkA?.ChatId);
        Assert.Equal(999002, linkB?.ChatId);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request, cancellationToken));
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
                "token-tercero" => Task.FromResult<AuthenticatedIdentity?>(
                    new AuthenticatedIdentity(ThirdUserId, "tercero@cuna.local")),
                _ => Task.FromResult<AuthenticatedIdentity?>(null)
            };
        }
    }
}
