using CunaServer.Data;

namespace CunaServer.Services;

internal static class FamilyAuthorizationHelper
{
    public static async Task<(UserMembership? Membership, string? ForbiddenMessage)> RequireMembershipAsync(
        IFamilyAccountRepository accountRepo,
        AuthenticatedIdentity identity,
        string attemptedAction,
        string? clientIp,
        CancellationToken ct)
    {
        var membership = await accountRepo.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is not null)
        {
            return (membership, null);
        }

        await accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
            null,
            identity.UserId,
            null,
            "acceso_denegado_403",
            clientIp,
            $"sin_familia:{attemptedAction}",
            DateTimeOffset.UtcNow), ct);

        return (null, "Acceso denegado: el usuario no pertenece a una familia activa.");
    }

    public static async Task<(UserMembership? Membership, string? ForbiddenMessage)> RequirePrincipalAsync(
        IFamilyAccountRepository accountRepo,
        AuthenticatedIdentity identity,
        string attemptedAction,
        string? clientIp,
        CancellationToken ct,
        string? customPrincipalMessage = null)
    {
        var (membership, error) = await RequireMembershipAsync(accountRepo, identity, attemptedAction, clientIp, ct);
        if (membership is null)
        {
            return (null, error);
        }

        if (!string.Equals(membership.Rol, UserRoles.Principal, StringComparison.Ordinal))
        {
            await accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                membership.FamilyId,
                identity.UserId,
                null,
                "acceso_denegado_403",
                clientIp,
                $"rol_secundario:{attemptedAction}",
                DateTimeOffset.UtcNow), ct);
            return (null, customPrincipalMessage ?? "Acceso denegado (RF03): se requieren privilegios de Usuario Principal.");
        }

        return (membership, null);
    }

    public static async Task<(UserMembership? Membership, CribRecord? Crib, string? ForbiddenMessage)> RequireFamilyCribAccessAsync(
        IFamilyAccountRepository accountRepo,
        IDeviceTelemetryRepository deviceRepo,
        AuthenticatedIdentity identity,
        Guid cunaId,
        string attemptedAction,
        bool requirePrincipal,
        string? clientIp,
        CancellationToken ct,
        string? customPrincipalMessage = null)
    {
        var (membership, error) = requirePrincipal
            ? await RequirePrincipalAsync(
                accountRepo,
                identity,
                attemptedAction,
                clientIp,
                ct,
                customPrincipalMessage ?? "Acceso denegado (RF03): solo el Usuario Principal puede modificar umbrales.")
            : await RequireMembershipAsync(accountRepo, identity, attemptedAction, clientIp, ct);

        if (membership is null)
        {
            return (null, null, error);
        }

        var crib = await deviceRepo.GetCribByIdAsync(cunaId, ct);
        if (crib is null || crib.FamilyId != membership.FamilyId)
        {
            await accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                membership.FamilyId,
                identity.UserId,
                null,
                "acceso_denegado_403",
                clientIp,
                $"cuna_ajena:{attemptedAction}:{cunaId}",
                DateTimeOffset.UtcNow), ct);
            return (null, null, "Acceso denegado: la cuna solicitada no pertenece a tu familia.");
        }

        return (membership, crib, null);
    }

    public static async Task<(UserMembership? Membership, BabyRecord? Baby, string? ForbiddenMessage)> RequireFamilyBabyAccessAsync(
        IFamilyAccountRepository accountRepo,
        IDeviceTelemetryRepository deviceRepo,
        AuthenticatedIdentity identity,
        Guid bebeId,
        string attemptedAction,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, error) = await RequireMembershipAsync(accountRepo, identity, attemptedAction, clientIp, ct);
        if (membership is null)
        {
            return (null, null, error);
        }

        var baby = await deviceRepo.GetBabyByIdAsync(bebeId, ct);
        if (baby is null || baby.FamilyId != membership.FamilyId)
        {
            await accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                membership.FamilyId,
                identity.UserId,
                null,
                "acceso_denegado_403",
                clientIp,
                $"bebe_ajeno:{attemptedAction}:{bebeId}",
                DateTimeOffset.UtcNow), ct);
            return (null, null, "Acceso denegado: el perfil de bebé no pertenece a tu familia.");
        }

        return (membership, baby, null);
    }
}
