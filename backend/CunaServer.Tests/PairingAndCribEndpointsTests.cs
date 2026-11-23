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

public class PairingAndCribEndpointsTests
{
    private static readonly Guid PrincipalUserId = Guid.Parse("11111111-1111-1111-1111-111111111101");
    private static readonly Guid SecondaryUserId = Guid.Parse("11111111-1111-1111-1111-111111111102");

    private WebApplicationFactory<Program> CreateTestFactory(
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
                services.AddSingleton<IJwtTokenValidator>(new FakePairingJwtValidator());
            });
        });
    }

    [Fact]
    public async Task FullFlow_RF24_RF25_RF26_RF27_RF28_Claim_TcpConnect_Rename_Baby_And_Unlink()
    {
        await using var factory = CreateTestFactory(out _, out var deviceRepo);
        using var client = factory.CreateClient();
        var tcpServer = factory.Services.GetRequiredService<DeviceTcpServer>();
        var tcpPort = await tcpServer.WaitForBoundPortAsync();

        // 1. Bootstrap familia para el Usuario Principal
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        var bootResp = await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia Soto", "Papá", "M1"));
        Assert.Equal(HttpStatusCode.Created, bootResp.StatusCode);

        // 2. Intentar reclamar sin ventana abierta -> falla (RF24)
        var noWinResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-central-nuevo",
            "RECLAMO-CENTRAL-1001",
            null,
            "Cuna Principal"));
        Assert.Equal(HttpStatusCode.BadRequest, noWinResp.StatusCode);

        // 3. Abrir ventana de emparejamiento de 60 minutos (RF24)
        var winResp = await client.PostAsync("/api/pairing/window", null);
        Assert.Equal(HttpStatusCode.OK, winResp.StatusCode);
        var winBody = await winResp.Content.ReadFromJsonAsync<OpenPairingWindowResponse>();
        Assert.NotNull(winBody);
        Assert.Equal(PairingAndCribService.PairingWindowMinutes, winBody.VentanaMinutos);
        Assert.True(winBody.ExpiraEn > DateTimeOffset.UtcNow.AddMinutes(55));

        // 4. Reclamar Unidad Central de fábrica -> crea cuna nueva y emite token de 32 bytes (RF24/RF25)
        var claimCentralResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-central-nuevo",
            "RECLAMO-CENTRAL-1001",
            null,
            "Cuna Principal"));
        Assert.Equal(HttpStatusCode.Created, claimCentralResp.StatusCode);
        var claimedCentral = await claimCentralResp.Content.ReadFromJsonAsync<ClaimDeviceResponse>();
        Assert.NotNull(claimedCentral);
        Assert.Equal("pico-central-nuevo", claimedCentral.DeviceId);
        Assert.Equal("central", claimedCentral.Tipo);
        Assert.True(claimedCentral.DeviceTokenOnce.Length >= 40); // 32 bytes Base64Url (~43 chars)

        // Verificar que en BD solo se guardó el hash SHA-256 y no el token en texto plano
        var storedDev = await deviceRepo.GetClaimableDeviceAsync("pico-central-nuevo", CancellationToken.None);
        Assert.NotNull(storedDev);
        Assert.NotEqual(claimedCentral.DeviceTokenOnce, storedDev.HashToken);
        Assert.Equal(CryptoTokenHelper.ComputeSha256Hex(claimedCentral.DeviceTokenOnce), storedDev.HashToken);

        // 5. Reutilizar el mismo código de reclamo falla (un solo uso)
        var reuseResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-central-nuevo",
            "RECLAMO-CENTRAL-1001",
            claimedCentral.CunaId,
            null));
        Assert.Equal(HttpStatusCode.Conflict, reuseResp.StatusCode);

        // 6. Reclamar periférico (colchón) asociándolo a la cuna creada
        var claimColchonResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-colchon-nuevo",
            "RECLAMO-COLCHON-1002",
            claimedCentral.CunaId,
            null));
        Assert.Equal(HttpStatusCode.Created, claimColchonResp.StatusCode);

        // 7. Conectar la Unidad Central recién reclamada por TCP usando el token generado
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync("127.0.0.1", tcpPort);
        await using var stream = tcpClient.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false), leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        await writer.WriteLineAsync(
            $$"""{"v":1,"t":"hello","seq":1,"ts":1762550000,"d":{"device_id":"pico-central-nuevo","device_type":"central","token":"{{claimedCentral.DeviceTokenOnce}}","fw":"1.0.0"} }""");
        var helloAck = await reader.ReadLineAsync();
        Assert.NotNull(helloAck);
        Assert.Contains("\"t\":\"hello_ack\"", helloAck);
        Assert.True(tcpServer.IsDeviceConnected("pico-central-nuevo"));

        // 8. RF28: Registrar y actualizar bebé asociado a la cuna
        var createBabyResp = await client.PostAsJsonAsync("/api/babies", new UpsertBabyRequest(
            claimedCentral.CunaId,
            "Bebé Sofía",
            new DateOnly(2026, 5, 10),
            "Sin alergias"));
        Assert.Equal(HttpStatusCode.Created, createBabyResp.StatusCode);
        var baby = await createBabyResp.Content.ReadFromJsonAsync<BabyRecord>();
        Assert.NotNull(baby);
        Assert.Equal(claimedCentral.CunaId, baby.CunaId);

        var updateBabyResp = await client.PatchAsJsonAsync($"/api/babies/{baby.Id}", new UpsertBabyRequest(
            claimedCentral.CunaId,
            "Bebé Sofía Soto",
            new DateOnly(2026, 5, 10),
            "Control pediátrico mensual"));
        Assert.Equal(HttpStatusCode.OK, updateBabyResp.StatusCode);

        // 9. RF26: Renombrar cuna (1-30 caracteres)
        var renameResp = await client.PatchAsJsonAsync($"/api/cribs/{claimedCentral.CunaId}", new RenameCribRequest("Cuna Cuarto Principal"));
        Assert.Equal(HttpStatusCode.OK, renameResp.StatusCode);
        var renamedCrib = await renameResp.Content.ReadFromJsonAsync<CribRecord>();
        Assert.NotNull(renamedCrib);
        Assert.Equal("Cuna Cuarto Principal", renamedCrib.Nombre);

        // 10. RF27: Desvincular cuna sin confirmación explícita falla
        var badUnlinkResp = await client.PostAsJsonAsync($"/api/cribs/{claimedCentral.CunaId}/unlink", new UnlinkCribRequest(false, "NO"));
        Assert.Equal(HttpStatusCode.BadRequest, badUnlinkResp.StatusCode);

        // 11. RF27: Desvincular cuna con confirmación "DESVINCULAR" revoca tokens, cierra TCP y conserva al bebé
        var unlinkResp = await client.PostAsJsonAsync($"/api/cribs/{claimedCentral.CunaId}/unlink", new UnlinkCribRequest(true, "DESVINCULAR"));
        Assert.Equal(HttpStatusCode.OK, unlinkResp.StatusCode);

        Assert.False(tcpServer.IsDeviceConnected("pico-central-nuevo"));

        var babiesAfterUnlink = await client.GetFromJsonAsync<IReadOnlyList<BabyRecord>>("/api/babies");
        Assert.NotNull(babiesAfterUnlink);
        Assert.Single(babiesAfterUnlink);
        Assert.Equal("Bebé Sofía Soto", babiesAfterUnlink[0].Nombre);
        Assert.Null(babiesAfterUnlink[0].CunaId);
    }

    [Fact]
    public async Task RF24_LockoutAfterFiveFailedClaimAttempts_And_RejectOtherFamilyDevice()
    {
        await using var factory = CreateTestFactory(out _, out _);
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia A", "Mamá", "F1"));
        await client.PostAsync("/api/pairing/window", null);

        // Dispositivo ya vinculado a otra familia en semilla (sim-biberon-a) -> 409 Conflict
        var otherFamilyDevResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "sim-biberon-a",
            "CUALQUIER-CODIGO",
            null,
            "Cuna Intrusa"));
        Assert.Equal(HttpStatusCode.Conflict, otherFamilyDevResp.StatusCode);

        // 5 intentos fallidos sobre pico-biberon-nuevo
        for (var i = 1; i <= 5; i++)
        {
            var attemptResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
                "pico-biberon-nuevo",
                $"CODIGO-INCORRECTO-{i}",
                null,
                "Cuna Test"));
            Assert.Equal(HttpStatusCode.BadRequest, attemptResp.StatusCode);
        }

        // Incluso con el código correcto ahora está bloqueado tras 5 intentos fallidos
        var lockedResp = await client.PostAsJsonAsync("/api/pairing/claim", new ClaimDeviceRequest(
            "pico-biberon-nuevo",
            "RECLAMO-BIBERON-1004",
            null,
            "Cuna Test"));
        Assert.Equal(HttpStatusCode.BadRequest, lockedResp.StatusCode);
        var lockedBody = await lockedResp.Content.ReadAsStringAsync();
        Assert.Contains("bloqueado", lockedBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RF03_SecondaryUser_CannotClaimOrRenameOrUnlinkOrEditBaby_And_IsAudited()
    {
        await using var factory = CreateTestFactory(out var accountRepo, out _);
        using var client = factory.CreateClient();

        // Principal crea familia e invita a secundario
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-principal");
        var bootResp = await client.PostAsJsonAsync("/api/account/bootstrap", new BootstrapAccountRequest("Familia Roles", "Mamá", "F1"));
        var bootCtx = await bootResp.Content.ReadFromJsonAsync<AccountContextResponse>();
        Assert.NotNull(bootCtx?.Familia);

        var invResp = await client.PostAsync("/api/family/invitations", null);
        var inv = await invResp.Content.ReadFromJsonAsync<CreateInvitationResponse>();

        // Secundario acepta invitación
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-secundario");
        await client.PostAsJsonAsync("/api/family/invitations/accept", new AcceptInvitationRequest(inv!.Token, "Tío", "C1"));

        // Secundario intenta abrir ventana de emparejamiento -> 403
        var winResp = await client.PostAsync("/api/pairing/window", null);
        Assert.Equal(HttpStatusCode.Forbidden, winResp.StatusCode);

        // Secundario intenta registrar bebé -> 403
        var babyResp = await client.PostAsJsonAsync("/api/babies", new UpsertBabyRequest(
            Guid.NewGuid(),
            "Bebé",
            new DateOnly(2026, 6, 1),
            null));
        Assert.Equal(HttpStatusCode.Forbidden, babyResp.StatusCode);

        // Bitácora de seguridad registró ambos intentos
        var events = await accountRepo.ListSecurityEventsAsync(bootCtx.Familia.Id, CancellationToken.None);
        Assert.True(events.Count >= 2);
    }

    private sealed class FakePairingJwtValidator : IJwtTokenValidator
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
