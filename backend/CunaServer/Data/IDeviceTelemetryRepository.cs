using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CunaServer.Data;

public interface IDeviceTelemetryRepository
{
    Task<RegisteredDevice?> GetDeviceByDeviceIdAsync(string deviceId, CancellationToken ct);
    Task<RegisteredDevice?> GetCentralDeviceForCribAsync(Guid cunaId, CancellationToken ct);
    Task<CribConfig> GetCribConfigAsync(Guid cunaId, Guid familyId, CancellationToken ct);
    Task RecordHeartbeatAsync(Guid deviceRowId, Guid cunaId, string deviceType, int? batteryPct, string? fwVersion, DateTimeOffset now, CancellationToken ct);
    Task<bool> TryAdvanceSeqAsync(Guid deviceRowId, long seq, CancellationToken ct);
    Task SaveTelemetryAsync(TelemetryRecord record, CancellationToken ct);
    Task SaveEventAlertAsync(EventAlertRecord record, CancellationToken ct);
    Task SaveToyUsageAsync(ToyUsageRecord record, CancellationToken ct);
    Task LogSecurityEventAsync(SecurityAuditEntry entry, CancellationToken ct);

    Task<IReadOnlyList<TelemetryRecord>> ListTelemetryForCribAsync(Guid cunaId, CancellationToken ct);
    Task<IReadOnlyList<EventAlertRecord>> ListAlertsForCribAsync(Guid cunaId, CancellationToken ct);
    Task<IReadOnlyList<ToyUsageRecord>> ListToyUsageForCribAsync(Guid cunaId, CancellationToken ct);
}

public sealed class InMemoryDeviceTelemetryRepository : IDeviceTelemetryRepository
{
    private readonly ConcurrentDictionary<string, RegisteredDevice> _devicesByDeviceId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, CribConfig> _cribConfigs = new();
    private readonly ConcurrentQueue<TelemetryRecord> _telemetry = new();
    private readonly ConcurrentQueue<EventAlertRecord> _alerts = new();
    private readonly ConcurrentQueue<ToyUsageRecord> _toyUsage = new();
    private readonly ConcurrentQueue<SecurityAuditEntry> _securityAudit = new();
    private readonly object _seqLock = new();

    public InMemoryDeviceTelemetryRepository()
    {
        SeedDefaultDemoDevices();
    }

    private void SeedDefaultDemoDevices()
    {
        var familyId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var cunaId = Guid.Parse("aaaaaaaa-c001-4000-8000-000000000001");

        _cribConfigs[cunaId] = new CribConfig(
            cunaId,
            familyId,
            EscalaMasa: 0.20m,
            TempMin: 18.0m,
            TempMax: 26.0m,
            HumMin: 40.0m,
            HumMax: 60.0m,
            MovimientoUmbral: 0.150m,
            MovimientoAlertaActiva: true,
            RuidoMasaPiso: 0.010m);

        var demoDevices = new (Guid Id, string DeviceId, string Tipo, string RawDemoToken)[]
        {
            (Guid.Parse("aaaaaaaa-d001-4000-8000-000000000001"), "sim-central-a", DeviceTypes.Central, "TOKEN-CENTRAL-A-DEMO-000000000000"),
            (Guid.Parse("aaaaaaaa-d001-4000-8000-000000000002"), "sim-colchon-a", DeviceTypes.Colchon, "TOKEN-COLCHON-A-DEMO-000000000000"),
            (Guid.Parse("aaaaaaaa-d001-4000-8000-000000000003"), "sim-movil-a", DeviceTypes.Movil, "TOKEN-MOVIL-A-DEMO-000000000000"),
            (Guid.Parse("aaaaaaaa-d001-4000-8000-000000000004"), "sim-biberon-a", DeviceTypes.Biberon, "TOKEN-BIBERON-A-DEMO-000000000000"),
            (Guid.Parse("aaaaaaaa-d001-4000-8000-000000000005"), "sim-juguete-a", DeviceTypes.Juguete, "TOKEN-JUGUETE-A-DEMO-000000000000")
        };

        foreach (var d in demoDevices)
        {
            _devicesByDeviceId[d.DeviceId] = new RegisteredDevice(
                d.Id,
                d.DeviceId,
                cunaId,
                familyId,
                d.Tipo,
                CryptoTokenHelper.ComputeSha256Hex(d.RawDemoToken),
                "vinculado",
                null,
                0,
                null,
                "1.0.0-sim");
        }
    }

