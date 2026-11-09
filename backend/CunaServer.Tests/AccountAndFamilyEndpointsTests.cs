using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CunaServer.Data;
using CunaServer.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CunaServer.Tests;

public sealed class TestJwksAuthority : IDisposable
{
    private readonly ECDsa _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public string Kid { get; } = "test-es256-key-1";
    public string JwksJson { get; }

    public TestJwksAuthority()
    {
        var pub = _ecdsa.ExportParameters(false);
        var jwk = new
        {
            keys = new[]
            {
                new
                {
                    kty = "EC",
                    crv = "P-256",
                    alg = "ES256",
                    use = "sig",
                    kid = Kid,
                    x = CryptoTokenHelper.Base64UrlEncode(pub.Q.X!),
                    y = CryptoTokenHelper.Base64UrlEncode(pub.Q.Y!)
                }
            }
        };
        JwksJson = JsonSerializer.Serialize(jwk);
    }

    public string CreateToken(Guid userId, string email, TimeSpan? lifetime = null)
    {
        var headerJson = JsonSerializer.Serialize(new { alg = "ES256", typ = "JWT", kid = Kid });
        var exp = DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromHours(1)).ToUnixTimeSeconds();
        var payloadJson = JsonSerializer.Serialize(new
        {
            sub = userId.ToString(),
            email,
            role = "authenticated",
            exp
        });

        var headerB64 = CryptoTokenHelper.Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadB64 = CryptoTokenHelper.Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = $"{headerB64}.{payloadB64}";
        var sig = _ecdsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var sigB64 = CryptoTokenHelper.Base64UrlEncode(sig);
        return $"{signingInput}.{sigB64}";
    }

    public void Dispose() => _ecdsa.Dispose();
}

public class AccountAndFamilyEndpointsTests : IDisposable
{
    private readonly TestJwksAuthority _jwks = new();
    private readonly InMemoryFamilyAccountRepository _repository = new();
    private readonly WebApplicationFactory<Program> _factory;

    public AccountAndFamilyEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFamilyAccountRepository>();
                services.AddSingleton<IFamilyAccountRepository>(_repository);

