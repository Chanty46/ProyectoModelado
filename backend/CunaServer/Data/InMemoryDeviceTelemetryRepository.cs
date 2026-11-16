using System.Collections.Concurrent;

namespace CunaServer.Data;

/// <summary>
/// Implementación en memoria de <see cref="IDeviceTelemetryRepository"/> con colas acotadas por cuna
/// y datos semilla de demostración/fábrica para pruebas y desarrollo local.
/// </summary>
public sealed partial class InMemoryDeviceTelemetryRepository : IDeviceTelemetryRepository
{
    private const int MaxTelemetryPerCrib = 2880;
    private const int MaxAlertsPerCrib = 1000;
    private const int MaxToyUsagePerCrib = 1000;
    private const int MaxAuditEntries = 2000;
    private const int MaxClaimAttempts = 5;

    private readonly ConcurrentDictionary<string, RegisteredDevice> _devicesByDeviceId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, string> _deviceIdByRowId = new();
    private readonly ConcurrentDictionary<string, ClaimableDeviceRecord> _claimableDevices = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, CribRecord> _cribs = new();
    private readonly ConcurrentDictionary<Guid, BabyRecord> _babies = new();
    private readonly ConcurrentDictionary<Guid, CribConfig> _cribConfigs = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<TelemetryRecord>> _telemetryByCrib = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<EventAlertRecord>> _alertsByCrib = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<ToyUsageRecord>> _toyUsageByCrib = new();
    private readonly ConcurrentQueue<SecurityAuditEntry> _securityAudit = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<CaregiverAuditLogEntry>> _caregiverActionsByFamily = new();
    private readonly ConcurrentDictionary<Guid, FeedingSessionRecord> _feedingSessions = new();
    private readonly ConcurrentDictionary<Guid, SleepSessionRecord> _sleepSessions = new();
    private readonly List<SongCatalogItem> _songs = new();
    private readonly object _seqLock = new();

    /// <summary>
    /// Inicializa el repositorio en memoria con la cuna demo, los 5 dispositivos simulados,
    /// 5 dispositivos de fábrica sin dueño y el catálogo de canciones.
    /// </summary>
    public InMemoryDeviceTelemetryRepository()
    {
        SeedDefaultDemoDevices();
    }

    #region Datos semilla y utilidades para pruebas

    /// <summary>
    /// Registra un dispositivo nuevo de fábrica (sin dueño) listo para pruebas de emparejamiento RF24/RF25,
    /// almacenando únicamente el hash SHA-256 de su código de reclamo.
    /// </summary>
    public void SeedFactoryUnclaimedDevice(
        string deviceId,
        string tipo,
        string rawClaimCode,
        DateTimeOffset? reclamoExpiraEn = null)
    {
        var row = new ClaimableDeviceRecord(
            Guid.NewGuid(),
            deviceId,
            null,
            null,
            tipo,
            CryptoTokenHelper.ComputeSha256Hex(rawClaimCode),
            reclamoExpiraEn ?? DateTimeOffset.UtcNow.AddHours(24),
            false,
            0,
            null,
            "sin_dueno",
            null,
            "1.0.0");
        _claimableDevices[deviceId] = row;
    }

    /// <summary>
    /// Inserta o reemplaza un dispositivo autenticado directamente en el almacén en memoria (uso en pruebas).
    /// </summary>
    public void RegisterOrUpdateDevice(RegisteredDevice device)
    {
        _devicesByDeviceId[device.DeviceId] = device;
        _deviceIdByRowId[device.Id] = device.DeviceId;
    }

    /// <summary>
    /// Devuelve una instantánea de la bitácora de seguridad en memoria (uso en pruebas).
    /// </summary>
    public IReadOnlyList<SecurityAuditEntry> GetSecurityAuditSnapshot() => _securityAudit.ToList();

