using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CunaServer.Data;

namespace CunaServer.Services;

public interface IJwtTokenValidator
{
    Task<AuthenticatedIdentity?> ValidateBearerTokenAsync(string? authorizationHeader, CancellationToken ct);
}

public sealed class SupabaseJwksTokenValidator : IJwtTokenValidator
{
    private readonly HttpClient _httpClient;
    private readonly string _jwksUrl;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private string? _cachedJwksJson;
    private DateTimeOffset _cacheExpiresAt = DateTimeOffset.MinValue;

    public SupabaseJwksTokenValidator(HttpClient httpClient, IConfiguration configuration, string? initialJwksJson = null)
    {
        _httpClient = httpClient;
        var supabaseUrl = (configuration["SUPABASE_URL"] ?? "https://rjklzbarjuaxdhryklzw.supabase.co").TrimEnd('/');
        _jwksUrl = $"{supabaseUrl}/auth/v1/.well-known/jwks.json";

        if (!string.IsNullOrWhiteSpace(initialJwksJson))
        {
            _cachedJwksJson = initialJwksJson;
            _cacheExpiresAt = DateTimeOffset.UtcNow.AddHours(24);
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

            var jwksJson = await GetJwksJsonAsync(ct);
            if (string.IsNullOrWhiteSpace(jwksJson))
            {
                return null;
            }

            if (!VerifySignatureWithJwks(jwksJson, alg, kid, signedBytes, signatureBytes))
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

    private async Task<string?> GetJwksJsonAsync(CancellationToken ct)
    {
        if (_cachedJwksJson is not null && DateTimeOffset.UtcNow < _cacheExpiresAt)
        {
            return _cachedJwksJson;
        }

        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_cachedJwksJson is not null && DateTimeOffset.UtcNow < _cacheExpiresAt)
            {
                return _cachedJwksJson;
            }

            using var response = await _httpClient.GetAsync(_jwksUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                return _cachedJwksJson;
            }

            _cachedJwksJson = await response.Content.ReadAsStringAsync(ct);
            _cacheExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
            return _cachedJwksJson;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static bool VerifySignatureWithJwks(
        string jwksJson,
        string alg,
        string? kid,
        byte[] signedData,
        byte[] signature)
    {
        using var jwksDoc = JsonDocument.Parse(jwksJson);
        if (!jwksDoc.RootElement.TryGetProperty("keys", out var keysElement) ||
            keysElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var key in keysElement.EnumerateArray())
        {
            if (!string.IsNullOrWhiteSpace(kid) &&
                key.TryGetProperty("kid", out var keyKid) &&
                !string.Equals(keyKid.GetString(), kid, StringComparison.Ordinal))
            {
                continue;
            }

            var kty = key.TryGetProperty("kty", out var ktyProp) ? ktyProp.GetString() : null;
            if (alg == "ES256" && kty == "EC")
            {
                var x = key.GetProperty("x").GetString()!;
                var y = key.GetProperty("y").GetString()!;
                var ecParams = new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint
                    {
                        X = CryptoTokenHelper.Base64UrlDecode(x),
                        Y = CryptoTokenHelper.Base64UrlDecode(y)
                    }
                };

                using var ecdsa = ECDsa.Create(ecParams);
                if (ecdsa.VerifyData(
                        signedData,
                        signature,
                        HashAlgorithmName.SHA256,
                        DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                {
                    return true;
                }
            }
            else if (alg == "RS256" && kty == "RSA")
            {
                var n = key.GetProperty("n").GetString()!;
                var e = key.GetProperty("e").GetString()!;
                var rsaParams = new RSAParameters
                {
                    Modulus = CryptoTokenHelper.Base64UrlDecode(n),
                    Exponent = CryptoTokenHelper.Base64UrlDecode(e)
                };

                using var rsa = RSA.Create(rsaParams);
                if (rsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
