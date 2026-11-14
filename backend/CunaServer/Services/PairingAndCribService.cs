using System.Collections.Concurrent;
using CunaServer.Data;
using CunaServer.Tcp;

namespace CunaServer.Services;

public sealed class PairingAndCribService
{
    public const int PairingWindowMinutes = 10;

    private readonly IFamilyAccountRepository _accountRepo;
    private readonly IDeviceTelemetryRepository _deviceRepo;
    private readonly DeviceTcpServer _tcpServer;
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _pairingWindowsByFamily = new();

    public PairingAndCribService(
        IFamilyAccountRepository accountRepo,
        IDeviceTelemetryRepository deviceRepo,
        DeviceTcpServer tcpServer)
    {
        _accountRepo = accountRepo;
        _deviceRepo = deviceRepo;
        _tcpServer = tcpServer;
    }

    public async Task<ServiceResult<OpenPairingWindowResponse>> OpenPairingWindowAsync(
        AuthenticatedIdentity identity,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, forbidden) = await RequirePrincipalAsync(identity, "abrir_ventana_emparejamiento", clientIp, ct);
        if (forbidden is not null || membership is null)
        {
            return ServiceResult<OpenPairingWindowResponse>.Forbidden(forbidden!);
        }

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(PairingWindowMinutes);
        _pairingWindowsByFamily[membership.FamilyId] = expiresAt;

