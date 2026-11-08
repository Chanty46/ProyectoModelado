using CunaServer.Data;

namespace CunaServer.Services;

public enum ServiceStatus
{
    Ok,
    Created,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict
}

public sealed record ServiceResult<T>(
    ServiceStatus Status,
    T? Data = default,
    string? ErrorMessage = null)
{
    public static ServiceResult<T> Ok(T data) => new(ServiceStatus.Ok, data);
    public static ServiceResult<T> Created(T data) => new(ServiceStatus.Created, data);
    public static ServiceResult<T> BadRequest(string msg) => new(ServiceStatus.BadRequest, default, msg);
    public static ServiceResult<T> Forbidden(string msg) => new(ServiceStatus.Forbidden, default, msg);
    public static ServiceResult<T> NotFound(string msg) => new(ServiceStatus.NotFound, default, msg);
    public static ServiceResult<T> Conflict(string msg) => new(ServiceStatus.Conflict, default, msg);
}

public sealed class FamilyAccountService
{
    private readonly IFamilyAccountRepository _repository;
    private readonly string _allowedWebOrigin;

    public FamilyAccountService(IFamilyAccountRepository repository, IConfiguration configuration)
    {
        _repository = repository;
        _allowedWebOrigin = (configuration["ALLOWED_WEB_ORIGIN"] ?? "http://localhost:5173").TrimEnd('/');
    }

    public async Task<AccountContextResponse> GetAccountContextAsync(
        AuthenticatedIdentity identity,
        CancellationToken ct)
    {
        var membership = await _repository.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            return new AccountContextResponse(identity.UserId, identity.Email, null, null, [], []);
        }

        var family = await _repository.GetFamilyAsync(membership.FamilyId, ct);
        var profiles = await _repository.ListActiveProfilesAsync(membership.FamilyId, ct);
        var members = await _repository.ListFamilyMembersAsync(membership.FamilyId, ct);

