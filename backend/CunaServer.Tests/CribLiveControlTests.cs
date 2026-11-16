using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using CunaServer.Data;
using CunaServer.Services;
using CunaServer.Tcp;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CunaServer.Tests;

public class CribLiveControlTests
{
    private static readonly Guid PrincipalUserId = Guid.Parse("22222222-2222-2222-2222-222222222201");
    private static readonly Guid SecondaryUserId = Guid.Parse("22222222-2222-2222-2222-222222222202");

    private WebApplicationFactory<Program> CreateTestFactory(
        CribLiveControlOptions liveOptions,
        out InMemoryFamilyAccountRepository accountRepo,
        out InMemoryDeviceTelemetryRepository deviceRepo)
    {
        var accRepo = new InMemoryFamilyAccountRepository();
        var devRepo = new InMemoryDeviceTelemetryRepository();
        accountRepo = accRepo;
        deviceRepo = devRepo;

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IFamilyAccountRepository>(accRepo);
                services.AddSingleton<IDeviceTelemetryRepository>(devRepo);
                services.AddSingleton<IJwtTokenValidator>(new FakeLiveJwtValidator());
                services.AddSingleton(liveOptions);
            });
        });
    }

    [Fact]
    public async Task RF04_RF05_RemovalGraceWindow_CancelledByAttentionMode_Or_TriggersCriticalAlarm()
    {
        var options = new CribLiveControlOptions
        {
            RemovalGraceWindow = TimeSpan.FromMilliseconds(220),
            AttentionModeDuration = TimeSpan.FromSeconds(900),
            WatchdogTimeout = TimeSpan.FromSeconds(60)
        };

        await using var factory = CreateTestFactory(options, out _, out var deviceRepo);
        using var client = factory.CreateClient();
        var tcpServer = factory.Services.GetRequiredService<DeviceTcpServer>();
        var tcpPort = await tcpServer.WaitForBoundPortAsync();

        // 1. Bootstrap familia y reclamar Central + Colchón
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia M5", "Mamá", "F1"));
        await client.PostAsync("/api/pairing/window", null);

        var claimCentralResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-central-nuevo",
            "RECLAMO-CENTRAL-1001",
            null,
            "Cuna En Vivo"));
        var centralClaim = await claimCentralResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();
        Assert.NotNull(centralClaim);
        var cunaId = centralClaim.CunaId;

        var claimColchonResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-colchon-nuevo",
            "RECLAMO-COLCHON-1002",
            cunaId,
            null));
        var colchonClaim = await claimColchonResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();
        Assert.NotNull(colchonClaim);

        // 2. Conectar Central y Colchón por TCP
        using var centralTcp = new TcpClient();
        await centralTcp.ConnectAsync("127.0.0.1", tcpPort);
        await using var centralStream = centralTcp.GetStream();
        using var centralReader = new StreamReader(centralStream, new UTF8Encoding(false), leaveOpen: true);
        await using var centralWriter = new StreamWriter(centralStream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        await centralWriter.WriteLineAsync(
            $$"""{"v":1,"t":"hello","seq":1,"ts":1762560000,"d":{"device_id":"pico-central-nuevo","device_type":"central","token":"{{centralClaim.DeviceTokenOnce}}","fw":"1.0.0"} }""");
        var centralAck = await centralReader.ReadLineAsync();
        Assert.Contains("\"t\":\"hello_ack\"", centralAck);

        using var colchonTcp = new TcpClient();
        await colchonTcp.ConnectAsync("127.0.0.1", tcpPort);
        await using var colchonStream = colchonTcp.GetStream();
        using var colchonReader = new StreamReader(colchonStream, new UTF8Encoding(false), leaveOpen: true);
        await using var colchonWriter = new StreamWriter(colchonStream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        await colchonWriter.WriteLineAsync(
            $$"""{"v":1,"t":"hello","seq":1,"ts":1762560000,"d":{"device_id":"pico-colchon-nuevo","device_type":"colchon","token":"{{colchonClaim.DeviceTokenOnce}}","fw":"1.0.0"} }""");
        var colchonAck = await colchonReader.ReadLineAsync();
        Assert.Contains("\"t\":\"hello_ack\"", colchonAck);

        // 3. Escenario A: Colchón envía 'removal', pero en < 220 ms se activa Modo Atención -> NO suena alarma
        await colchonWriter.WriteLineAsync(
            """{"v":1,"t":"event","seq":1,"ts":1762560005,"d":{"kind":"removal","severity":"critica","msg":"Retiro detectado"} }""");
        _ = await colchonReader.ReadLineAsync(); // ack del evento

        var modeResp = await client.PostAsJsonAsync($"/api/cribs/{cunaId}/mode", new SetCribModeRequest("atencion"));
        Assert.Equal(HttpStatusCode.OK, modeResp.StatusCode);

        await Task.Delay(320);

        var alertsAfterCancel = await deviceRepo.ListAlertsForCribAsync(cunaId, CancellationToken.None);
        Assert.DoesNotContain(alertsAfterCancel, a => a.Tipo == "removal_alarm");

        // 4. Volver a modo monitoreo y disparar 'removal' dejando vencer la ventana de gracia -> dispara alarma crítica RF04
        var resumeResp = await client.PostAsJsonAsync($"/api/cribs/{cunaId}/mode", new SetCribModeRequest("monitoreo"));
        Assert.Equal(HttpStatusCode.OK, resumeResp.StatusCode);

        await colchonWriter.WriteLineAsync(
            """{"v":1,"t":"event","seq":2,"ts":1762560020,"d":{"kind":"removal","severity":"critica","msg":"Retiro sin pausa"} }""");
        _ = await colchonReader.ReadLineAsync();

        await Task.Delay(350);

        var cribInAlarm = await deviceRepo.GetCribByIdAsync(cunaId, CancellationToken.None);
        Assert.NotNull(cribInAlarm);
        Assert.Equal("alarma", cribInAlarm.ModoOperacion);

        var alertsAfterExpiry = await deviceRepo.ListAlertsForCribAsync(cunaId, CancellationToken.None);
        Assert.Contains(alertsAfterExpiry, a => a.Tipo == "removal_alarm" && a.Severidad == "critica");
    }

    [Fact]
    public async Task RF09_Light_RF08_Thresholds_RF29_MovementToggle_RF20_Watchdog_And_RF03_Forbidden()
    {
        var options = new CribLiveControlOptions
        {
            RemovalGraceWindow = TimeSpan.FromSeconds(5),
            AttentionModeDuration = TimeSpan.FromSeconds(900),
            WatchdogTimeout = TimeSpan.FromSeconds(60)
        };

        await using var factory = CreateTestFactory(options, out var accountRepo, out var deviceRepo);
        using var client = factory.CreateClient();
        var liveService = factory.Services.GetRequiredService<CribLiveControlService>();

        // 1. Bootstrap familia, invitar secundario y crear cuna
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        var bootResp = await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia Control", "Papá", "M1"));
        var bootCtx = await bootResp.Content.ReadFromJsonAsync<AccountContextResponse>();
        var profileId = bootCtx!.Perfiles[0].Id;

        var invResp = await client.PostAsync("/api/family/invitations", null);
        var inv = await invResp.Content.ReadFromJsonAsync<CreateInvitationResponse>();

        await client.PostAsync("/api/pairing/window", null);
        var claimResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-central-nuevo",
            "RECLAMO-CENTRAL-1001",
            null,
            "Cuna Principal"));
        var claimed = await claimResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();
        var cunaId = claimed!.CunaId;

        // 2. RF09: Regular iluminación nocturna gradual
        var lightResp = await client.PostAsJsonAsync($"/api/cribs/{cunaId}/light", new SetCribLightRequest(true, 75, profileId));
        Assert.Equal(HttpStatusCode.OK, lightResp.StatusCode);
        var cribWithLight = await lightResp.Content.ReadFromJsonAsync<CribRecord>();
        Assert.NotNull(cribWithLight);
        Assert.True(cribWithLight.LuzEncendida);
        Assert.Equal(75, cribWithLight.LuzNivel);

        // 3. RF08 + RF29: Ajustar umbrales y desactivar alerta de movimiento anómalo
        var updateThresholdsResp = await client.PutAsJsonAsync($"/api/cribs/{cunaId}/thresholds", new UpdateCribThresholdsRequest(
            TempMin: 19.5m,
            TempMax: 24.5m,
            HumMin: 45.0m,
            HumMax: 58.0m,
            MovimientoUmbral: 0.220m,
            MovimientoAlertaActiva: false,
            RuidoMasaPiso: 0.012m,
            EscalaMasa: 0.20m,
            PerfilId: profileId));
        Assert.Equal(HttpStatusCode.OK, updateThresholdsResp.StatusCode);

        // Verificar que con MovimientoAlertaActiva == false, un evento movement_high se filtra
        var centralDevice = await deviceRepo.GetCentralDeviceForCribAsync(cunaId, CancellationToken.None);
        var shouldPersistMovement = await liveService.ProcessIncomingDeviceEventAsync(
            centralDevice!,
            new EventAlertRecord(
                Guid.NewGuid(),
                cunaId,
                centralDevice!.Id,
                "movement_high",
                "advertencia",
                "Movimiento alto",
                null,
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.False(shouldPersistMovement);

        // 4. RF20: Simular heartbeat y luego avance de reloj > 60 s -> Watchdog marca desconectada y genera alerta
        var t0 = DateTimeOffset.UtcNow;
        await deviceRepo.RecordHeartbeatAsync(centralDevice.Id, cunaId, DeviceTypes.Central, 95, "1.0.0", t0, CancellationToken.None);

        var liveBefore = await client.GetFromJsonAsync<LiveCribStatusResponse>($"/api/cribs/{cunaId}/live");
        Assert.Equal("en_linea", liveBefore!.Cuna.EstadoConexion);

        await liveService.EvaluateWatchdogAndTimersAsync(t0.AddSeconds(65), CancellationToken.None);

        var liveAfter = await client.GetFromJsonAsync<LiveCribStatusResponse>($"/api/cribs/{cunaId}/live");
        Assert.Equal("desconectada", liveAfter!.Cuna.EstadoConexion);
        Assert.Contains(liveAfter.AlertasRecientes, a => a.Tipo == "watchdog_offline" && a.Severidad == "critica");

        // 5. RF03: Usuario secundario intenta modificar umbrales -> 403 Forbidden + bitacora_seguridad
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-secundario");
        await client.PostAsJsonAsync("/api/family/invitations/accept", new AcceptInvitationRequest(inv!.Token, "Abuela", "A1"));

        var secThresholdsResp = await client.PutAsJsonAsync($"/api/cribs/{cunaId}/thresholds", new UpdateCribThresholdsRequest(
            20.0m, 25.0m, 40.0m, 60.0m, 0.150m, true, 0.010m, 0.20m));
        Assert.Equal(HttpStatusCode.Forbidden, secThresholdsResp.StatusCode);

        var auditLogs = await accountRepo.ListSecurityEventsAsync(bootCtx.Familia!.Id, CancellationToken.None);
        Assert.Contains(auditLogs, e => e.Evento == "acceso_denegado_403" && e.UsuarioId == SecondaryUserId);
    }

    [Fact]
    public async Task MultiDeviceTelemetry_ConsolidatesLatestFieldsAcrossCentralAndColchon()
    {
        await using var factory = CreateTestFactory(new CribLiveControlOptions(), out _, out var deviceRepo);
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia MultiNodo", "Mamá", "M1"));
        await client.PostAsync("/api/pairing/window", null);

        var claimCentralResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-central-nuevo",
            "RECLAMO-CENTRAL-1001",
            null,
            "Cuna Multi"));
        var claimedCentral = await claimCentralResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();
        var cunaId = claimedCentral!.CunaId;

        var claimColchonResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-colchon-nuevo",
            "RECLAMO-COLCHON-1002",
            cunaId,
            null));
        var claimedColchon = await claimColchonResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();

        var t0 = DateTimeOffset.UtcNow.AddSeconds(-20);
        // Ambos dispositivos inician con seq=1; primero colchón, después central, luego biberón simulado
        await deviceRepo.SaveTelemetryAsync(new TelemetryRecord(
            cunaId, claimedColchon!.DeviceRowId, t0, 0.720m, 0.042m, null, null, null, null, 98, 1), CancellationToken.None);
        await deviceRepo.SaveTelemetryAsync(new TelemetryRecord(
            cunaId, claimedCentral.DeviceRowId, t0.AddSeconds(5), null, null, 22.8m, 49.5m, null, null, null, 1), CancellationToken.None);

        var live = await client.GetFromJsonAsync<LiveCribStatusResponse>($"/api/cribs/{cunaId}/live");
        Assert.NotNull(live);
        Assert.NotNull(live!.UltimaTelemetria);
        Assert.Equal(0.720m, live.UltimaTelemetria!.MasaKg);
        Assert.Equal(0.042m, live.UltimaTelemetria.IndiceMovimiento);
        Assert.Equal(22.8m, live.UltimaTelemetria.TempC);
        Assert.Equal(49.5m, live.UltimaTelemetria.Humedad);
        Assert.Equal(2, live.TelemetriaReciente.Count);
        Assert.True(live.TelemetriaReciente[0].Ts <= live.TelemetriaReciente[1].Ts);
    }

    [Fact]
    public async Task CriticalModeCommands_RequireAckWithRetry_And_DoNotBlockHttpEndpoint()
    {
        var accRepo = new InMemoryFamilyAccountRepository();
        var devRepo = new InMemoryDeviceTelemetryRepository();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IFamilyAccountRepository>(accRepo);
                services.AddSingleton<IDeviceTelemetryRepository>(devRepo);
                services.AddSingleton<IJwtTokenValidator>(new FakeLiveJwtValidator());
                services.AddSingleton(new CribLiveControlOptions
                {
                    RemovalGraceWindow = TimeSpan.FromSeconds(5),
                    AttentionModeDuration = TimeSpan.FromSeconds(900),
                    WatchdogTimeout = TimeSpan.FromSeconds(60)
                });
                services.AddSingleton(new DeviceTcpServerOptions
                {
                    BindAddress = IPAddress.Loopback,
                    Port = 0,
                    AckTimeout = TimeSpan.FromMilliseconds(120)
                });
            });
        });

        using var client = factory.CreateClient();
        var tcpServer = factory.Services.GetRequiredService<DeviceTcpServer>();
        var tcpPort = await tcpServer.WaitForBoundPortAsync();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia Retry", "Mamá", "F1"));
        await client.PostAsync("/api/pairing/window", null);
        var claimResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-central-nuevo",
            "RECLAMO-CENTRAL-1001",
            null,
            "Cuna Retry"));
        var claimed = await claimResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();

        using var centralTcp = new TcpClient();
        await centralTcp.ConnectAsync("127.0.0.1", tcpPort);
        await using var stream = centralTcp.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false), leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        await writer.WriteLineAsync(
            $$"""{"v":1,"t":"hello","seq":1,"ts":1762560000,"d":{"device_id":"pico-central-nuevo","device_type":"central","token":"{{claimed!.DeviceTokenOnce}}","fw":"1.0.0"} }""");
        var helloAck = await reader.ReadLineAsync();
        Assert.Contains("\"t\":\"hello_ack\"", helloAck);

        // El endpoint HTTP debe responder rápido (< 200 ms) sin esperar los reintentos TCP ni el ACK del dispositivo
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var modeResp = await client.PostAsJsonAsync($"/api/cribs/{claimed.CunaId}/mode", new SetCribModeRequest("atencion"));
        sw.Stop();
        Assert.Equal(HttpStatusCode.OK, modeResp.StatusCode);
        Assert.True(sw.ElapsedMilliseconds < 250, $"POST /mode tardó {sw.ElapsedMilliseconds} ms y no debe bloquear en TCP");

        // Central omite ACK en el 1er alarm_set -> servidor debe reintentar el mismo cmdId antes de pasar a mode_set
        var line1 = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(line1);
        using var doc1 = System.Text.Json.JsonDocument.Parse(line1!);
        Assert.Equal("alarm_set", doc1.RootElement.GetProperty("d").GetProperty("cmd").GetString());
        var alarmCmdId = doc1.RootElement.GetProperty("d").GetProperty("id").GetString();

        var line2 = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(line2);
        using var doc2 = System.Text.Json.JsonDocument.Parse(line2!);
        Assert.Equal("alarm_set", doc2.RootElement.GetProperty("d").GetProperty("cmd").GetString());
        Assert.Equal(alarmCmdId, doc2.RootElement.GetProperty("d").GetProperty("id").GetString());

        // Confirmar alarm_set -> llega mode_set y lo confirmamos
        await writer.WriteLineAsync($$"""{"v":1,"t":"ack","seq":2,"ts":1762560001,"d":{"id":"{{alarmCmdId}}","ok":true} }""");

        var line3 = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(line3);
        using var doc3 = System.Text.Json.JsonDocument.Parse(line3!);
        Assert.Equal("mode_set", doc3.RootElement.GetProperty("d").GetProperty("cmd").GetString());
        Assert.Equal("atencion", doc3.RootElement.GetProperty("d").GetProperty("params").GetProperty("mode").GetString());
        var modeCmdId = doc3.RootElement.GetProperty("d").GetProperty("id").GetString();

        await writer.WriteLineAsync($$"""{"v":1,"t":"ack","seq":3,"ts":1762560002,"d":{"id":"{{modeCmdId}}","ok":true} }""");

        var line4 = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(line4);
        using var doc4 = System.Text.Json.JsonDocument.Parse(line4!);
        Assert.Equal("status_led", doc4.RootElement.GetProperty("d").GetProperty("cmd").GetString());
        Assert.Equal("yellow", doc4.RootElement.GetProperty("d").GetProperty("params").GetProperty("color").GetString());
    }

    private sealed class FakeLiveJwtValidator : IJwtTokenValidator
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
