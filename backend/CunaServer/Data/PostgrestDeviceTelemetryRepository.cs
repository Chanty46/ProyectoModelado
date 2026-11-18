using System.Text.Json;

namespace CunaServer.Data;

/// <summary>
/// Implementación de <see cref="IDeviceTelemetryRepository"/> respaldada por Supabase PostgREST
/// mediante <see cref="PostgrestClientHelper"/>, deduciendo siempre <c>family_id</c> desde la base de datos.
/// </summary>
public sealed partial class PostgrestDeviceTelemetryRepository : IDeviceTelemetryRepository
{
    private const int MaxClaimAttempts = 5;
    private const int RecentRowsLimit = 200;

    private readonly PostgrestClientHelper _client;

    /// <summary>
    /// Inicializa el repositorio PostgREST configurando el cliente HTTP con <c>SUPABASE_URL</c> y <c>SUPABASE_SECRET_KEY</c>.
    /// </summary>
    public PostgrestDeviceTelemetryRepository(HttpClient httpClient, IConfiguration configuration)
    {
        _client = new PostgrestClientHelper(httpClient, configuration);
    }

    #region Autenticación de dispositivos, anti-replay y telemetría (M3)

    /// <inheritdoc />
    /// <remarks>Deduce <c>family_id</c> consultando la tabla <c>cunas</c> a partir de <c>cuna_id</c>.</remarks>
    public async Task<RegisteredDevice?> GetDeviceByDeviceIdAsync(string deviceId, CancellationToken ct)
    {
        var encodedId = Uri.EscapeDataString(deviceId);
        var d = await _client.GetFirstOrDefaultAsync<DeviceRowDto>(
            $"dispositivos?device_id=eq.{encodedId}&select=id,device_id,cuna_id,tipo,hash_token,estado,bateria_pct,ultimo_seq,ultimo_heartbeat,fw_version&limit=1",
            ct);
        if (d is null || d.CunaId is null || string.IsNullOrWhiteSpace(d.HashToken))
        {
            return null;
        }

        var cuna = await _client.GetFirstOrDefaultAsync<CunaOwnerDto>(
            $"cunas?id=eq.{d.CunaId}&select=id,family_id,escala_masa&limit=1",
            ct);
        if (cuna is null)
        {
            return null;
        }

        return new RegisteredDevice(
            d.Id,
            d.DeviceId,
            d.CunaId.Value,
            cuna.FamilyId,
            d.Tipo,
            d.HashToken,
            d.Estado,
            d.BateriaPct,
            d.UltimoSeq,
            d.UltimoHeartbeat,
            d.FwVersion);
    }

    /// <inheritdoc />
    public async Task<RegisteredDevice?> GetCentralDeviceForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var d = await _client.GetFirstOrDefaultAsync<DeviceRowDto>(
            $"dispositivos?cuna_id=eq.{cunaId}&tipo=eq.central&select=id,device_id,cuna_id,tipo,hash_token,estado,bateria_pct,ultimo_seq,ultimo_heartbeat,fw_version&limit=1",
            ct);
        if (d is null || d.CunaId is null || d.Estado is "revocado" or "sin_dueno")
        {
            return null;
        }

        var cuna = await _client.GetFirstOrDefaultAsync<CunaOwnerDto>(
            $"cunas?id=eq.{cunaId}&select=id,family_id,escala_masa&limit=1",
            ct);
        if (cuna is null)
        {
            return null;
        }