    private void SeedDefaultDemoDevices()
    {
        var familyId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var cunaId = Guid.Parse("aaaaaaaa-c001-4000-8000-000000000001");
        var now = DateTimeOffset.UtcNow;

        _cribs[cunaId] = new CribRecord(
            cunaId,
            familyId,
            "Cuna Habitación A",
            "en_linea",
            "monitoreo",
            null,
            false,
            0,
            0.20m,
            now,
            now);

        _cribConfigs[cunaId] = CreateDefaultCribConfig(cunaId, familyId);

        SeedDemoCribDevices(cunaId, familyId);
        SeedFactoryDevices();
        SeedDefaultSongs();
    }

    private void SeedDemoCribDevices(Guid cunaId, Guid familyId)
    {
        var demoDevices = new (Guid Id, string DeviceId, string Tipo, string RawDemoToken, string RawClaimCode)[]
        {
            (Guid.Parse("aaaaaaaa-d001-4000-8000-000000000001"), "sim-central-a", DeviceTypes.Central, "TOKEN-CENTRAL-A-DEMO-000000000000", "CLAIM-CENTRAL-A-DEMO"),
            (Guid.Parse("aaaaaaaa-d001-4000-8000-000000000002"), "sim-colchon-a", DeviceTypes.Colchon, "TOKEN-COLCHON-A-DEMO-000000000000", "CLAIM-COLCHON-A-DEMO"),
            (Guid.Parse("aaaaaaaa-d001-4000-8000-000000000003"), "sim-movil-a", DeviceTypes.Movil, "TOKEN-MOVIL-A-DEMO-000000000000", "CLAIM-MOVIL-A-DEMO"),
            (Guid.Parse("aaaaaaaa-d001-4000-8000-000000000004"), "sim-biberon-a", DeviceTypes.Biberon, "TOKEN-BIBERON-A-DEMO-000000000000", "CLAIM-BIBERON-A-DEMO"),
            (Guid.Parse("aaaaaaaa-d001-4000-8000-000000000005"), "sim-juguete-a", DeviceTypes.Juguete, "TOKEN-JUGUETE-A-DEMO-000000000000", "CLAIM-JUGUETE-A-DEMO")
        };

        foreach (var d in demoDevices)
        {
            var tokenHash = CryptoTokenHelper.ComputeSha256Hex(d.RawDemoToken);
            var claimHash = CryptoTokenHelper.ComputeSha256Hex(d.RawClaimCode);
            var registered = new RegisteredDevice(
                d.Id,
                d.DeviceId,
                cunaId,
                familyId,
                d.Tipo,
                tokenHash,
                "vinculado",
                null,
                0,
                null,
                "1.0.0-sim");
            _devicesByDeviceId[d.DeviceId] = registered;
            _deviceIdByRowId[d.Id] = d.DeviceId;

            _claimableDevices[d.DeviceId] = new ClaimableDeviceRecord(
                d.Id,
                d.DeviceId,
                cunaId,
                familyId,
                d.Tipo,
                claimHash,
                null,
                true,
                0,
                tokenHash,
                "vinculado",
                null,
                "1.0.0-sim");
        }
    }

    private void SeedFactoryDevices()
    {
        SeedFactoryUnclaimedDevice("pico-central-nuevo", DeviceTypes.Central, "RECLAMO-CENTRAL-1001");
        SeedFactoryUnclaimedDevice("pico-colchon-nuevo", DeviceTypes.Colchon, "RECLAMO-COLCHON-1002");
        SeedFactoryUnclaimedDevice("pico-movil-nuevo", DeviceTypes.Movil, "RECLAMO-MOVIL-1003");
        SeedFactoryUnclaimedDevice("pico-biberon-nuevo", DeviceTypes.Biberon, "RECLAMO-BIBERON-1004");
        SeedFactoryUnclaimedDevice("pico-juguete-nuevo", DeviceTypes.Juguete, "RECLAMO-JUGUETE-1005");
    }