    public void RegisterOrUpdateDevice(RegisteredDevice device)
    {
        _devicesByDeviceId[device.DeviceId] = device;
    }

    public IReadOnlyList<SecurityAuditEntry> GetSecurityAuditSnapshot() => _securityAudit.ToList();

    public Task<RegisteredDevice?> GetDeviceByDeviceIdAsync(string deviceId, CancellationToken ct)
    {
        _devicesByDeviceId.TryGetValue(deviceId, out var device);
        return Task.FromResult(device);
    }

    public Task<RegisteredDevice?> GetCentralDeviceForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var central = _devicesByDeviceId.Values.FirstOrDefault(d =>
            d.CunaId == cunaId &&
            d.Tipo == DeviceTypes.Central &&
            d.Estado is not ("revocado" or "sin_dueno"));
        return Task.FromResult(central);
    }

    public Task<CribConfig> GetCribConfigAsync(Guid cunaId, Guid familyId, CancellationToken ct)
    {
        if (_cribConfigs.TryGetValue(cunaId, out var cfg))
        {
            return Task.FromResult(cfg);
        }

        var defaultCfg = new CribConfig(
            cunaId,
            familyId,
            0.20m,
            18.0m,
            26.0m,
            40.0m,
            60.0m,
            0.150m,
            true,
            0.010m);
        _cribConfigs[cunaId] = defaultCfg;
        return Task.FromResult(defaultCfg);
    }

    public Task RecordHeartbeatAsync(
        Guid deviceRowId,
        Guid cunaId,
        string deviceType,
        int? batteryPct,
        string? fwVersion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        lock (_seqLock)
        {
            var entry = _devicesByDeviceId.Values.FirstOrDefault(d => d.Id == deviceRowId);
            if (entry is not null)
            {
                _devicesByDeviceId[entry.DeviceId] = entry with
                {
                    Estado = "en_linea",
                    BateriaPct = batteryPct ?? entry.BateriaPct,
                    UltimoHeartbeat = now,
                    FwVersion = fwVersion ?? entry.FwVersion
                };
            }
        }
        return Task.CompletedTask;
    }

    public Task<bool> TryAdvanceSeqAsync(Guid deviceRowId, long seq, CancellationToken ct)
    {
        if (seq <= 0)
        {
            return Task.FromResult(false);
        }

        lock (_seqLock)
        {
            var entry = _devicesByDeviceId.Values.FirstOrDefault(d => d.Id == deviceRowId);
            if (entry is null || seq <= entry.UltimoSeq)
            {
                return Task.FromResult(false);
            }

            _devicesByDeviceId[entry.DeviceId] = entry with { UltimoSeq = seq };
            return Task.FromResult(true);
        }
    }

    public Task SaveTelemetryAsync(TelemetryRecord record, CancellationToken ct)
    {
        _telemetry.Enqueue(record);
        return Task.CompletedTask;
    }

    public Task SaveEventAlertAsync(EventAlertRecord record, CancellationToken ct)
    {
        _alerts.Enqueue(record);
        return Task.CompletedTask;
    }

    public Task SaveToyUsageAsync(ToyUsageRecord record, CancellationToken ct)
    {
        _toyUsage.Enqueue(record);
        return Task.CompletedTask;
    }

    public Task LogSecurityEventAsync(SecurityAuditEntry entry, CancellationToken ct)
    {
        _securityAudit.Enqueue(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TelemetryRecord>> ListTelemetryForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var list = _telemetry.Where(t => t.CunaId == cunaId).OrderBy(t => t.Seq).ToList();
        return Task.FromResult<IReadOnlyList<TelemetryRecord>>(list);
    }

    public Task<IReadOnlyList<EventAlertRecord>> ListAlertsForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var list = _alerts.Where(a => a.CunaId == cunaId).OrderBy(a => a.Ts).ToList();
        return Task.FromResult<IReadOnlyList<EventAlertRecord>>(list);
    }

    public Task<IReadOnlyList<ToyUsageRecord>> ListToyUsageForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var list = _toyUsage.Where(u => u.CunaId == cunaId).OrderBy(u => u.Inicio).ToList();
        return Task.FromResult<IReadOnlyList<ToyUsageRecord>>(list);
    }
}

