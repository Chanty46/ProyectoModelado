using CunaServer.Data;
using CunaServer.Tcp;

namespace CunaServer.Services;

public sealed record IntercomChunkRequest(
    int SampleRateHz,
    int Seq,
    string PcmBase64);

public sealed record IntercomChunkResponse(
    bool Delivered,
    int SampleCount,
    int SampleRateHz,
    double MaxSafeGainApplied);

public sealed class IntercomAudioService
{
    public const int MaxPcmBytesPerChunk = 1280; // <= 160 ms a 8 kHz -> línea TCP < 2 KB (< 4 KB)
    public const double MaxSafeGain = 0.65;      // Límite de seguridad auditiva <= 65 dBA (RF11)

    private readonly IFamilyAccountRepository _accountRepo;
    private readonly IDeviceTelemetryRepository _deviceRepo;
    private readonly DeviceTcpServer _tcpServer;

    public IntercomAudioService(
        IFamilyAccountRepository accountRepo,
        IDeviceTelemetryRepository deviceRepo,
        DeviceTcpServer tcpServer)
    {
        _accountRepo = accountRepo;
        _deviceRepo = deviceRepo;
        _tcpServer = tcpServer;
    }

    public async Task<ServiceResult<IntercomChunkResponse>> SendAudioChunkAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        IntercomChunkRequest request,
        string? clientIp,
        CancellationToken ct)
    {
        var membership = await _accountRepo.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                null,
                identity.UserId,
                null,
                "acceso_denegado_403",
                clientIp,
                "intercom_sin_familia",
                DateTimeOffset.UtcNow), ct);
            return ServiceResult<IntercomChunkResponse>.Forbidden("El usuario no pertenece a una familia activa.");
        }

        var central = await _deviceRepo.GetCentralDeviceForCribAsync(cunaId, ct);
        if (central is null)
        {
            return ServiceResult<IntercomChunkResponse>.NotFound("No se encontró una Unidad Central vinculada a esta cuna.");
        }

        if (central.FamilyId != membership.FamilyId)
        {
            await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                membership.FamilyId,
                identity.UserId,
                central.Id,
                "acceso_denegado_403",
                clientIp,
                $"intercom_cuna_ajena:{cunaId}",
                DateTimeOffset.UtcNow), ct);
            return ServiceResult<IntercomChunkResponse>.Forbidden("Acceso denegado: la cuna pertenece a otra familia.");
        }

        if (request.SampleRateHz is < 4000 or > 8000)
        {
            return ServiceResult<IntercomChunkResponse>.BadRequest(
                "La tasa de muestreo de intercomunicador debe estar entre 4000 y 8000 Hz (RF11).");
        }

        if (request.Seq < 0)
        {
            return ServiceResult<IntercomChunkResponse>.BadRequest("Número de secuencia de audio inválido.");
        }

        if (string.IsNullOrWhiteSpace(request.PcmBase64))
        {
            return ServiceResult<IntercomChunkResponse>.BadRequest("El bloque PCM no puede estar vacío.");
        }

        byte[] rawPcm;
        try
        {
            rawPcm = Convert.FromBase64String(request.PcmBase64.Trim());
        }
        catch (FormatException)
        {
            return ServiceResult<IntercomChunkResponse>.BadRequest("Codificación Base64 de audio inválida.");
        }

        if (rawPcm.Length is < 1 or > MaxPcmBytesPerChunk)
        {
            return ServiceResult<IntercomChunkResponse>.BadRequest(
                $"El bloque PCM debe tener entre 1 y {MaxPcmBytesPerChunk} bytes para respetar el límite de 4 KB por línea TCP.");
        }

        var attenuatedPcm = ApplySafeVolumeLimiter(rawPcm);
        var attenuatedBase64 = Convert.ToBase64String(attenuatedPcm);

        var delivered = await _tcpServer.SendCommandAsync(
            central.DeviceId,
            "audio_chunk",
            new
            {
                rate = request.SampleRateHz,
                seq = request.Seq,
                max_dba = 65,
                pcm_b64 = attenuatedBase64
            },
            requireAck: false,
            ct);

        return ServiceResult<IntercomChunkResponse>.Ok(
            new IntercomChunkResponse(delivered, attenuatedPcm.Length, request.SampleRateHz, MaxSafeGain));
    }

    public static byte[] ApplySafeVolumeLimiter(ReadOnlySpan<byte> pcmUnsigned8Bit)
    {
        var output = new byte[pcmUnsigned8Bit.Length];
        for (var i = 0; i < pcmUnsigned8Bit.Length; i++)
        {
            var centered = pcmUnsigned8Bit[i] - 128;
            var scaled = (int)Math.Round(centered * MaxSafeGain);
            var clamped = Math.Clamp(128 + scaled, 0, 255);
            output[i] = (byte)clamped;
        }
        return output;
    }
}