    private void SeedDefaultSongs()
    {
        _songs.AddRange(new[]
        {
            new SongCatalogItem(
                Guid.Parse("cccccccc-0001-4000-8000-000000000001"),
                "estrellita",
                "Estrellita ¿Dónde Estás?",
                30,
                """[{"freq_hz":262,"ms":400},{"freq_hz":262,"ms":400},{"freq_hz":392,"ms":400},{"freq_hz":392,"ms":400},{"freq_hz":440,"ms":400},{"freq_hz":440,"ms":400},{"freq_hz":392,"ms":800}]"""),
            new SongCatalogItem(
                Guid.Parse("cccccccc-0002-4000-8000-000000000002"),
                "brahms",
                "Canción de Cuna (Brahms)",
                45,
                """[{"freq_hz":330,"ms":400},{"freq_hz":330,"ms":400},{"freq_hz":392,"ms":800},{"freq_hz":330,"ms":400},{"freq_hz":330,"ms":400},{"freq_hz":392,"ms":800}]"""),
            new SongCatalogItem(
                Guid.Parse("cccccccc-0003-4000-8000-000000000003"),
                "arrorro",
                "Arrorró Mi Niño",
                40,
                """[{"freq_hz":294,"ms":500},{"freq_hz":330,"ms":500},{"freq_hz":349,"ms":400},{"freq_hz":392,"ms":600},{"freq_hz":262,"ms":800}]""")
        });
    }

    #endregion

    #region Autenticación de dispositivos, anti-replay y telemetría (M3)

    /// <inheritdoc />
    public Task<RegisteredDevice?> GetDeviceByDeviceIdAsync(string deviceId, CancellationToken ct)
    {
        _devicesByDeviceId.TryGetValue(deviceId, out var device);
        return Task.FromResult(device);
    }

