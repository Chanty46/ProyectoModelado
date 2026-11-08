using System.Security.Cryptography;
using System.Text;

namespace CunaServer.Data;

public static class UserRoles
{
    public const string Principal = "principal";
    public const string Secundario = "secundario";
}

public sealed record AuthenticatedIdentity(Guid UserId, string Email);

public sealed record FamilyRecord(
    Guid Id,
    string Nombre,
    DateTimeOffset CreadoEn);

public sealed record UserMembership(
    Guid Id,
    Guid FamilyId,
    string Rol,
    string Email,
    DateTimeOffset CreadoEn);

public sealed record CaregiverProfileRecord(
    Guid Id,
    Guid FamilyId,
    string Nombre,
    string Avatar,
    bool Activo,
    DateTimeOffset? EliminadoEn,
    DateTimeOffset CreadoEn);

public sealed record InvitationRecord(
    Guid Id,
    Guid FamilyId,
    Guid? CreadoPor,
    string HashCodigo,
    DateTimeOffset ExpiraEn,
    int Intentos,
    bool Usada,
    Guid? UsadaPor,
    DateTimeOffset? UsadaEn,
    DateTimeOffset CreadoEn);

public sealed record SecurityAuditEntry(
    Guid? FamilyId,
    Guid? UsuarioId,
    Guid? DispositivoId,
    string Evento,
    string? IpOrigen,
    string? Detalle,
    DateTimeOffset Ts);

public sealed record AccountContextResponse(
    Guid UserId,
    string Email,
    FamilyRecord? Familia,
    string? Rol,
    IReadOnlyList<CaregiverProfileRecord> Perfiles,
    IReadOnlyList<UserMembership> Miembros);

public sealed record BootstrapAccountRequest(string? NombreFamilia, string? NombrePerfilInicial, string? AvatarInicial);

public sealed record UpdateFamilyRequest(string Nombre);

public sealed record UpsertCaregiverProfileRequest(string Nombre, string? Avatar);

public sealed record CreateInvitationResponse(
    Guid InvitationId,
    string Token,
    string InviteUrl,
    DateTimeOffset ExpiraEn);

public sealed record AcceptInvitationRequest(string Token, string? NombrePerfil, string? Avatar);

public static class CryptoTokenHelper
{
    public static string GenerateOpaqueToken(int byteLength = 32)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Base64UrlEncode(bytes);
    }

    public static string ComputeSha256Hex(string rawValue)
    {
        var bytes = Encoding.UTF8.GetBytes(rawValue.Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool ConstantTimeHashEquals(string expectedHexHash, string actualHexHash)
    {
        var a = Encoding.UTF8.GetBytes(expectedHexHash.ToLowerInvariant());
        var b = Encoding.UTF8.GetBytes(actualHexHash.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static string Base64UrlEncode(ReadOnlySpan<byte> input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static byte[] Base64UrlDecode(string input)
    {
        var normalized = input.Replace('-', '+').Replace('_', '/');
        var padding = (4 - (normalized.Length % 4)) % 4;
        if (padding > 0)
        {
            normalized += new string('=', padding);
        }

        return Convert.FromBase64String(normalized);
    }
}
