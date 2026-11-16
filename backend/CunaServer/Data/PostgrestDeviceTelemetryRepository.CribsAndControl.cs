using System.Text.Json;

namespace CunaServer.Data;

public sealed partial class PostgrestDeviceTelemetryRepository
{
    #region Emparejamiento, cunas y perfil del bebé (M4: RF24–RF28)

    /// <inheritdoc />
    public async Task<ClaimableDeviceRecord?> GetClaimableDeviceAsync(string deviceId, CancellationToken ct)
    {
        var encodedId = Uri.EscapeDataString(deviceId);
        var d = await _client.GetFirstOrDefaultAsync<ClaimableDeviceRowDto>(
            $"dispositivos?device_id=eq.{encodedId}&select=id,device_id,cuna_id,tipo,hash_reclamo,reclamo_expira_en,reclamo_usado,intentos_reclamo,hash_token,estado,bateria_pct,fw_version&limit=1",
            ct);
        if (d is null)
        {
            return null;
        }

        Guid? familyId = null;
        if (d.CunaId.HasValue)
        {
            var cuna = await GetCribByIdAsync(d.CunaId.Value, ct);
            familyId = cuna?.FamilyId;
        }

        return MapClaimableDevice(d, familyId);
    }

    /// <inheritdoc />
    public async Task IncrementClaimAttemptAsync(Guid deviceRowId, CancellationToken ct)
    {
        var current = await _client.GetFirstOrDefaultAsync<ClaimableDeviceRowDto>(
            $"dispositivos?id=eq.{deviceRowId}&select=id,device_id,cuna_id,tipo,hash_reclamo,reclamo_expira_en,reclamo_usado,intentos_reclamo,hash_token,estado,bateria_pct,fw_version&limit=1",
            ct);
        if (current is null || current.IntentosReclamo >= MaxClaimAttempts)
        {
            return;
        }

        await _client.PatchAsync(
            $"dispositivos?id=eq.{deviceRowId}",
            new { intentos_reclamo = current.IntentosReclamo + 1 },
            ct);
    }

    /// <inheritdoc />
    public async Task<CribRecord> CreateCribWithDefaultsAsync(Guid familyId, string nombre, CancellationToken ct)
    {
        var created = await _client.PostAndReadSingleAsync<object, CribDto>(
            "cunas",
            new
            {
                family_id = familyId,
                nombre,
                estado_conexion = "desconectada",
                modo_operacion = "monitoreo",
                escala_masa = 0.20m
            },
            ct);
        var crib = created.ToModel();

        await _client.PostAsync(
            "umbrales",
            new
            {
                cuna_id = crib.Id,
                temp_min = 18.0m,
                temp_max = 26.0m,
                hum_min = 40.0m,
                hum_max = 60.0m,
                movimiento_umbral = 0.150m,
                movimiento_alerta_activa = true,
                ruido_masa_piso = 0.010m
            },
            ct);

        return crib;
    }