        return new RegisteredDevice(
            d.Id,
            d.DeviceId,
            cunaId,
            cuna.FamilyId,
            d.Tipo,
            d.HashToken ?? "",
            d.Estado,
            d.BateriaPct,
            d.UltimoSeq,
            d.UltimoHeartbeat,
            d.FwVersion);
    }

    /// <inheritdoc />
    public async Task<CribConfig> GetCribConfigAsync(Guid cunaId, Guid familyId, CancellationToken ct)
    {
        var cuna = await _client.GetFirstOrDefaultAsync<CunaOwnerDto>(
            $"cunas?id=eq.{cunaId}&family_id=eq.{familyId}&select=id,family_id,escala_masa&limit=1",
            ct);
        var escalaMasa = cuna?.EscalaMasa ?? 0.20m;

        var u = await _client.GetFirstOrDefaultAsync<UmbralesRowDto>(
            $"umbrales?cuna_id=eq.{cunaId}&select=temp_min,temp_max,hum_min,hum_max,movimiento_umbral,movimiento_alerta_activa,ruido_masa_piso&limit=1",
            ct);

        return new CribConfig(
            cunaId,
            familyId,
            escalaMasa,
            u?.TempMin ?? 18.0m,
            u?.TempMax ?? 26.0m,
            u?.HumMin ?? 40.0m,
            u?.HumMax ?? 60.0m,
            u?.MovimientoUmbral ?? 0.150m,
            u?.MovimientoAlertaActiva ?? true,
            u?.RuidoMasaPiso ?? 0.010m);
    }

    /// <inheritdoc />
    public async Task RecordHeartbeatAsync(
        Guid deviceRowId,
        Guid cunaId,
        string deviceType,
        int? batteryPct,
        string? fwVersion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await _client.PatchAsync(
            $"dispositivos?id=eq.{deviceRowId}",
            new
            {
                estado = "en_linea",
                bateria_pct = batteryPct,
                ultimo_heartbeat = now,
                fw_version = fwVersion
            },
            ct);

        if (deviceType == DeviceTypes.Central)
        {
            await _client.PatchAsync(
                $"cunas?id=eq.{cunaId}",
                new
                {
                    estado_conexion = "en_linea",
                    ultimo_heartbeat = now
                },
                ct);
        }
    }

    /// <inheritdoc />
    /// <remarks>Usa filtro condicional <c>ultimo_seq=lt.{seq}</c> para garantizar atomicidad anti-replay en PostgreSQL.</remarks>
    public async Task<bool> TryAdvanceSeqAsync(Guid deviceRowId, long seq, CancellationToken ct)
    {
        if (seq <= 0)
        {
            return false;
        }

        var updated = await _client.PatchAndReadListAsync<object, DeviceRowDto>(
            $"dispositivos?id=eq.{deviceRowId}&ultimo_seq=lt.{seq}",
            new { ultimo_seq = seq },
            ct);
        return updated.Count > 0;
    }

    /// <inheritdoc />
    public Task SaveTelemetryAsync(TelemetryRecord record, CancellationToken ct)
    {
        return _client.PostAsync(
            "telemetria",
            new
            {
                cuna_id = record.CunaId,
                dispositivo_id = record.DispositivoId == Guid.Empty ? (Guid?)null : record.DispositivoId,
                ts = record.Ts,
                masa_kg = record.MasaKg,
                indice_movimiento = record.IndiceMovimiento,
                temp_c = record.TempC,
                humedad = record.Humedad,
                masa_biberon_g = record.MasaBiberonG,
                temp_biberon_c = record.TempBiberonC,
                bateria_pct = record.BateriaPct,
                seq = record.Seq
            },
            ct,
            preferHeader: "resolution=ignore-duplicates");
    }

    /// <inheritdoc />
    public Task SaveEventAlertAsync(EventAlertRecord record, CancellationToken ct)
    {
        return _client.PostAsync(
            "eventos_alertas",
            new
            {
                id = record.Id,
                cuna_id = record.CunaId,
                dispositivo_id = record.DispositivoId == Guid.Empty ? (Guid?)null : record.DispositivoId,
                tipo = record.Tipo,
                severidad = record.Severidad,
                mensaje = record.Mensaje,
                datos = record.DatosJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(record.DatosJson),
                ts = record.Ts
            },
            ct);
    }

    /// <inheritdoc />
    public Task SaveToyUsageAsync(ToyUsageRecord record, CancellationToken ct)
    {
        return _client.PostAsync(
            "uso_juguetes",
            new
            {
                id = record.Id,
                cuna_id = record.CunaId,
                tipo = record.Tipo,
                perfil_id = record.PerfilId,
                inicio = record.Inicio,
                duracion = record.DuracionSegundos,
                aciertos = record.Aciertos,
                patron = record.Patron,
                velocidad = record.Velocidad
            },
            ct);
    }

    /// <inheritdoc />
    public Task LogSecurityEventAsync(SecurityAuditEntry entry, CancellationToken ct) =>
        _client.LogSecurityEventAsync(entry, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TelemetryRecord>> ListTelemetryForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var rows = await _client.GetListAsync<TelemetryDto>(
            $"telemetria?cuna_id=eq.{cunaId}&order=ts.desc&limit={RecentRowsLimit}",
            ct);
        return rows
            .OrderBy(r => r.Ts)
            .ThenBy(r => r.Seq ?? 0)
            .Select(r => new TelemetryRecord(
                r.CunaId, r.DispositivoId ?? Guid.Empty, r.Ts, r.MasaKg, r.IndiceMovimiento,
                r.TempC, r.Humedad, r.MasaBiberonG, r.TempBiberonC, r.BateriaPct, r.Seq ?? 0))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EventAlertRecord>> ListAlertsForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var rows = await _client.GetListAsync<EventAlertDto>(
            $"eventos_alertas?cuna_id=eq.{cunaId}&order=ts.desc&limit={RecentRowsLimit}",
            ct);
        return rows
            .OrderBy(r => r.Ts)
            .Select(r => new EventAlertRecord(
                r.Id, r.CunaId, r.DispositivoId ?? Guid.Empty, r.Tipo, r.Severidad, r.Mensaje, r.Datos?.GetRawText(), r.Ts))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ToyUsageRecord>> ListToyUsageForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var rows = await _client.GetListAsync<ToyUsageDto>(
            $"uso_juguetes?cuna_id=eq.{cunaId}&order=inicio.desc&limit={RecentRowsLimit}",
            ct);
        return rows
            .OrderBy(r => r.Inicio)
            .Select(r => new ToyUsageRecord(
                r.Id, r.CunaId, r.Tipo, r.PerfilId, r.Inicio, r.Duracion, r.Aciertos, r.Patron, r.Velocidad))
            .ToList();
    }

    #endregion

    #region DTOs PostgREST

    private sealed record DeviceRowDto(
        Guid Id,
        string DeviceId,
        Guid? CunaId,
        string Tipo,
        string? HashToken,
        string Estado,
        int? BateriaPct,
        long UltimoSeq,
        DateTimeOffset? UltimoHeartbeat,
        string? FwVersion);

    private sealed record CunaOwnerDto(Guid Id, Guid FamilyId, decimal EscalaMasa);

    private sealed record UmbralesRowDto(
        decimal TempMin,
        decimal TempMax,
        decimal HumMin,
        decimal HumMax,
        decimal MovimientoUmbral,
        bool MovimientoAlertaActiva,
        decimal RuidoMasaPiso);

    private sealed record TelemetryDto(
        Guid CunaId,
        Guid? DispositivoId,
        DateTimeOffset Ts,
        decimal? MasaKg,
        decimal? IndiceMovimiento,
        decimal? TempC,
        decimal? Humedad,
        decimal? MasaBiberonG,
        decimal? TempBiberonC,
        int? BateriaPct,
        long? Seq);

    private sealed record EventAlertDto(
        Guid Id,
        Guid CunaId,
        Guid? DispositivoId,
        string Tipo,
        string Severidad,
        string? Mensaje,
        JsonElement? Datos,
        DateTimeOffset Ts);

    private sealed record ToyUsageDto(
        Guid Id,
        Guid CunaId,
        string Tipo,
        Guid? PerfilId,
        DateTimeOffset Inicio,
        int Duracion,
        int? Aciertos,
        string? Patron,
        string? Velocidad);

    private sealed record ClaimableDeviceRowDto(
        Guid Id,
        string DeviceId,
        Guid? CunaId,
        string Tipo,
        string? HashReclamo,
        DateTimeOffset? ReclamoExpiraEn,
        bool ReclamoUsado,
        int IntentosReclamo,
        string? HashToken,
        string Estado,
        int? BateriaPct,
        string? FwVersion);

    private sealed record CribDto(
        Guid Id,
        Guid FamilyId,
        string Nombre,
        string EstadoConexion,
        string ModoOperacion,
        DateTimeOffset? ModoAtencionExpiraEn,
        bool LuzEncendida,
        int LuzNivel,
        decimal EscalaMasa,
        DateTimeOffset? UltimoHeartbeat,
        DateTimeOffset CreadoEn)
    {
        public CribRecord ToModel() => new(
            Id, FamilyId, Nombre, EstadoConexion, ModoOperacion,
            ModoAtencionExpiraEn, LuzEncendida, LuzNivel, EscalaMasa, UltimoHeartbeat, CreadoEn);
    }

    private sealed record BabyDto(
        Guid Id,
        Guid FamilyId,
        Guid? CunaId,
        string Nombre,
        DateOnly FechaNacimiento,
        string? Notas,
        DateTimeOffset CreadoEn)
    {
        public BabyRecord ToModel() => new(Id, FamilyId, CunaId, Nombre, FechaNacimiento, Notas, CreadoEn);
    }

    private sealed record CaregiverAuditDto(
        Guid FamilyId,
        Guid? UsuarioId,
        Guid? PerfilId,
        string Accion,
        JsonElement? Detalle,
        DateTimeOffset Ts);

    private sealed record FeedingSessionDto(
        Guid Id,
        Guid BebeId,
        Guid? PerfilId,
        decimal MInicial,
        decimal? MFinal,
        decimal? Ml,
        decimal? TempC,
        bool AlertaTemperatura,
        DateTimeOffset Inicio,
        DateTimeOffset? Fin)
    {
        public FeedingSessionRecord ToModel() => new(
            Id, BebeId, PerfilId, MInicial, MFinal, Ml, TempC, AlertaTemperatura, Inicio, Fin);
    }

    private sealed record SleepSessionDto(
        Guid Id,
        Guid BebeId,
        Guid CunaId,
        string Tipo,
        DateTimeOffset Inicio,
        DateTimeOffset? Fin,
        decimal? MovimientoPromedio,
        int Interrupciones)
    {
        public SleepSessionRecord ToModel() => new(
            Id, BebeId, CunaId, Tipo, Inicio, Fin, MovimientoPromedio, Interrupciones);
    }

    private sealed record SongCatalogDto(
        Guid Id,
        string Codigo,
        string Nombre,
        int DuracionS,
        JsonElement? NotasJson);

    #endregion
}
