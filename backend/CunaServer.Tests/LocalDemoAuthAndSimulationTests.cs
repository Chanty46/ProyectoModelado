using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CunaServer.Data;
using CunaServer.Services;
using CunaServer.Telegram;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CunaServer.Tests;

public sealed class LocalDemoAuthAndSimulationTests
{
    private static WebApplicationFactory<Program> CreateLocalDemoFactory(
        out InMemoryTelegramBotTransport tgTransport,
        bool localAuthEnabled = true)
    {
        var transport = new InMemoryTelegramBotTransport();
        tgTransport = transport;

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SUPABASE_SECRET_KEY"] = "",
                    ["TELEGRAM_BOT_TOKEN"] = "",
                    ["LOCAL_DEMO_AUTH_ENABLED"] = localAuthEnabled ? "true" : "false",
                    ["TCP_SERVER_ENABLED"] = "false"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ITelegramBotTransport>(transport);
            });
        });
    }

    [Fact]
    public async Task LocalLogin_ValidatesPadre123_SeedsDemoWorkspace_AndRunsSimulations()
    {
        await using var factory = CreateLocalDemoFactory(out var tgTransport);
        using var client = factory.CreateClient();

        var modeResp = await client.GetFromJsonAsync<AuthModeResponse>("/api/auth/mode");
        Assert.NotNull(modeResp);
        Assert.True(modeResp!.LocalLoginEnabled);

        var badLogin = await client.PostAsJsonAsync("/api/auth/local-login", new LocalLoginRequest("Padre", "wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);

        var loginResp = await client.PostAsJsonAsync("/api/auth/local-login", new LocalLoginRequest("Padre", "123"));
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var login = await loginResp.Content.ReadFromJsonAsync<LocalLoginResponse>();
        Assert.NotNull(login);
        Assert.False(string.IsNullOrWhiteSpace(login!.AccessToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var me = await client.GetFromJsonAsync<AccountContextResponse>("/api/account/me");
        Assert.NotNull(me);
        Assert.Equal("Familia Demo", me!.Familia?.Nombre);
        Assert.Equal("principal", me.Rol);

        var cribs = await client.GetFromJsonAsync<List<CribSummaryResponse>>("/api/cribs");
        Assert.NotNull(cribs);
        var crib = Assert.Single(cribs!);
        Assert.Equal("Cuna Habitación A", crib.Cuna.Nombre);
        Assert.NotNull(crib.Bebe);
        Assert.Equal(5, crib.Dispositivos.Count);

        // Vincular Telegram y probar envío real de alerta ambiental simulada
        var botService = factory.Services.GetRequiredService<TelegramBotService>();
        var codeResp = await client.PostAsync("/api/telegram/link-code", null);
        var code = await codeResp.Content.ReadFromJsonAsync<GenerateTelegramCodeResponse>();
        await botService.ProcessInboundCommandAsync(555001L, $"/start {code!.Codigo}", DateTimeOffset.UtcNow, CancellationToken.None);

        var simEnvResp = await client.PostAsJsonAsync(
            $"/api/demo/cribs/{crib.Cuna.Id}/simulate",
            new DemoSimulateRequest("env_alert"));
        Assert.Equal(HttpStatusCode.OK, simEnvResp.StatusCode);

        Assert.Contains(tgTransport.GetSentMessages(), m =>
            m.ChatId == 555001L && m.Text.Contains("Temperatura o humedad fuera de rango"));

        var simToyResp = await client.PostAsJsonAsync(
            $"/api/demo/cribs/{crib.Cuna.Id}/simulate",
            new DemoSimulateRequest("toy_game"));
        Assert.Equal(HttpStatusCode.OK, simToyResp.StatusCode);

        var toys = await client.GetFromJsonAsync<ToysAndPeripheralsStatusResponse>($"/api/cribs/{crib.Cuna.Id}/toys");
        Assert.NotNull(toys);
        Assert.Contains(toys!.HistorialUso, u => u.Tipo == "botones" && u.Aciertos == 7);
    }

    [Fact]
    public async Task LocalLogin_ReturnsForbidden_WhenDisabledByConfig()
    {
        await using var factory = CreateLocalDemoFactory(out _, localAuthEnabled: false);
        using var client = factory.CreateClient();

        var modeResp = await client.GetFromJsonAsync<AuthModeResponse>("/api/auth/mode");
        Assert.NotNull(modeResp);
        Assert.False(modeResp!.LocalLoginEnabled);

        var loginResp = await client.PostAsJsonAsync("/api/auth/local-login", new LocalLoginRequest("Padre", "123"));
        Assert.Equal(HttpStatusCode.Forbidden, loginResp.StatusCode);
    }
}