    /// <inheritdoc />
    public async Task<CribRecord?> GetCribByIdAsync(Guid cunaId, CancellationToken ct)
    {
        var dto = await _client.GetFirstOrDefaultAsync<CribDto>(
            $"cunas?id=eq.{cunaId}&select=id,family_id,nombre,estado_conexion,modo_operacion,modo_atencion_expira_en,luz_encendida,luz_nivel,escala_masa,ultimo_heartbeat,creado_en&limit=1",
            ct);
        return dto?.ToModel();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CribRecord>> ListCribsByFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await _client.GetListAsync<CribDto>(
            $"cunas?family_id=eq.{familyId}&select=id,family_id,nombre,estado_conexion,modo_operacion,modo_atencion_expira_en,luz_encendida,luz_nivel,escala_masa,ultimo_heartbeat,creado_en&order=creado_en.asc",
            ct);
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <inheritdoc />
    public async Task<CribRecord?> RenameCribAsync(Guid familyId, Guid cunaId, string newName, CancellationToken ct)
    {
        var dto = await _client.PatchAndReadFirstAsync<object, CribDto>(
            $"cunas?id=eq.{cunaId}&family_id=eq.{familyId}",
            new { nombre = newName },
            ct);
        return dto?.ToModel();
    }

    /// <inheritdoc />
    public async Task<ClaimableDeviceRecord> BindClaimedDeviceAsync(
        Guid deviceRowId,
        Guid cunaId,
        Guid familyId,
        string hashToken,
        CancellationToken ct)
    {
        var d = await _client.PatchAndReadSingleAsync<object, ClaimableDeviceRowDto>(
            $"dispositivos?id=eq.{deviceRowId}",
            new
            {
                cuna_id = cunaId,
                reclamo_usado = true,
                hash_token = hashToken,
                estado = "vinculado"
            },
            ct);
        return MapClaimableDevice(d, familyId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClaimableDeviceRecord>> ListDevicesByCribAsync(Guid cunaId, CancellationToken ct)
    {
        var crib = await GetCribByIdAsync(cunaId, ct);
        var rows = await _client.GetListAsync<ClaimableDeviceRowDto>(
            $"dispositivos?cuna_id=eq.{cunaId}&select=id,device_id,cuna_id,tipo,hash_reclamo,reclamo_expira_en,reclamo_usado,intentos_reclamo,hash_token,estado,bateria_pct,fw_version&order=tipo.asc",
            ct);
        return rows.Select(d => MapClaimableDevice(d, crib?.FamilyId)).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> UnlinkCribAndRevokeDevicesAsync(Guid familyId, Guid cunaId, CancellationToken ct)
    {
        var crib = await GetCribByIdAsync(cunaId, ct);
        if (crib is null || crib.FamilyId != familyId)
        {
            return [];
        }

        var devices = await ListDevicesByCribAsync(cunaId, ct);
        var deviceIds = devices.Select(d => d.DeviceId).ToList();

        await _client.PatchAsync(
            $"dispositivos?cuna_id=eq.{cunaId}",
            new
            {
                cuna_id = (Guid?)null,
                hash_token = (string?)null,
                estado = "revocado"
            },
            ct);

        // Conservar el perfil e historial del bebé desasociando cuna_id (RF27)
        await _client.PatchAsync(
            $"bebes?cuna_id=eq.{cunaId}&family_id=eq.{familyId}",
            new { cuna_id = (Guid?)null },
            ct);

        await _client.DeleteAsync($"cunas?id=eq.{cunaId}&family_id=eq.{familyId}", ct, ensureSuccess: true);

        return deviceIds;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BabyRecord>> ListBabiesByFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await _client.GetListAsync<BabyDto>(
            $"bebes?family_id=eq.{familyId}&select=id,family_id,cuna_id,nombre,fecha_nacimiento,notas,creado_en&order=creado_en.asc",
            ct);
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <inheritdoc />
    public async Task<BabyRecord> CreateBabyAsync(
        Guid familyId,
        Guid cunaId,
        string nombre,
        DateOnly fechaNacimiento,
        string? notas,
        CancellationToken ct)
    {
        var created = await _client.PostAndReadSingleAsync<object, BabyDto>(
            "bebes",
            new
            {
                family_id = familyId,
                cuna_id = cunaId,
                nombre,
                fecha_nacimiento = fechaNacimiento,
                notas
            },
            ct);
        return created.ToModel();
    }

    /// <inheritdoc />
    public async Task<BabyRecord?> UpdateBabyAsync(
        Guid familyId,
        Guid bebeId,
        Guid cunaId,
        string nombre,
        DateOnly fechaNacimiento,
        string? notas,
        CancellationToken ct)
    {
        var dto = await _client.PatchAndReadFirstAsync<object, BabyDto>(
            $"bebes?id=eq.{bebeId}&family_id=eq.{familyId}",
            new
            {
                cuna_id = cunaId,
                nombre,
                fecha_nacimiento = fechaNacimiento,
                notas
            },
            ct);
        return dto?.ToModel();
    }

    #endregion

    #region Cuna en vivo y bitácora del cuidador (M5: RF04, RF05, RF07, RF08, RF09, RF20, RF29)

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegisteredDevice>> ListActiveDevicesForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var rows = await _client.GetListAsync<DeviceRowDto>(
            $"dispositivos?cuna_id=eq.{cunaId}&select=id,device_id,cuna_id,tipo,hash_token,estado,bateria_pct,ultimo_seq,ultimo_heartbeat,fw_version",
            ct);
        if (rows.Count == 0)
        {
            return Array.Empty<RegisteredDevice>();
        }

        var crib = await GetCribByIdAsync(cunaId, ct);
        if (crib is null)
        {
            return Array.Empty<RegisteredDevice>();
        }

        return rows
            .Where(r => r.Estado is not ("revocado" or "sin_dueno") && !string.IsNullOrEmpty(r.HashToken))
            .Select(r => new RegisteredDevice(
                r.Id,
                r.DeviceId,
                cunaId,
                crib.FamilyId,
                r.Tipo,
                r.HashToken!,
                r.Estado,
                r.BateriaPct,
                r.UltimoSeq,
                r.UltimoHeartbeat,
                r.FwVersion))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<CribRecord?> UpdateCribModeAsync(
        Guid cunaId,
        string modoOperacion,
        DateTimeOffset? modoAtencionExpiraEn,
        CancellationToken ct)
    {
        var dto = await _client.PatchAndReadFirstAsync<object, CribDto>(
            $"cunas?id=eq.{cunaId}",
            new
            {
                modo_operacion = modoOperacion,
                modo_atencion_expira_en = modoAtencionExpiraEn
            },
            ct);
        return dto?.ToModel();
    }

    /// <inheritdoc />
    public async Task<CribRecord?> UpdateCribLightAsync(
        Guid cunaId,
        bool luzEncendida,
        int luzNivel,
        CancellationToken ct)
    {
        var dto = await _client.PatchAndReadFirstAsync<object, CribDto>(
            $"cunas?id=eq.{cunaId}",
            new
            {
                luz_encendida = luzEncendida,
                luz_nivel = luzNivel
            },
            ct);
        return dto?.ToModel();
    }

    /// <inheritdoc />
    public async Task<CribConfig> UpdateCribThresholdsAsync(
        Guid cunaId,
        Guid familyId,
        UpdateCribThresholdsRequest request,
        CancellationToken ct)
    {
        await _client.PatchAsync(
            $"cunas?id=eq.{cunaId}&family_id=eq.{familyId}",
            new { escala_masa = request.EscalaMasa },
            ct);

        await _client.PatchAsync(
            $"umbrales?cuna_id=eq.{cunaId}",
            new
            {
                temp_min = request.TempMin,
                temp_max = request.TempMax,
                hum_min = request.HumMin,
                hum_max = request.HumMax,
                movimiento_umbral = request.MovimientoUmbral,
                movimiento_alerta_activa = request.MovimientoAlertaActiva,
                ruido_masa_piso = request.RuidoMasaPiso,
                actualizado_en = DateTimeOffset.UtcNow
            },
            ct);

        return new CribConfig(
            cunaId,
            familyId,
            request.EscalaMasa,
            request.TempMin,
            request.TempMax,
            request.HumMin,
            request.HumMax,
            request.MovimientoUmbral,
            request.MovimientoAlertaActiva,
            request.RuidoMasaPiso);
    }

    /// <inheritdoc />
    public async Task<CribRecord?> UpdateCribConnectionStateAsync(
        Guid cunaId,
        string estadoConexion,
        CancellationToken ct)
    {
        var dto = await _client.PatchAndReadFirstAsync<object, CribDto>(
            $"cunas?id=eq.{cunaId}",
            new { estado_conexion = estadoConexion },
            ct);
        var updated = dto?.ToModel();

        if (updated is not null && estadoConexion == "desconectada")
        {
            await _client.PatchAsync(
                $"dispositivos?cuna_id=eq.{cunaId}",
                new { estado = "desconectado" },
                ct);
        }

        return updated;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CribRecord>> ListAllCribsAsync(CancellationToken ct)
    {
        var rows = await _client.GetListAsync<CribDto>("cunas?order=creado_en.asc", ct);
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <inheritdoc />
    public async Task LogCaregiverActionAsync(CaregiverAuditLogEntry entry, CancellationToken ct)
    {
        using var detalleDoc = entry.DetalleJson is not null ? JsonDocument.Parse(entry.DetalleJson) : null;
        await _client.PostAsync(
            "bitacora",
            new
            {
                family_id = entry.FamilyId,
                usuario_id = entry.UsuarioId,
                perfil_id = entry.PerfilId,
                accion = entry.Accion,
                detalle = detalleDoc is not null ? detalleDoc.RootElement : (JsonElement?)null,
                ts = entry.Ts
            },
            ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CaregiverAuditLogEntry>> ListCaregiverActionsAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await _client.GetListAsync<CaregiverAuditDto>(
            $"bitacora?family_id=eq.{familyId}&order=ts.asc",
            ct);
        return rows.Select(r => new CaregiverAuditLogEntry(
            r.FamilyId,
            r.UsuarioId,
            r.PerfilId,
            r.Accion,
            r.Detalle?.GetRawText(),
            r.Ts)).ToList();
    }

    #endregion

    private static ClaimableDeviceRecord MapClaimableDevice(ClaimableDeviceRowDto d, Guid? familyId) =>
        new(
            d.Id,
            d.DeviceId,
            d.CunaId,
            familyId,
            d.Tipo,
            d.HashReclamo,
            d.ReclamoExpiraEn,
            d.ReclamoUsado,
            d.IntentosReclamo,
            d.HashToken,
            d.Estado,
            d.BateriaPct,
            d.FwVersion);
}
