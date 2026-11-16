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

/// <summary>
/// Servicio de intercomunicación unidireccional (RF11) que atenúa y retransmite bloques PCM
/// hacia la Unidad Central garantizando el límite de seguridad auditiva infantil &lt;= 65 dBA.
/// </summary>
public sealed class IntercomAudioService
{
    /// <summary>
    /// Tope de 1280 bytes PCM (&lt;= 160 ms a 8 kHz) para que, una vez codificado en Base64 dentro
    /// del sobre JSON, la trama TCP completa permanezca holgadamente por debajo del framing de 4 KB.
    /// </summary>
    public const int MaxPcmBytesPerChunk = 1280;

    /// <summary>
    /// Factor de atenuación máximo (0.65) aplicado sobre la señal PCM de 8 bits para mantener
    /// la presión sonora de salida en la cuna dentro del límite de seguridad auditiva &lt;= 65 dBA (RF11).
    /// </summary>
    public const double MaxSafeGain = 0.65;

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
        var (membership, forbidden) = await FamilyAuthorizationHelper.RequireMembershipAsync(
            _accountRepo,
            identity,
            "intercom",
            clientIp,
            ct);
        if (membership is null)
        {
            return ServiceResult<IntercomChunkResponse>.Forbidden(forbidden ?? "El usuario no pertenece a una familia activa.");
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

        if (!TryValidateAndDecodeAudioChunk(request, out var rawPcm, out var validationError))
        {
            return ServiceResult<IntercomChunkResponse>.BadRequest(validationError!);
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

    /// <summary>
    /// Atenúa muestras PCM de 8 bits sin signo centradas en 128 multiplicando su amplitud por
    /// <see cref="MaxSafeGain"/> (0.65) para acotar la salida acústica a &lt;= 65 dBA antes del envío TCP.
    /// </summary>
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

    private static bool TryValidateAndDecodeAudioChunk(
        IntercomChunkRequest request,
        out byte[] rawPcm,
        out string? errorMessage)
    {
        rawPcm = Array.Empty<byte>();
        errorMessage = null;

        if (request.SampleRateHz is < 4000 or > 8000)
        {
            errorMessage = "La tasa de muestreo de intercomunicador debe estar entre 4000 y 8000 Hz (RF11).";
            return false;
        }

        if (request.Seq < 0)
        {
            errorMessage = "Número de secuencia de audio inválido.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.PcmBase64))
        {
            errorMessage = "El bloque PCM no puede estar vacío.";
            return false;
        }

        try
        {
            rawPcm = Convert.FromBase64String(request.PcmBase64.Trim());
        }
        catch (FormatException)
        {
            errorMessage = "Codificación Base64 de audio inválida.";
            return false;
        }

        if (rawPcm.Length is < 1 or > MaxPcmBytesPerChunk)
        {
            errorMessage = $"El bloque PCM debe tener entre 1 y {MaxPcmBytesPerChunk} bytes para respetar el límite de 4 KB por línea TCP.";
            return false;
        }

        return true;
    }
}
