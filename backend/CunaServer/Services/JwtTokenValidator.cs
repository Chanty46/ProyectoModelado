using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CunaServer.Data;

namespace CunaServer.Services;

public interface IJwtTokenValidator
{
    Task<AuthenticatedIdentity?> ValidateBearerTokenAsync(string? authorizationHeader, CancellationToken ct);
}

public sealed class CompositeJwtTokenValidator : IJwtTokenValidator
{
    private readonly SupabaseJwksTokenValidator _supabaseValidator;
    private readonly LocalDemoAuthService? _localDemoAuth;

    public CompositeJwtTokenValidator(
        SupabaseJwksTokenValidator supabaseValidator,
        LocalDemoAuthService? localDemoAuth = null)
    {
        _supabaseValidator = supabaseValidator;
        _localDemoAuth = localDemoAuth;
    }

    public Task<AuthenticatedIdentity?> ValidateBearerTokenAsync(string? authorizationHeader, CancellationToken ct)
    {
        if (_localDemoAuth is { IsEnabled: true } &&
            !string.IsNullOrWhiteSpace(authorizationHeader) &&
            authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var rawToken = authorizationHeader["Bearer ".Length..].Trim();
            var localIdentity = _localDemoAuth.ValidateToken(rawToken);
            if (localIdentity is not null)
            {
                return Task.FromResult<AuthenticatedIdentity?>(localIdentity);
            }
        }

        return _supabaseValidator.ValidateBearerTokenAsync(authorizationHeader, ct);
    }
}

public sealed class SupabaseJwksTokenValidator : IJwtTokenValidator
{
    private sealed record CachedJwk(string? Kid, string Kty, ECDsa? EcKey, RSA? RsaKey);
    private sealed record CachedJwksSnapshot(IReadOnlyList<CachedJwk> Keys, DateTimeOffset ExpiresAt);

    private readonly HttpClient _httpClient;
    private readonly string _jwksUrl;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private volatile CachedJwksSnapshot? _cachedSnapshot;

    public SupabaseJwksTokenValidator(HttpClient httpClient, IConfiguration configuration, string? initialJwksJson = null)
    {
        _httpClient = httpClient;
        var supabaseUrl = (configuration["SUPABASE_URL"] ?? "https://rjklzbarjuaxdhryklzw.supabase.co").TrimEnd('/');
        _jwksUrl = $"{supabaseUrl}/auth/v1/.well-known/jwks.json";

        if (!string.IsNullOrWhiteSpace(initialJwksJson))
        {
            _cachedSnapshot = new CachedJwksSnapshot(
                ParseJwksKeys(initialJwksJson),
                DateTimeOffset.UtcNow.AddHours(24));
        }
    }

    public async Task<AuthenticatedIdentity?> ValidateBearerTokenAsync(string? authorizationHeader, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader) ||
            !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var headerJson = Encoding.UTF8.GetString(CryptoTokenHelper.Base64UrlDecode(parts[0]));
            var payloadJson = Encoding.UTF8.GetString(CryptoTokenHelper.Base64UrlDecode(parts[1]));
            var signatureBytes = CryptoTokenHelper.Base64UrlDecode(parts[2]);
            var signedBytes = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");

            using var headerDoc = JsonDocument.Parse(headerJson);
            var alg = headerDoc.RootElement.TryGetProperty("alg", out var algProp) ? algProp.GetString() : null;
            var kid = headerDoc.RootElement.TryGetProperty("kid", out var kidProp) ? kidProp.GetString() : null;

            if (string.IsNullOrWhiteSpace(alg) || (alg != "ES256" && alg != "RS256"))
            {
                return null;
            }

            var jwksSnapshot = await GetJwksSnapshotAsync(ct);
            if (jwksSnapshot is null || jwksSnapshot.Keys.Count == 0)
            {
                return null;
            }

            if (!VerifySignatureWithCachedKeys(jwksSnapshot.Keys, alg, kid, signedBytes, signatureBytes))
            {
                return null;
            }

            using var payloadDoc = JsonDocument.Parse(payloadJson);
            var root = payloadDoc.RootElement;

            var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (root.TryGetProperty("exp", out var expProp) && expProp.TryGetInt64(out var exp))
            {
                if (nowEpoch >= exp)
                {
                    return null;
                }
            }
            else
            {
                return null;
            }