        return new AccountContextResponse(
            identity.UserId,
            identity.Email,
            family,
            membership.Rol,
            profiles,
            members);
    }

    public async Task<ServiceResult<AccountContextResponse>> BootstrapPrincipalAccountAsync(
        AuthenticatedIdentity identity,
        BootstrapAccountRequest? request,
        CancellationToken ct)
    {
        var existing = await _repository.GetUserMembershipAsync(identity.UserId, ct);
        if (existing is not null)
        {
            var currentContext = await GetAccountContextAsync(identity, ct);
            return ServiceResult<AccountContextResponse>.Ok(currentContext);
        }

        var familyName = string.IsNullOrWhiteSpace(request?.NombreFamilia)
            ? "Mi Familia"
            : request!.NombreFamilia.Trim();

        if (familyName.Length is < 1 or > 80)
        {
            return ServiceResult<AccountContextResponse>.BadRequest(
                "El nombre de la familia debe tener entre 1 y 80 caracteres.");
        }

        var profileName = string.IsNullOrWhiteSpace(request?.NombrePerfilInicial)
            ? "Cuidador Principal"
            : request!.NombrePerfilInicial.Trim();

        if (profileName.Length is < 1 or > 30)
        {
            return ServiceResult<AccountContextResponse>.BadRequest(
                "El nombre del perfil debe tener entre 1 y 30 caracteres (RF23).");
        }

        var avatar = string.IsNullOrWhiteSpace(request?.AvatarInicial)
            ? "F1"
            : request!.AvatarInicial.Trim();

        await _repository.CreateFamilyWithPrincipalAsync(
            identity.UserId,
            identity.Email,
            familyName,
            profileName,
            avatar,
            ct);

        var createdContext = await GetAccountContextAsync(identity, ct);
        return ServiceResult<AccountContextResponse>.Created(createdContext);
    }

    public async Task<ServiceResult<FamilyRecord>> UpdateFamilyNameAsync(
        AuthenticatedIdentity identity,
        UpdateFamilyRequest request,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, forbidden) = await RequirePrincipalAsync(identity, "actualizar_familia", clientIp, ct);
        if (forbidden is not null || membership is null)
        {
            return ServiceResult<FamilyRecord>.Forbidden(forbidden!);
        }

        var nombre = (request.Nombre ?? "").Trim();
        if (nombre.Length is < 1 or > 80)
        {
            return ServiceResult<FamilyRecord>.BadRequest("El nombre de la familia debe tener entre 1 y 80 caracteres.");
        }

        var updated = await _repository.UpdateFamilyNameAsync(membership.FamilyId, nombre, ct);
        return updated is null
            ? ServiceResult<FamilyRecord>.NotFound("Familia no encontrada.")
            : ServiceResult<FamilyRecord>.Ok(updated);
    }

    public async Task<ServiceResult<CaregiverProfileRecord>> CreateProfileAsync(
        AuthenticatedIdentity identity,
        UpsertCaregiverProfileRequest request,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, forbidden) = await RequirePrincipalAsync(identity, "crear_perfil_cuidador", clientIp, ct);
        if (forbidden is not null || membership is null)
        {
            return ServiceResult<CaregiverProfileRecord>.Forbidden(forbidden!);
        }

        var nombre = (request.Nombre ?? "").Trim();
        if (nombre.Length is < 1 or > 30)
        {
            return ServiceResult<CaregiverProfileRecord>.BadRequest(
                "El nombre del perfil debe tener entre 1 y 30 caracteres (RF23).");
        }

        var avatar = string.IsNullOrWhiteSpace(request.Avatar) ? "default" : request.Avatar.Trim();
        if (avatar.Length > 30)
        {
            return ServiceResult<CaregiverProfileRecord>.BadRequest("Identificador de avatar inválido.");
        }

        try
        {
            var created = await _repository.CreateProfileAsync(membership.FamilyId, nombre, avatar, ct);
            return ServiceResult<CaregiverProfileRecord>.Created(created);
        }
        catch (InvalidOperationException ex)
        {
            return ServiceResult<CaregiverProfileRecord>.Conflict(ex.Message);
        }
    }

    public async Task<ServiceResult<CaregiverProfileRecord>> UpdateProfileAsync(
        AuthenticatedIdentity identity,
        Guid profileId,
        UpsertCaregiverProfileRequest request,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, forbidden) = await RequirePrincipalAsync(identity, "editar_perfil_cuidador", clientIp, ct);
        if (forbidden is not null || membership is null)
        {
            return ServiceResult<CaregiverProfileRecord>.Forbidden(forbidden!);
        }

        var nombre = (request.Nombre ?? "").Trim();
        if (nombre.Length is < 1 or > 30)
        {
            return ServiceResult<CaregiverProfileRecord>.BadRequest(
                "El nombre del perfil debe tener entre 1 y 30 caracteres (RF23).");
        }

        var avatar = string.IsNullOrWhiteSpace(request.Avatar) ? "default" : request.Avatar.Trim();
        var updated = await _repository.UpdateProfileAsync(membership.FamilyId, profileId, nombre, avatar, ct);
        return updated is null
            ? ServiceResult<CaregiverProfileRecord>.NotFound("Perfil no encontrado en la familia.")
            : ServiceResult<CaregiverProfileRecord>.Ok(updated);
    }

    public async Task<ServiceResult<bool>> SoftDeleteProfileAsync(
        AuthenticatedIdentity identity,
        Guid profileId,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, forbidden) = await RequirePrincipalAsync(identity, "eliminar_perfil_cuidador", clientIp, ct);
        if (forbidden is not null || membership is null)
        {
            return ServiceResult<bool>.Forbidden(forbidden!);
        }

        var deleted = await _repository.SoftDeleteProfileAsync(membership.FamilyId, profileId, ct);
        return deleted
            ? ServiceResult<bool>.Ok(true)
            : ServiceResult<bool>.NotFound("Perfil no encontrado o ya dado de baja.");
    }

    public async Task<ServiceResult<CreateInvitationResponse>> CreateInvitationAsync(
        AuthenticatedIdentity identity,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, forbidden) = await RequirePrincipalAsync(identity, "crear_invitacion_rf02", clientIp, ct);
        if (forbidden is not null || membership is null)
        {
            return ServiceResult<CreateInvitationResponse>.Forbidden(forbidden!);
        }

        var rawToken = CryptoTokenHelper.GenerateOpaqueToken(32);
        var hashCodigo = CryptoTokenHelper.ComputeSha256Hex(rawToken);
        var expiraEn = DateTimeOffset.UtcNow.AddHours(24);

        var record = await _repository.CreateInvitationAsync(
            membership.FamilyId,
            identity.UserId,
            hashCodigo,
            expiraEn,
            ct);

        await _repository.LogSecurityEventAsync(new SecurityAuditEntry(
            membership.FamilyId,
            identity.UserId,
            null,
            "invitacion_creada",
            clientIp,
            $"invitation_id={record.Id}",
            DateTimeOffset.UtcNow), ct);

        var inviteUrl = $"{_allowedWebOrigin}/invitacion/{rawToken}";
        return ServiceResult<CreateInvitationResponse>.Created(
            new CreateInvitationResponse(record.Id, rawToken, inviteUrl, expiraEn));
    }

    public async Task<ServiceResult<AccountContextResponse>> AcceptInvitationAsync(
        AuthenticatedIdentity identity,
        AcceptInvitationRequest request,
        string? clientIp,
        CancellationToken ct)
    {
        var rawToken = (request.Token ?? "").Trim();
        if (rawToken.Length < 16 || rawToken.Length > 128)
        {
            return ServiceResult<AccountContextResponse>.BadRequest("Token de invitación inválido.");
        }

        var existingMembership = await _repository.GetUserMembershipAsync(identity.UserId, ct);
        if (existingMembership is not null)
        {
            return ServiceResult<AccountContextResponse>.Conflict("El usuario ya pertenece a una familia.");
        }

        var hashCodigo = CryptoTokenHelper.ComputeSha256Hex(rawToken);
        var invitation = await _repository.GetInvitationByHashAsync(hashCodigo, ct);
        if (invitation is null)
        {
            await _repository.LogSecurityEventAsync(new SecurityAuditEntry(
                null,
                identity.UserId,
                null,
                "invitacion_invalida",
                clientIp,
                "hash_no_encontrado",
                DateTimeOffset.UtcNow), ct);
            return ServiceResult<AccountContextResponse>.BadRequest("El enlace de invitación no es válido o ha expirado.");
        }

        if (invitation.Usada || invitation.Intentos >= 5 || DateTimeOffset.UtcNow >= invitation.ExpiraEn)
        {
            await _repository.IncrementInvitationAttemptAsync(invitation.Id, ct);
            await _repository.LogSecurityEventAsync(new SecurityAuditEntry(
                invitation.FamilyId,
                identity.UserId,
                null,
                "invitacion_rechazada",
                clientIp,
                $"invitation_id={invitation.Id}",
                DateTimeOffset.UtcNow), ct);
            return ServiceResult<AccountContextResponse>.BadRequest("El enlace de invitación ya fue utilizado o ha expirado.");
        }

        await _repository.AcceptInvitationAsync(
            invitation.Id,
            invitation.FamilyId,
            identity.UserId,
            identity.Email,
            request.NombrePerfil,
            request.Avatar,
            ct);

        await _repository.LogSecurityEventAsync(new SecurityAuditEntry(
            invitation.FamilyId,
            identity.UserId,
            null,
            "invitacion_aceptada",
            clientIp,
            $"invitation_id={invitation.Id}",
            DateTimeOffset.UtcNow), ct);

        var context = await GetAccountContextAsync(identity, ct);
        return ServiceResult<AccountContextResponse>.Ok(context);
    }

    public async Task<ServiceResult<bool>> RevokeSecondaryMemberAsync(
        AuthenticatedIdentity identity,
        Guid memberUserId,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, forbidden) = await RequirePrincipalAsync(identity, "revocar_miembro_rf22", clientIp, ct);
        if (forbidden is not null || membership is null)
        {
            return ServiceResult<bool>.Forbidden(forbidden!);
        }

        if (memberUserId == identity.UserId)
        {
            return ServiceResult<bool>.BadRequest("El Usuario Principal no puede revocar su propio acceso.");
        }

        var revoked = await _repository.RevokeSecondaryMemberAsync(membership.FamilyId, memberUserId, ct);
        if (!revoked)
        {
            return ServiceResult<bool>.NotFound("Miembro secundario no encontrado en tu familia.");
        }

        await _repository.LogSecurityEventAsync(new SecurityAuditEntry(
            membership.FamilyId,
            identity.UserId,
            null,
            "revocacion_usuario_rf22",
            clientIp,
            $"revoked_user_id={memberUserId}",
            DateTimeOffset.UtcNow), ct);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<IReadOnlyList<SecurityAuditEntry>>> ListSecurityAuditAsync(
        AuthenticatedIdentity identity,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, forbidden) = await RequirePrincipalAsync(identity, "consultar_bitacora_seguridad", clientIp, ct);
        if (forbidden is not null || membership is null)
        {
            return ServiceResult<IReadOnlyList<SecurityAuditEntry>>.Forbidden(forbidden!);
        }

        var entries = await _repository.ListSecurityEventsAsync(membership.FamilyId, ct);
        return ServiceResult<IReadOnlyList<SecurityAuditEntry>>.Ok(entries);
    }

    private async Task<(UserMembership? Membership, string? ForbiddenMessage)> RequirePrincipalAsync(
        AuthenticatedIdentity identity,
        string attemptedAction,
        string? clientIp,
        CancellationToken ct)
    {
        var membership = await _repository.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            await _repository.LogSecurityEventAsync(new SecurityAuditEntry(
                null,
                identity.UserId,
                null,
                "acceso_denegado_403",
                clientIp,
                $"sin_familia:{attemptedAction}",
                DateTimeOffset.UtcNow), ct);
            return (null, "Acceso denegado: el usuario no pertenece a una familia activa.");
        }

        if (!string.Equals(membership.Rol, UserRoles.Principal, StringComparison.Ordinal))
        {
            await _repository.LogSecurityEventAsync(new SecurityAuditEntry(
                membership.FamilyId,
                identity.UserId,
                null,
                "acceso_denegado_403",
                clientIp,
                $"rol_secundario:{attemptedAction}",
                DateTimeOffset.UtcNow), ct);
            return (null, "Acceso denegado (RF03): se requieren privilegios de Usuario Principal.");
        }

        return (membership, null);
    }
}