    /// <inheritdoc />
    public Task<RegisteredDevice?> GetCentralDeviceForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var central = _devicesByDeviceId.Values.FirstOrDefault(d =>
            d.CunaId == cunaId &&
            d.Tipo == DeviceTypes.Central &&
            d.Estado is not ("revocado" or "sin_dueno"));
        return Task.FromResult(central);
    }

    /// <inheritdoc />
    public Task<CribConfig> GetCribConfigAsync(Guid cunaId, Guid familyId, CancellationToken ct)
    {
        if (_cribConfigs.TryGetValue(cunaId, out var cfg))
        {
            return Task.FromResult(cfg);
        }

        var defaultCfg = CreateDefaultCribConfig(cunaId, familyId);
        _cribConfigs[cunaId] = defaultCfg;
        return Task.FromResult(defaultCfg);
    }

    /// <inheritdoc />
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
            if (_deviceIdByRowId.TryGetValue(deviceRowId, out var devId) &&
                _devicesByDeviceId.TryGetValue(devId, out var entry))
            {
                _devicesByDeviceId[entry.DeviceId] = entry with
                {
                    Estado = "en_linea",
                    BateriaPct = batteryPct ?? entry.BateriaPct,
                    UltimoHeartbeat = now,
                    FwVersion = fwVersion ?? entry.FwVersion
                };

                if (_claimableDevices.TryGetValue(entry.DeviceId, out var claimable))
                {
                    _claimableDevices[entry.DeviceId] = claimable with
                    {
                        Estado = "en_linea",
                        BateriaPct = batteryPct ?? claimable.BateriaPct,
                        FwVersion = fwVersion ?? claimable.FwVersion
                    };
                }
            }

            if (deviceType == DeviceTypes.Central && _cribs.TryGetValue(cunaId, out var crib))
            {
                _cribs[cunaId] = crib with
                {
                    EstadoConexion = "en_linea",
                    UltimoHeartbeat = now
                };
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Exige secuencia estrictamente creciente (<c>seq &gt; ultimo_seq</c>) bajo bloqueo para evitar replay.</remarks>
    public Task<bool> TryAdvanceSeqAsync(Guid deviceRowId, long seq, CancellationToken ct)
    {
        if (seq <= 0)
        {
            return Task.FromResult(false);
        }

        lock (_seqLock)
        {
            if (!_deviceIdByRowId.TryGetValue(deviceRowId, out var devId) ||
                !_devicesByDeviceId.TryGetValue(devId, out var entry) ||
                seq <= entry.UltimoSeq)
            {
                return Task.FromResult(false);
            }

            _devicesByDeviceId[entry.DeviceId] = entry with { UltimoSeq = seq };
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    /// <remarks>Mantiene un buffer circular en memoria acotado a <see cref="MaxTelemetryPerCrib"/> lecturas por cuna.</remarks>
    public Task SaveTelemetryAsync(TelemetryRecord record, CancellationToken ct)
    {
        var queue = _telemetryByCrib.GetOrAdd(record.CunaId, _ => new ConcurrentQueue<TelemetryRecord>());
        EnqueueBounded(queue, MaxTelemetryPerCrib, record);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Mantiene un buffer circular en memoria acotado a <see cref="MaxAlertsPerCrib"/> alertas por cuna.</remarks>
    public Task SaveEventAlertAsync(EventAlertRecord record, CancellationToken ct)
    {
        var queue = _alertsByCrib.GetOrAdd(record.CunaId, _ => new ConcurrentQueue<EventAlertRecord>());
        EnqueueBounded(queue, MaxAlertsPerCrib, record);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Mantiene un buffer circular en memoria acotado a <see cref="MaxToyUsagePerCrib"/> usos por cuna.</remarks>
    public Task SaveToyUsageAsync(ToyUsageRecord record, CancellationToken ct)
    {
        var queue = _toyUsageByCrib.GetOrAdd(record.CunaId, _ => new ConcurrentQueue<ToyUsageRecord>());
        EnqueueBounded(queue, MaxToyUsagePerCrib, record);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Acota la bitácora en memoria a <see cref="MaxAuditEntries"/> entradas para evitar crecimiento ilimitado.</remarks>
    public Task LogSecurityEventAsync(SecurityAuditEntry entry, CancellationToken ct)
    {
        EnqueueBounded(_securityAudit, MaxAuditEntries, entry);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TelemetryRecord>> ListTelemetryForCribAsync(Guid cunaId, CancellationToken ct)
    {
        if (!_telemetryByCrib.TryGetValue(cunaId, out var queue))
        {
            return Task.FromResult<IReadOnlyList<TelemetryRecord>>(Array.Empty<TelemetryRecord>());
        }

        var list = queue.OrderBy(t => t.Ts).ThenBy(t => t.Seq).ToList();
        return Task.FromResult<IReadOnlyList<TelemetryRecord>>(list);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EventAlertRecord>> ListAlertsForCribAsync(Guid cunaId, CancellationToken ct)
    {
        if (!_alertsByCrib.TryGetValue(cunaId, out var queue))
        {
            return Task.FromResult<IReadOnlyList<EventAlertRecord>>(Array.Empty<EventAlertRecord>());
        }

        var list = queue.OrderBy(a => a.Ts).ToList();
        return Task.FromResult<IReadOnlyList<EventAlertRecord>>(list);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ToyUsageRecord>> ListToyUsageForCribAsync(Guid cunaId, CancellationToken ct)
    {
        if (!_toyUsageByCrib.TryGetValue(cunaId, out var queue))
        {
            return Task.FromResult<IReadOnlyList<ToyUsageRecord>>(Array.Empty<ToyUsageRecord>());
        }

        var list = queue.OrderBy(u => u.Inicio).ToList();
        return Task.FromResult<IReadOnlyList<ToyUsageRecord>>(list);
    }

    #endregion

    #region Helpers compartidos

    private static CribConfig CreateDefaultCribConfig(Guid cunaId, Guid familyId) =>
        new(
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

    /// <summary>
    /// Inserta un elemento en la cola concurrente y descarta los más antiguos cuando excede <paramref name="maxItems"/>.
    /// </summary>
    private static void EnqueueBounded<T>(ConcurrentQueue<T> queue, int maxItems, T item)
    {
        queue.Enqueue(item);
        while (queue.Count > maxItems && queue.TryDequeue(out _))
        {
        }
    }

    #endregion
}