            if (!root.TryGetProperty("sub", out var subProp) ||
                !Guid.TryParse(subProp.GetString(), out var userId))
            {
                return null;
            }

            var email = root.TryGetProperty("email", out var emailProp)
                ? emailProp.GetString() ?? ""
                : "";

            if (string.IsNullOrWhiteSpace(email))
            {
                email = $"{userId:N}@usuario.local";
            }

            return new AuthenticatedIdentity(userId, email);
        }
        catch
        {
            return null;
        }
    }

    private async Task<CachedJwksSnapshot?> GetJwksSnapshotAsync(CancellationToken ct)
    {
        var current = _cachedSnapshot;
        if (current is not null && DateTimeOffset.UtcNow < current.ExpiresAt)
        {
            return current;
        }

        await _cacheLock.WaitAsync(ct);
        try
        {
            current = _cachedSnapshot;
            if (current is not null && DateTimeOffset.UtcNow < current.ExpiresAt)
            {
                return current;
            }

            using var response = await _httpClient.GetAsync(_jwksUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                return current;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var parsedKeys = ParseJwksKeys(json);
            var refreshed = new CachedJwksSnapshot(parsedKeys, DateTimeOffset.UtcNow.AddMinutes(30));
            _cachedSnapshot = refreshed;
            return refreshed;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static IReadOnlyList<CachedJwk> ParseJwksKeys(string jwksJson)
    {
        using var jwksDoc = JsonDocument.Parse(jwksJson);
        if (!jwksDoc.RootElement.TryGetProperty("keys", out var keysElement) ||
            keysElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<CachedJwk>();
        }

        var keys = new List<CachedJwk>();
        foreach (var key in keysElement.EnumerateArray())
        {
            var kid = key.TryGetProperty("kid", out var kidProp) ? kidProp.GetString() : null;
            var kty = key.TryGetProperty("kty", out var ktyProp) ? ktyProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(kty))
            {
                continue;
            }

            if (kty == "EC" &&
                key.TryGetProperty("x", out var xProp) &&
                key.TryGetProperty("y", out var yProp))
            {
                var x = xProp.GetString();
                var y = yProp.GetString();
                if (!string.IsNullOrWhiteSpace(x) && !string.IsNullOrWhiteSpace(y))
                {
                    var ecParams = new ECParameters
                    {
                        Curve = ECCurve.NamedCurves.nistP256,
                        Q = new ECPoint
                        {
                            X = CryptoTokenHelper.Base64UrlDecode(x),
                            Y = CryptoTokenHelper.Base64UrlDecode(y)
                        }
                    };
                    keys.Add(new CachedJwk(kid, "EC", ECDsa.Create(ecParams), null));
                }
            }
            else if (kty == "RSA" &&
                key.TryGetProperty("n", out var nProp) &&
                key.TryGetProperty("e", out var eProp))
            {
                var n = nProp.GetString();
                var e = eProp.GetString();
                if (!string.IsNullOrWhiteSpace(n) && !string.IsNullOrWhiteSpace(e))
                {
                    var rsaParams = new RSAParameters
                    {
                        Modulus = CryptoTokenHelper.Base64UrlDecode(n),
                        Exponent = CryptoTokenHelper.Base64UrlDecode(e)
                    };
                    keys.Add(new CachedJwk(kid, "RSA", null, RSA.Create(rsaParams)));
                }
            }
        }

        return keys;
    }

    private static bool VerifySignatureWithCachedKeys(
        IReadOnlyList<CachedJwk> keys,
        string alg,
        string? kid,
        byte[] signedData,
        byte[] signature)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (!string.IsNullOrWhiteSpace(kid) &&
                !string.IsNullOrWhiteSpace(key.Kid) &&
                !string.Equals(key.Kid, kid, StringComparison.Ordinal))
            {
                continue;
            }

            if (alg == "ES256" && key.EcKey is not null &&
                key.EcKey.VerifyData(
                    signedData,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return true;
            }

            if (alg == "RS256" && key.RsaKey is not null &&
                key.RsaKey.VerifyData(signedData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                return true;
            }
        }

        return false;
    }
}