        return ServiceResult<OpenPairingWindowResponse>.Ok(
            new OpenPairingWindowResponse(expiresAt, PairingWindowMinutes));
    }

    public async Task<ServiceResult<ClaimDeviceResponse>> ClaimDeviceAsync(
        AuthenticatedIdentity identity,
        ClaimDeviceRequest? request,
        string? clientIp,
        CancellationToken ct)
    {
        if (request is null)
        {
            return ServiceResult<ClaimDeviceResponse>.BadRequest("Se requiere el cuerpo de la petición.");
        }

        var (membership, forbidden) = await RequirePrincipalAsync(identity, "reclamar_dispositivo_rf24", clientIp, ct);
        if (forbidden is not null || membership is null)
        {
            return ServiceResult<ClaimDeviceResponse>.Forbidden(forbidden!);
        }

        if (!_pairingWindowsByFamily.TryGetValue(membership.FamilyId, out var windowExpires) ||
            DateTimeOffset.UtcNow >= windowExpires)
        {
            return ServiceResult<ClaimDeviceResponse>.BadRequest(
                "La ventana de emparejamiento de 10 minutos no está abierta o ha expirado. Pulsa 'Agregar dispositivo' primero.");
        }

        var deviceId = (request.DeviceId ?? "").Trim();
        var rawClaimCode = (request.CodigoReclamo ?? "").Trim();

        if (deviceId.Length is < 3 or > 64)
        {
            return ServiceResult<ClaimDeviceResponse>.BadRequest("Identificador de dispositivo inválido (3–64 caracteres).");
        }

        if (rawClaimCode.Length is < 8 or > 64)
        {
            return ServiceResult<ClaimDeviceResponse>.BadRequest("El código de reclamo debe tener al menos 8 caracteres.");
        }

        var device = await _deviceRepo.GetClaimableDeviceAsync(deviceId, ct);
        if (device is null)
        {
            return ServiceResult<ClaimDeviceResponse>.NotFound("Dispositivo no encontrado.");
        }

        // RF24: Rechazar si ya está vinculado a otra familia informando el motivo
        if (device.FamilyId.HasValue && device.FamilyId.Value != membership.FamilyId)
        {
            await _deviceRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                membership.FamilyId,
                identity.UserId,
                device.Id,
                "reclamo_rechazado_familia_ajena",
                clientIp,
                $"device_id={deviceId}",
                DateTimeOffset.UtcNow), ct);
            return ServiceResult<ClaimDeviceResponse>.Conflict(
                "Rechazado (RF24): este dispositivo ya se encuentra vinculado a otra familia.");
        }

        if (device.ReclamoUsado || device.CunaId.HasValue)
        {
            return ServiceResult<ClaimDeviceResponse>.Conflict(
                "Rechazado: el código de reclamo ya fue utilizado anteriormente.");
        }

        if (device.IntentosReclamo >= 5)
        {
            await _deviceRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                membership.FamilyId,
                identity.UserId,
                device.Id,
                "reclamo_bloqueado_intentos",
                clientIp,
                $"device_id={deviceId}",
                DateTimeOffset.UtcNow), ct);
            return ServiceResult<ClaimDeviceResponse>.BadRequest(
                "El código de reclamo fue bloqueado tras superar el máximo de 5 intentos fallidos.");
        }

        if (device.ReclamoExpiraEn.HasValue && DateTimeOffset.UtcNow >= device.ReclamoExpiraEn.Value)
        {
            return ServiceResult<ClaimDeviceResponse>.BadRequest("El código de reclamo del dispositivo ha caducado.");
        }

        var candidateHash = CryptoTokenHelper.ComputeSha256Hex(rawClaimCode);
        var isValidClaim = !string.IsNullOrWhiteSpace(device.HashReclamo) &&
                           CryptoTokenHelper.ConstantTimeHashEquals(device.HashReclamo, candidateHash);

        if (!isValidClaim)
        {
            await _deviceRepo.IncrementClaimAttemptAsync(device.Id, ct);
            await _deviceRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                membership.FamilyId,
                identity.UserId,
                device.Id,
                "reclamo_codigo_invalido",
                clientIp,
                $"device_id={deviceId}",
                DateTimeOffset.UtcNow), ct);
            return ServiceResult<ClaimDeviceResponse>.BadRequest("Código de reclamo incorrecto.");
        }

        CribRecord targetCrib;
        if (request.CunaId.HasValue)
        {
            var existingCrib = await _deviceRepo.GetCribByIdAsync(request.CunaId.Value, ct);
            if (existingCrib is null || existingCrib.FamilyId != membership.FamilyId)
            {
                return ServiceResult<ClaimDeviceResponse>.BadRequest("La cuna seleccionada no pertenece a tu familia.");
            }
            targetCrib = existingCrib;
        }
        else
        {
            if (device.Tipo != DeviceTypes.Central)
            {
                return ServiceResult<ClaimDeviceResponse>.BadRequest(
                    "Precondición RF25: para vincular un periférico primero debes registrar y seleccionar una cuna (RF24).");
            }

            var nombreCuna = string.IsNullOrWhiteSpace(request.NombreCuna)
                ? "Cuna Principal"
                : request.NombreCuna.Trim();

            if (nombreCuna.Length is < 1 or > 30)
            {
                return ServiceResult<ClaimDeviceResponse>.BadRequest(
                    "El nombre de la cuna debe tener entre 1 y 30 caracteres (RF26).");
            }

            targetCrib = await _deviceRepo.CreateCribWithDefaultsAsync(membership.FamilyId, nombreCuna, ct);
        }

        var rawDeviceToken = CryptoTokenHelper.GenerateOpaqueToken(32);
        var tokenHash = CryptoTokenHelper.ComputeSha256Hex(rawDeviceToken);

        await _deviceRepo.BindClaimedDeviceAsync(
            device.Id,
            targetCrib.Id,
            membership.FamilyId,
            tokenHash,
            ct);

        await _deviceRepo.LogSecurityEventAsync(new SecurityAuditEntry(
            membership.FamilyId,
            identity.UserId,
            device.Id,
            "dispositivo_vinculado_rf24",
            clientIp,
            $"device_id={deviceId};cuna_id={targetCrib.Id}",
            DateTimeOffset.UtcNow), ct);

        return ServiceResult<ClaimDeviceResponse>.Created(new ClaimDeviceResponse(
            device.Id,
            device.DeviceId,
            device.Tipo,
            targetCrib.Id,
            targetCrib.Nombre,
            rawDeviceToken));
    }

    public async Task<ServiceResult<IReadOnlyList<CribSummaryResponse>>> ListCribsAsync(
        AuthenticatedIdentity identity,
        CancellationToken ct)
    {
        var membership = await _accountRepo.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            return ServiceResult<IReadOnlyList<CribSummaryResponse>>.Forbidden("El usuario no pertenece a una familia activa.");
        }

        var cribs = await _deviceRepo.ListCribsByFamilyAsync(membership.FamilyId, ct);
        var babies = await _deviceRepo.ListBabiesByFamilyAsync(membership.FamilyId, ct);
        var summaries = new List<CribSummaryResponse>(cribs.Count);

        foreach (var crib in cribs)
        {
            var devices = await _deviceRepo.ListDevicesByCribAsync(crib.Id, ct);
            var baby = babies.FirstOrDefault(b => b.CunaId == crib.Id);
            summaries.Add(new CribSummaryResponse(crib, devices, baby));
        }

        return ServiceResult<IReadOnlyList<CribSummaryResponse>>.Ok(summaries);
    }

    public async Task<ServiceResult<CribRecord>> RenameCribAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        RenameCribRequest? request,
        string? clientIp,
        CancellationToken ct)
    {
        if (request is null)
        {
            return ServiceResult<CribRecord>.BadRequest("Se requiere el cuerpo de la petición.");
        }

        var (membership, forbidden) = await RequirePrincipalAsync(identity, "renombrar_cuna_rf26", clientIp, ct);
        if (forbidden is not null || membership is null)
        {
            return ServiceResult<CribRecord>.Forbidden(forbidden!);
        }

        var nombre = (request.Nombre ?? "").Trim();
        if (nombre.Length is < 1 or > 30)
        {
            return ServiceResult<CribRecord>.BadRequest("El nombre de la cuna debe tener entre 1 y 30 caracteres (RF26).");
        }

        var updated = await _deviceRepo.RenameCribAsync(membership.FamilyId, cunaId, nombre, ct);
        return updated is null
            ? ServiceResult<CribRecord>.NotFound("Cuna no encontrada en tu familia.")
            : ServiceResult<CribRecord>.Ok(updated);
    }

    public async Task<ServiceResult<bool>> UnlinkCribAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        UnlinkCribRequest? request,
        string? clientIp,
        CancellationToken ct)
    {
        if (request is null)
        {
            return ServiceResult<bool>.BadRequest("Se requiere el cuerpo de la petición.");
        }

        var (membership, forbidden) = await RequirePrincipalAsync(identity, "desvincular_cuna_rf27", clientIp, ct);
        if (forbidden is not null || membership is null)
        {
            return ServiceResult<bool>.Forbidden(forbidden!);
        }

        if (!request.ConfirmacionExplicita ||
            !string.Equals((request.TextoConfirmacion ?? "").Trim(), "DESVINCULAR", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<bool>.BadRequest(
                "RF27 requiere doble confirmación explícita escribiendo 'DESVINCULAR'.");
        }

        var revokedDeviceIds = await _deviceRepo.UnlinkCribAndRevokeDevicesAsync(membership.FamilyId, cunaId, ct);
        if (revokedDeviceIds.Count == 0 && await _deviceRepo.GetCribByIdAsync(cunaId, ct) is not null)
        {
            return ServiceResult<bool>.NotFound("Cuna no encontrada en tu familia.");
        }

        foreach (var devId in revokedDeviceIds)
        {
            _tcpServer.DisconnectDevice(devId);
        }

        await _deviceRepo.LogSecurityEventAsync(new SecurityAuditEntry(
            membership.FamilyId,
            identity.UserId,
            null,
            "desvinculacion_cuna_rf27",
            clientIp,
            $"cuna_id={cunaId};revoked_count={revokedDeviceIds.Count}",
            DateTimeOffset.UtcNow), ct);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<IReadOnlyList<BabyRecord>>> ListBabiesAsync(
        AuthenticatedIdentity identity,
        CancellationToken ct)
    {
        var membership = await _accountRepo.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            return ServiceResult<IReadOnlyList<BabyRecord>>.Forbidden("El usuario no pertenece a una familia activa.");
        }

        var babies = await _deviceRepo.ListBabiesByFamilyAsync(membership.FamilyId, ct);
        return ServiceResult<IReadOnlyList<BabyRecord>>.Ok(babies);
    }

    public async Task<ServiceResult<BabyRecord>> CreateBabyAsync(
        AuthenticatedIdentity identity,
        UpsertBabyRequest request,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, forbidden) = await RequirePrincipalAsync(identity, "registrar_bebe_rf28", clientIp, ct);
        if (forbidden is not null || membership is null)
        {
            return ServiceResult<BabyRecord>.Forbidden(forbidden!);
        }

        var validationError = await ValidateBabyRequestAsync(membership.FamilyId, request, ct);
        if (validationError is not null)
        {
            return ServiceResult<BabyRecord>.BadRequest(validationError);
        }

        var created = await _deviceRepo.CreateBabyAsync(
            membership.FamilyId,
            request.CunaId,
            request.Nombre.Trim(),
            request.FechaNacimiento,
            string.IsNullOrWhiteSpace(request.Notas) ? null : request.Notas.Trim(),
            ct);

        return ServiceResult<BabyRecord>.Created(created);
    }

    public async Task<ServiceResult<BabyRecord>> UpdateBabyAsync(
        AuthenticatedIdentity identity,
        Guid bebeId,
        UpsertBabyRequest request,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, forbidden) = await RequirePrincipalAsync(identity, "actualizar_bebe_rf28", clientIp, ct);
        if (forbidden is not null || membership is null)
        {
            return ServiceResult<BabyRecord>.Forbidden(forbidden!);
        }

        var validationError = await ValidateBabyRequestAsync(membership.FamilyId, request, ct);
        if (validationError is not null)
        {
            return ServiceResult<BabyRecord>.BadRequest(validationError);
        }

        var updated = await _deviceRepo.UpdateBabyAsync(
            membership.FamilyId,
            bebeId,
            request.CunaId,
            request.Nombre.Trim(),
            request.FechaNacimiento,
            string.IsNullOrWhiteSpace(request.Notas) ? null : request.Notas.Trim(),
            ct);

        return updated is null
            ? ServiceResult<BabyRecord>.NotFound("Perfil de bebé no encontrado en tu familia.")
            : ServiceResult<BabyRecord>.Ok(updated);
    }

    private async Task<string?> ValidateBabyRequestAsync(Guid familyId, UpsertBabyRequest? request, CancellationToken ct)
    {
        if (request is null)
        {
            return "Se requiere el cuerpo de la petición.";
        }

        var nombre = (request.Nombre ?? "").Trim();
        if (nombre.Length is < 1 or > 60)
        {
            return "El nombre del bebé debe tener entre 1 y 60 caracteres (RF28).";
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        if (request.FechaNacimiento > today)
        {
            return "La fecha de nacimiento no puede ser futura (RF28).";
        }

        var crib = await _deviceRepo.GetCribByIdAsync(request.CunaId, ct);
        if (crib is null || crib.FamilyId != familyId)
        {
            return "Precondición RF28: debes asociar al bebé con una cuna registrada en tu familia (RF24).";
        }

        return null;
    }

    private Task<(UserMembership? Membership, string? ForbiddenMessage)> RequirePrincipalAsync(
        AuthenticatedIdentity identity,
        string attemptedAction,
        string? clientIp,
        CancellationToken ct)
        => FamilyAuthorizationHelper.RequirePrincipalAsync(
            _accountRepo,
            identity,
            attemptedAction,
            clientIp,
            ct);
}