                services.RemoveAll<IJwtTokenValidator>();
                services.AddSingleton<IJwtTokenValidator>(sp =>
                {
                    var config = sp.GetRequiredService<IConfiguration>();
                    return new SupabaseJwksTokenValidator(new HttpClient(), config, _jwks.JwksJson);
                });
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _jwks.Dispose();
    }

    private HttpClient CreateAuthenticatedClient(Guid userId, string email, TimeSpan? lifetime = null)
    {
        var client = _factory.CreateClient();
        var token = _jwks.CreateToken(userId, email, lifetime);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task JwtValidator_RejectsMissingExpiredOrTamperedTokens()
    {
        using var anonClient = _factory.CreateClient();
        using var resMissing = await anonClient.GetAsync("/api/account/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resMissing.StatusCode);

        var userId = Guid.NewGuid();
        using var expiredClient = CreateAuthenticatedClient(userId, "exp@ejemplo.local", TimeSpan.FromMinutes(-5));
        using var resExpired = await expiredClient.GetAsync("/api/account/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resExpired.StatusCode);

        using var tamperedClient = _factory.CreateClient();
        var validToken = _jwks.CreateToken(userId, "tamper@ejemplo.local");
        tamperedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", validToken[..^4] + "AAAA");
        using var resTampered = await tamperedClient.GetAsync("/api/account/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resTampered.StatusCode);
    }

    [Fact]
    public async Task Rf01AndRf23_PrincipalBootstrapsFamily_ManagesUpTo6Profiles_AndSoftDeletes()
    {
        var principalId = Guid.NewGuid();
        using var client = CreateAuthenticatedClient(principalId, "principal@ejemplo.local");

        // RF01: Bootstrap crea la familia, el rol principal y el primer perfil activo
        using var bootRes = await client.PostAsJsonAsync("/api/account/bootstrap", new
        {
            nombreFamilia = "Familia Rivera",
            nombrePerfilInicial = "Mamá Clara",
            avatarInicial = "F1"
        });
        Assert.Equal(HttpStatusCode.Created, bootRes.StatusCode);

        var context = await bootRes.Content.ReadFromJsonAsync<AccountContextResponse>();
        Assert.NotNull(context);
        Assert.NotNull(context!.Familia);
        Assert.Equal("principal", context.Rol);
        Assert.Single(context.Perfiles);

        // RF23: Nombre > 30 caracteres se rechaza con 400
        using var badNameRes = await client.PostAsJsonAsync("/api/family/profiles", new
        {
            nombre = new string('A', 31),
            avatar = "F2"
        });
        Assert.Equal(HttpStatusCode.BadRequest, badNameRes.StatusCode);

        // RF23: Agregar 5 perfiles adicionales para llegar al máximo de 6 activos
        CaregiverProfileRecord? lastCreated = null;
        for (var i = 2; i <= 6; i++)
        {
            using var createRes = await client.PostAsJsonAsync("/api/family/profiles", new
            {
                nombre = $"Cuidador {i}",
                avatar = "C1"
            });
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
            lastCreated = await createRes.Content.ReadFromJsonAsync<CaregiverProfileRecord>();
        }

        // RF23: El 7.º perfil activo se rechaza con 409 Conflict
        using var seventhRes = await client.PostAsJsonAsync("/api/family/profiles", new
        {
            nombre = "Cuidador 7 Exceso",
            avatar = "C1"
        });
        Assert.Equal(HttpStatusCode.Conflict, seventhRes.StatusCode);

        // RF23: Baja lógica de un perfil permite crear otro sin borrar el registro histórico
        Assert.NotNull(lastCreated);
        using var delRes = await client.DeleteAsync($"/api/family/profiles/{lastCreated!.Id}");
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);

        var softDeletedRecord = await _repository.GetProfileAsync(lastCreated.Id, CancellationToken.None);
        Assert.NotNull(softDeletedRecord);
        Assert.False(softDeletedRecord!.Activo);
        Assert.NotNull(softDeletedRecord.EliminadoEn);

        using var replacementRes = await client.PostAsJsonAsync("/api/family/profiles", new
        {
            nombre = "Abuela Elena",
            avatar = "A1"
        });
        Assert.Equal(HttpStatusCode.Created, replacementRes.StatusCode);
    }

    [Fact]
    public async Task Rf02_Rf03_Rf22_InvitationSingleUse_SecondaryReceives403AndAudit_AndRevocationCutsAccessImmediately()
    {
        var principalId = Guid.NewGuid();
        var secondaryId = Guid.NewGuid();
        var thirdUserId = Guid.NewGuid();

        using var principalClient = CreateAuthenticatedClient(principalId, "principal@familia.local");
        using var secondaryClient = CreateAuthenticatedClient(secondaryId, "secundario@familia.local");
        using var thirdClient = CreateAuthenticatedClient(thirdUserId, "tercero@familia.local");

        // 1. Principal inicializa la familia
        using var bootRes = await principalClient.PostAsJsonAsync("/api/account/bootstrap", new
        {
            nombreFamilia = "Familia Segura",
            nombrePerfilInicial = "Papá Andrés",
            avatarInicial = "M1"
        });
        Assert.Equal(HttpStatusCode.Created, bootRes.StatusCode);
        var bootCtx = await bootRes.Content.ReadFromJsonAsync<AccountContextResponse>();
        var familyId = bootCtx!.Familia!.Id;

        // 2. RF02 / D8: Principal genera invitación de un solo uso (24 h, guardada solo como hash)
        using var invRes = await principalClient.PostAsync("/api/family/invitations", null);
        Assert.Equal(HttpStatusCode.Created, invRes.StatusCode);
        var invPayload = await invRes.Content.ReadFromJsonAsync<CreateInvitationResponse>();
        Assert.NotNull(invPayload);
        Assert.False(string.IsNullOrWhiteSpace(invPayload!.Token));

        var storedInvitations = await _repository.ListInvitationsAsync(familyId, CancellationToken.None);
        Assert.Single(storedInvitations);
        Assert.NotEqual(invPayload.Token, storedInvitations[0].HashCodigo);
        Assert.Equal(CryptoTokenHelper.ComputeSha256Hex(invPayload.Token), storedInvitations[0].HashCodigo);

        // 3. Usuario secundario acepta la invitación
        using var acceptRes = await secondaryClient.PostAsJsonAsync("/api/family/invitations/accept", new
        {
            token = invPayload.Token,
            nombrePerfil = "Tío Mateo",
            avatar = "C1"
        });
        Assert.Equal(HttpStatusCode.OK, acceptRes.StatusCode);
        var secCtx = await acceptRes.Content.ReadFromJsonAsync<AccountContextResponse>();
        Assert.Equal("secundario", secCtx!.Rol);
        Assert.Equal(familyId, secCtx.Familia!.Id);

        // 4. RF02: Reutilizar el mismo token de invitación falla (un solo uso)
        using var reuseRes = await thirdClient.PostAsJsonAsync("/api/family/invitations/accept", new
        {
            token = invPayload.Token
        });
        Assert.Equal(HttpStatusCode.BadRequest, reuseRes.StatusCode);

        // 5. RF03: Usuario secundario intenta acciones administrativas -> recibe HTTP 403 y se registra en bitacora_seguridad
        using var secCreateProfileRes = await secondaryClient.PostAsJsonAsync("/api/family/profiles", new
        {
            nombre = "Intento Secundario",
            avatar = "F1"
        });
        Assert.Equal(HttpStatusCode.Forbidden, secCreateProfileRes.StatusCode);

        using var secInviteRes = await secondaryClient.PostAsync("/api/family/invitations", null);
        Assert.Equal(HttpStatusCode.Forbidden, secInviteRes.StatusCode);

        using var secUpdateFamilyRes = await secondaryClient.PatchAsJsonAsync("/api/family", new
        {
            nombre = "Cambio No Autorizado"
        });
        Assert.Equal(HttpStatusCode.Forbidden, secUpdateFamilyRes.StatusCode);

        var auditEntries = await _repository.ListSecurityEventsAsync(familyId, CancellationToken.None);
        Assert.Contains(auditEntries, e => e.Evento == "acceso_denegado_403" && e.UsuarioId == secondaryId);

        // 6. RF22: Principal revoca al usuario secundario -> pierde acceso de inmediato (< 5 s)
        using var revokeRes = await principalClient.DeleteAsync($"/api/family/members/{secondaryId}");
        Assert.Equal(HttpStatusCode.OK, revokeRes.StatusCode);

        using var meAfterRevokeRes = await secondaryClient.GetAsync("/api/account/me");
        Assert.Equal(HttpStatusCode.OK, meAfterRevokeRes.StatusCode);
        var meAfterRevoke = await meAfterRevokeRes.Content.ReadFromJsonAsync<AccountContextResponse>();
        Assert.Null(meAfterRevoke!.Familia);
        Assert.Null(meAfterRevoke.Rol);
        Assert.Empty(meAfterRevoke.Perfiles);
    }
}