public sealed class PostgrestDeviceTelemetryRepository : IDeviceTelemetryRepository
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PostgrestDeviceTelemetryRepository(HttpClient httpClient, IConfiguration configuration)
    {
        _http = httpClient;
        var supabaseUrl = (configuration["SUPABASE_URL"] ?? "").TrimEnd('/');
        var secretKey = configuration["SUPABASE_SECRET_KEY"] ?? "";

        if (!string.IsNullOrWhiteSpace(supabaseUrl))
        {
            _http.BaseAddress = new Uri($"{supabaseUrl}/rest/v1/");
        }

        if (!string.IsNullOrWhiteSpace(secretKey))
        {
            _http.DefaultRequestHeaders.Remove("apikey");
            _http.DefaultRequestHeaders.Add("apikey", secretKey);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        }
    }

    public async Task<RegisteredDevice?> GetDeviceByDeviceIdAsync(string deviceId, CancellationToken ct)
    {
        var encodedId = Uri.EscapeDataString(deviceId);
        var rows = await _http.GetFromJsonAsync<List<DeviceRowDto>>(
            $"dispositivos?device_id=eq.{encodedId}&select=id,device_id,cuna_id,tipo,hash_token,estado,bateria_pct,ultimo_seq,ultimo_heartbeat,fw_version&limit=1",
            JsonOptions,
            ct);
        var d = rows?.FirstOrDefault();
        if (d is null || d.CunaId is null || string.IsNullOrWhiteSpace(d.HashToken))
        {
            return null;
        }

        var cunas = await _http.GetFromJsonAsync<List<CunaOwnerDto>>(
            $"cunas?id=eq.{d.CunaId}&select=id,family_id,escala_masa&limit=1",
            JsonOptions,
            ct);
        var cuna = cunas?.FirstOrDefault();
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

    public async Task<RegisteredDevice?> GetCentralDeviceForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<DeviceRowDto>>(
            $"dispositivos?cuna_id=eq.{cunaId}&tipo=eq.central&select=id,device_id,cuna_id,tipo,hash_token,estado,bateria_pct,ultimo_seq,ultimo_heartbeat,fw_version&limit=1",
            JsonOptions,
            ct);
        var d = rows?.FirstOrDefault();
        if (d is null || d.CunaId is null || d.Estado is "revocado" or "sin_dueno")
        {
            return null;
        }

        var cunas = await _http.GetFromJsonAsync<List<CunaOwnerDto>>(
            $"cunas?id=eq.{cunaId}&select=id,family_id,escala_masa&limit=1",
            JsonOptions,
            ct);
        var cuna = cunas?.FirstOrDefault();
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

    public async Task<CribConfig> GetCribConfigAsync(Guid cunaId, Guid familyId, CancellationToken ct)
    {
        var cunas = await _http.GetFromJsonAsync<List<CunaOwnerDto>>(
            $"cunas?id=eq.{cunaId}&select=id,family_id,escala_masa&limit=1",
            JsonOptions,
            ct);
        var escalaMasa = cunas?.FirstOrDefault()?.EscalaMasa ?? 0.20m;

        var umbrales = await _http.GetFromJsonAsync<List<UmbralesRowDto>>(
            $"umbrales?cuna_id=eq.{cunaId}&select=temp_min,temp_max,hum_min,hum_max,movimiento_umbral,movimiento_alerta_activa,ruido_masa_piso&limit=1",
            JsonOptions,
            ct);
        var u = umbrales?.FirstOrDefault();

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

    public async Task RecordHeartbeatAsync(
        Guid deviceRowId,
        Guid cunaId,
        string deviceType,
        int? batteryPct,
        string? fwVersion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        using var devReq = new HttpRequestMessage(HttpMethod.Patch, $"dispositivos?id=eq.{deviceRowId}");
        devReq.Content = JsonContent.Create(new
        {
            estado = "en_linea",
            bateria_pct = batteryPct,
            ultimo_heartbeat = now,
            fw_version = fwVersion
        }, options: JsonOptions);
        using var devRes = await _http.SendAsync(devReq, ct);
        devRes.EnsureSuccessStatusCode();

        if (deviceType == DeviceTypes.Central)
        {
            using var cunaReq = new HttpRequestMessage(HttpMethod.Patch, $"cunas?id=eq.{cunaId}");
            cunaReq.Content = JsonContent.Create(new
            {
                estado_conexion = "en_linea",
                ultimo_heartbeat = now
            }, options: JsonOptions);
            using var cunaRes = await _http.SendAsync(cunaReq, ct);
            cunaRes.EnsureSuccessStatusCode();
        }
    }

    public async Task<bool> TryAdvanceSeqAsync(Guid deviceRowId, long seq, CancellationToken ct)
    {
        if (seq <= 0)
        {
            return false;
        }

        using var req = new HttpRequestMessage(
            HttpMethod.Patch,
            $"dispositivos?id=eq.{deviceRowId}&ultimo_seq=lt.{seq}");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new { ultimo_seq = seq }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var updated = await res.Content.ReadFromJsonAsync<List<DeviceRowDto>>(JsonOptions, ct);
        return updated is { Count: > 0 };
    }

    public async Task SaveTelemetryAsync(TelemetryRecord record, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "telemetria");
        req.Headers.Add("Prefer", "resolution=ignore-duplicates");
        req.Content = JsonContent.Create(new
        {
            cuna_id = record.CunaId,
            dispositivo_id = record.DispositivoId,
            ts = record.Ts,
            masa_kg = record.MasaKg,
            indice_movimiento = record.IndiceMovimiento,
            temp_c = record.TempC,
            humedad = record.Humedad,
            masa_biberon_g = record.MasaBiberonG,
            temp_biberon_c = record.TempBiberonC,
            bateria_pct = record.BateriaPct,
            seq = record.Seq
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task SaveEventAlertAsync(EventAlertRecord record, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "eventos_alertas");
        req.Content = JsonContent.Create(new
        {
            id = record.Id,
            cuna_id = record.CunaId,
            dispositivo_id = record.DispositivoId,
            tipo = record.Tipo,
            severidad = record.Severidad,
            mensaje = record.Mensaje,
            datos = record.DatosJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(record.DatosJson),
            ts = record.Ts
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task SaveToyUsageAsync(ToyUsageRecord record, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "uso_juguetes");
        req.Content = JsonContent.Create(new
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
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task LogSecurityEventAsync(SecurityAuditEntry entry, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "bitacora_seguridad");
        req.Content = JsonContent.Create(new
        {
            family_id = entry.FamilyId,
            usuario_id = entry.UsuarioId,
            dispositivo_id = entry.DispositivoId,
            evento = entry.Evento,
            ip_origen = entry.IpOrigen,
            detalle = entry.Detalle is null ? null : new { info = entry.Detalle },
            ts = entry.Ts
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<TelemetryRecord>> ListTelemetryForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<TelemetryDto>>(
            $"telemetria?cuna_id=eq.{cunaId}&order=seq.asc&limit=200",
            JsonOptions,
            ct) ?? [];
        return rows.Select(r => new TelemetryRecord(
            r.CunaId, r.DispositivoId ?? Guid.Empty, r.Ts, r.MasaKg, r.IndiceMovimiento,
            r.TempC, r.Humedad, r.MasaBiberonG, r.TempBiberonC, r.BateriaPct, r.Seq ?? 0)).ToList();
    }

    public async Task<IReadOnlyList<EventAlertRecord>> ListAlertsForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<EventAlertDto>>(
            $"eventos_alertas?cuna_id=eq.{cunaId}&order=ts.asc&limit=200",
            JsonOptions,
            ct) ?? [];
        return rows.Select(r => new EventAlertRecord(
            r.Id, r.CunaId, r.DispositivoId ?? Guid.Empty, r.Tipo, r.Severidad, r.Mensaje, null, r.Ts)).ToList();
    }

    public async Task<IReadOnlyList<ToyUsageRecord>> ListToyUsageForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<ToyUsageDto>>(
            $"uso_juguetes?cuna_id=eq.{cunaId}&order=inicio.asc&limit=200",
            JsonOptions,
            ct) ?? [];
        return rows.Select(r => new ToyUsageRecord(
            r.Id, r.CunaId, r.Tipo, r.PerfilId, r.Inicio, r.Duracion, r.Aciertos, r.Patron, r.Velocidad)).ToList();
    }

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
}
