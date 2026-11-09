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

    // M4: Emparejamiento, cunas y perfil del bebé (RF24–RF28)
    Task<ClaimableDeviceRecord?> GetClaimableDeviceAsync(string deviceId, CancellationToken ct);
    Task IncrementClaimAttemptAsync(Guid deviceRowId, CancellationToken ct);
    Task<CribRecord> CreateCribWithDefaultsAsync(Guid familyId, string nombre, CancellationToken ct);
    Task<CribRecord?> GetCribByIdAsync(Guid cunaId, CancellationToken ct);
    Task<IReadOnlyList<CribRecord>> ListCribsByFamilyAsync(Guid familyId, CancellationToken ct);
    Task<CribRecord?> RenameCribAsync(Guid familyId, Guid cunaId, string newName, CancellationToken ct);
    Task<ClaimableDeviceRecord> BindClaimedDeviceAsync(
        Guid deviceRowId,
        Guid cunaId,
        Guid familyId,
        string hashToken,
        CancellationToken ct);
    Task<IReadOnlyList<ClaimableDeviceRecord>> ListDevicesByCribAsync(Guid cunaId, CancellationToken ct);
    Task<IReadOnlyList<string>> UnlinkCribAndRevokeDevicesAsync(Guid familyId, Guid cunaId, CancellationToken ct);

    Task<IReadOnlyList<BabyRecord>> ListBabiesByFamilyAsync(Guid familyId, CancellationToken ct);
    Task<BabyRecord> CreateBabyAsync(Guid familyId, Guid cunaId, string nombre, DateOnly fechaNacimiento, string? notas, CancellationToken ct);
    Task<BabyRecord?> UpdateBabyAsync(Guid familyId, Guid bebeId, Guid cunaId, string nombre, DateOnly fechaNacimiento, string? notas, CancellationToken ct);

    // M5: Cuna en vivo (RF04, RF05, RF07, RF08, RF09, RF20, RF29)
    Task<IReadOnlyList<RegisteredDevice>> ListActiveDevicesForCribAsync(Guid cunaId, CancellationToken ct);
    Task<CribRecord?> UpdateCribModeAsync(Guid cunaId, string modoOperacion, DateTimeOffset? modoAtencionExpiraEn, CancellationToken ct);
    Task<CribRecord?> UpdateCribLightAsync(Guid cunaId, bool luzEncendida, int luzNivel, CancellationToken ct);
    Task<CribConfig> UpdateCribThresholdsAsync(Guid cunaId, Guid familyId, UpdateCribThresholdsRequest request, CancellationToken ct);
    Task<CribRecord?> UpdateCribConnectionStateAsync(Guid cunaId, string estadoConexion, CancellationToken ct);
    Task<IReadOnlyList<CribRecord>> ListAllCribsAsync(CancellationToken ct);
    Task LogCaregiverActionAsync(CaregiverAuditLogEntry entry, CancellationToken ct);
    Task<IReadOnlyList<CaregiverAuditLogEntry>> ListCaregiverActionsAsync(Guid familyId, CancellationToken ct);

    // M7: Alimentación, sueño, canciones, juguetes e historiales (RF12–RF17, RF30–RF37)
    Task<BabyRecord?> GetBabyByIdAsync(Guid bebeId, CancellationToken ct);
    Task<FeedingSessionRecord> CreateFeedingSessionAsync(
        Guid bebeId,
        Guid? perfilId,
        decimal masaInicialG,
        decimal? tempC,
        bool alertaTemperatura,
        DateTimeOffset inicio,
        CancellationToken ct);
    Task<FeedingSessionRecord?> GetFeedingSessionByIdAsync(Guid sessionId, CancellationToken ct);
    Task<FeedingSessionRecord?> CompleteFeedingSessionAsync(
        Guid sessionId,
        decimal masaFinalG,
        decimal mlConsumidos,
        DateTimeOffset fin,
        CancellationToken ct);
    Task<IReadOnlyList<FeedingSessionRecord>> ListFeedingSessionsByBabyAsync(Guid bebeId, CancellationToken ct);
    Task<SleepSessionRecord> CreateSleepSessionAsync(
        Guid bebeId,
        Guid cunaId,
        string tipo,
        DateTimeOffset inicio,
        DateTimeOffset? fin,
        decimal? movimientoPromedio,
        int interrupciones,
        CancellationToken ct);
    Task<IReadOnlyList<SleepSessionRecord>> ListSleepSessionsByBabyAsync(Guid bebeId, CancellationToken ct);
    Task<IReadOnlyList<SongCatalogItem>> ListSongCatalogAsync(CancellationToken ct);
}

public sealed class InMemoryDeviceTelemetryRepository : IDeviceTelemetryRepository
{
    private readonly ConcurrentDictionary<string, RegisteredDevice> _devicesByDeviceId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ClaimableDeviceRecord> _claimableDevices = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, CribRecord> _cribs = new();
    private readonly ConcurrentDictionary<Guid, BabyRecord> _babies = new();
    private readonly ConcurrentDictionary<Guid, CribConfig> _cribConfigs = new();
    private readonly ConcurrentQueue<TelemetryRecord> _telemetry = new();
    private readonly ConcurrentQueue<EventAlertRecord> _alerts = new();
    private readonly ConcurrentQueue<ToyUsageRecord> _toyUsage = new();
    private readonly ConcurrentQueue<SecurityAuditEntry> _securityAudit = new();
    private readonly ConcurrentQueue<CaregiverAuditLogEntry> _caregiverActions = new();
    private readonly ConcurrentDictionary<Guid, FeedingSessionRecord> _feedingSessions = new();
    private readonly ConcurrentDictionary<Guid, SleepSessionRecord> _sleepSessions = new();
    private readonly List<SongCatalogItem> _songs = new();
    private readonly object _seqLock = new();

    public InMemoryDeviceTelemetryRepository()
    {
        SeedDefaultDemoDevices();
    }

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
            _devicesByDeviceId[d.DeviceId] = new RegisteredDevice(
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

        // Dispositivos nuevos de fábrica (sin dueño) listos para pruebas de emparejamiento RF24/RF25
        SeedFactoryUnclaimedDevice("pico-central-nuevo", DeviceTypes.Central, "RECLAMO-CENTRAL-1001");
        SeedFactoryUnclaimedDevice("pico-colchon-nuevo", DeviceTypes.Colchon, "RECLAMO-COLCHON-1002");
        SeedFactoryUnclaimedDevice("pico-movil-nuevo", DeviceTypes.Movil, "RECLAMO-MOVIL-1003");
        SeedFactoryUnclaimedDevice("pico-biberon-nuevo", DeviceTypes.Biberon, "RECLAMO-BIBERON-1004");
        SeedFactoryUnclaimedDevice("pico-juguete-nuevo", DeviceTypes.Juguete, "RECLAMO-JUGUETE-1005");

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
                "cancion_cuna_brahms",
                "Canción de Cuna (Brahms)",
                45,
                """[{"freq_hz":330,"ms":400},{"freq_hz":330,"ms":400},{"freq_hz":392,"ms":800},{"freq_hz":330,"ms":400},{"freq_hz":330,"ms":400},{"freq_hz":392,"ms":800}]"""),
            new SongCatalogItem(
                Guid.Parse("cccccccc-0003-4000-8000-000000000003"),
                "arrullo_suave",
                "Arrullo Nocturno Suave",
                30,
                """[{"freq_hz":294,"ms":500},{"freq_hz":330,"ms":500},{"freq_hz":262,"ms":1000}]""")
        });
    }

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

    public Task<ClaimableDeviceRecord?> GetClaimableDeviceAsync(string deviceId, CancellationToken ct)
    {
        _claimableDevices.TryGetValue(deviceId, out var dev);
        return Task.FromResult(dev);
    }

    public Task IncrementClaimAttemptAsync(Guid deviceRowId, CancellationToken ct)
    {
        lock (_seqLock)
        {
            var entry = _claimableDevices.Values.FirstOrDefault(d => d.Id == deviceRowId);
            if (entry is not null && entry.IntentosReclamo < 5)
            {
                _claimableDevices[entry.DeviceId] = entry with { IntentosReclamo = entry.IntentosReclamo + 1 };
            }
        }
        return Task.CompletedTask;
    }

    public Task<CribRecord> CreateCribWithDefaultsAsync(Guid familyId, string nombre, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var crib = new CribRecord(
            Guid.NewGuid(),
            familyId,
            nombre,
            "desconectada",
            "monitoreo",
            null,
            false,
            0,
            0.20m,
            null,
            now);

        _cribs[crib.Id] = crib;
        _cribConfigs[crib.Id] = new CribConfig(
            crib.Id,
            familyId,
            0.20m,
            18.0m,
            26.0m,
            40.0m,
            60.0m,
            0.150m,
            true,
            0.010m);

        return Task.FromResult(crib);
    }

    public Task<CribRecord?> GetCribByIdAsync(Guid cunaId, CancellationToken ct)
    {
        _cribs.TryGetValue(cunaId, out var crib);
        return Task.FromResult(crib);
    }

    public Task<IReadOnlyList<CribRecord>> ListCribsByFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var list = _cribs.Values
            .Where(c => c.FamilyId == familyId)
            .OrderBy(c => c.CreadoEn)
            .ToList();
        return Task.FromResult<IReadOnlyList<CribRecord>>(list);
    }

    public Task<CribRecord?> RenameCribAsync(Guid familyId, Guid cunaId, string newName, CancellationToken ct)
    {
        lock (_seqLock)
        {
            if (!_cribs.TryGetValue(cunaId, out var crib) || crib.FamilyId != familyId)
            {
                return Task.FromResult<CribRecord?>(null);
            }

            var updated = crib with { Nombre = newName };
            _cribs[cunaId] = updated;
            return Task.FromResult<CribRecord?>(updated);
        }
    }

    public Task<ClaimableDeviceRecord> BindClaimedDeviceAsync(
        Guid deviceRowId,
        Guid cunaId,
        Guid familyId,
        string hashToken,
        CancellationToken ct)
    {
        lock (_seqLock)
        {
            var current = _claimableDevices.Values.First(d => d.Id == deviceRowId);
            var updatedClaimable = current with
            {
                CunaId = cunaId,
                FamilyId = familyId,
                ReclamoUsado = true,
                HashToken = hashToken,
                Estado = "vinculado"
            };
            _claimableDevices[current.DeviceId] = updatedClaimable;

            _devicesByDeviceId[current.DeviceId] = new RegisteredDevice(
                current.Id,
                current.DeviceId,
                cunaId,
                familyId,
                current.Tipo,
                hashToken,
                "vinculado",
                current.BateriaPct,
                0,
                null,
                current.FwVersion);

            return Task.FromResult(updatedClaimable);
        }
    }

    public Task<IReadOnlyList<ClaimableDeviceRecord>> ListDevicesByCribAsync(Guid cunaId, CancellationToken ct)
    {
        var list = _claimableDevices.Values
            .Where(d => d.CunaId == cunaId && d.Estado != "revocado")
            .OrderBy(d => d.Tipo)
            .ToList();
        return Task.FromResult<IReadOnlyList<ClaimableDeviceRecord>>(list);
    }

    public Task<IReadOnlyList<string>> UnlinkCribAndRevokeDevicesAsync(Guid familyId, Guid cunaId, CancellationToken ct)
    {
        lock (_seqLock)
        {
            if (!_cribs.TryGetValue(cunaId, out var crib) || crib.FamilyId != familyId)
            {
                return Task.FromResult<IReadOnlyList<string>>([]);
            }

            var revokedIds = new List<string>();
            foreach (var dev in _claimableDevices.Values.Where(d => d.CunaId == cunaId).ToList())
            {
                revokedIds.Add(dev.DeviceId);
                _claimableDevices[dev.DeviceId] = dev with
                {
                    CunaId = null,
                    FamilyId = null,
                    HashToken = null,
                    Estado = "revocado"
                };

                if (_devicesByDeviceId.TryGetValue(dev.DeviceId, out var reg))
                {
                    _devicesByDeviceId[dev.DeviceId] = reg with
                    {
                        HashToken = "",
                        Estado = "revocado"
                    };
                }
            }

            // Conservar el perfil e historial del bebé desasociando cuna_id (RF27)
            foreach (var baby in _babies.Values.Where(b => b.CunaId == cunaId).ToList())
            {
                _babies[baby.Id] = baby with { CunaId = null };
            }

            _cribs.TryRemove(cunaId, out _);
            _cribConfigs.TryRemove(cunaId, out _);

            return Task.FromResult<IReadOnlyList<string>>(revokedIds);
        }
    }

    public Task<IReadOnlyList<BabyRecord>> ListBabiesByFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var list = _babies.Values
            .Where(b => b.FamilyId == familyId)
            .OrderBy(b => b.CreadoEn)
            .ToList();
        return Task.FromResult<IReadOnlyList<BabyRecord>>(list);
    }

    public Task<BabyRecord> CreateBabyAsync(
        Guid familyId,
        Guid cunaId,
        string nombre,
        DateOnly fechaNacimiento,
        string? notas,
        CancellationToken ct)
    {
        var baby = new BabyRecord(
            Guid.NewGuid(),
            familyId,
            cunaId,
            nombre,
            fechaNacimiento,
            notas,
            DateTimeOffset.UtcNow);
        _babies[baby.Id] = baby;
        return Task.FromResult(baby);
    }

    public Task<BabyRecord?> UpdateBabyAsync(
        Guid familyId,
        Guid bebeId,
        Guid cunaId,
        string nombre,
        DateOnly fechaNacimiento,
        string? notas,
        CancellationToken ct)
    {
        lock (_seqLock)
        {
            if (!_babies.TryGetValue(bebeId, out var current) || current.FamilyId != familyId)
            {
                return Task.FromResult<BabyRecord?>(null);
            }

            var updated = current with
            {
                CunaId = cunaId,
                Nombre = nombre,
                FechaNacimiento = fechaNacimiento,
                Notas = notas
            };
            _babies[bebeId] = updated;
            return Task.FromResult<BabyRecord?>(updated);
        }
    }

    public Task<IReadOnlyList<RegisteredDevice>> ListActiveDevicesForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var list = _devicesByDeviceId.Values
            .Where(d => d.CunaId == cunaId && d.Estado is not ("revocado" or "sin_dueno"))
            .ToList();
        return Task.FromResult<IReadOnlyList<RegisteredDevice>>(list);
    }

    public Task<CribRecord?> UpdateCribModeAsync(
        Guid cunaId,
        string modoOperacion,
        DateTimeOffset? modoAtencionExpiraEn,
        CancellationToken ct)
    {
        lock (_seqLock)
        {
            if (!_cribs.TryGetValue(cunaId, out var current))
            {
                return Task.FromResult<CribRecord?>(null);
            }

            var updated = current with
            {
                ModoOperacion = modoOperacion,
                ModoAtencionExpiraEn = modoAtencionExpiraEn
            };
            _cribs[cunaId] = updated;
            return Task.FromResult<CribRecord?>(updated);
        }
    }

    public Task<CribRecord?> UpdateCribLightAsync(
        Guid cunaId,
        bool luzEncendida,
        int luzNivel,
        CancellationToken ct)
    {
        lock (_seqLock)
        {
            if (!_cribs.TryGetValue(cunaId, out var current))
            {
                return Task.FromResult<CribRecord?>(null);
            }

            var updated = current with
            {
                LuzEncendida = luzEncendida,
                LuzNivel = luzNivel
            };
            _cribs[cunaId] = updated;
            return Task.FromResult<CribRecord?>(updated);
        }
    }

    public Task<CribConfig> UpdateCribThresholdsAsync(
        Guid cunaId,
        Guid familyId,
        UpdateCribThresholdsRequest request,
        CancellationToken ct)
    {
        lock (_seqLock)
        {
            var updatedCfg = new CribConfig(
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
            _cribConfigs[cunaId] = updatedCfg;

            if (_cribs.TryGetValue(cunaId, out var crib))
            {
                _cribs[cunaId] = crib with { EscalaMasa = request.EscalaMasa };
            }

            return Task.FromResult(updatedCfg);
        }
    }

    public Task<CribRecord?> UpdateCribConnectionStateAsync(
        Guid cunaId,
        string estadoConexion,
        CancellationToken ct)
    {
        lock (_seqLock)
        {
            if (!_cribs.TryGetValue(cunaId, out var current))
            {
                return Task.FromResult<CribRecord?>(null);
            }

            var updated = current with { EstadoConexion = estadoConexion };
            _cribs[cunaId] = updated;

            if (estadoConexion == "desconectada")
            {
                foreach (var dev in _devicesByDeviceId.Values.Where(d => d.CunaId == cunaId).ToList())
                {
                    _devicesByDeviceId[dev.DeviceId] = dev with { Estado = "desconectado" };
                    if (_claimableDevices.TryGetValue(dev.DeviceId, out var cl))
                    {
                        _claimableDevices[dev.DeviceId] = cl with { Estado = "desconectado" };
                    }
                }
            }

            return Task.FromResult<CribRecord?>(updated);
        }
    }

    public Task<IReadOnlyList<CribRecord>> ListAllCribsAsync(CancellationToken ct)
    {
        var list = _cribs.Values.OrderBy(c => c.CreadoEn).ToList();
        return Task.FromResult<IReadOnlyList<CribRecord>>(list);
    }

    public Task LogCaregiverActionAsync(CaregiverAuditLogEntry entry, CancellationToken ct)
    {
        _caregiverActions.Enqueue(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CaregiverAuditLogEntry>> ListCaregiverActionsAsync(Guid familyId, CancellationToken ct)
    {
        var list = _caregiverActions.Where(a => a.FamilyId == familyId).OrderBy(a => a.Ts).ToList();
        return Task.FromResult<IReadOnlyList<CaregiverAuditLogEntry>>(list);
    }

    public Task<BabyRecord?> GetBabyByIdAsync(Guid bebeId, CancellationToken ct)
    {
        _babies.TryGetValue(bebeId, out var baby);
        return Task.FromResult(baby);
    }

    public Task<FeedingSessionRecord> CreateFeedingSessionAsync(
        Guid bebeId,
        Guid? perfilId,
        decimal masaInicialG,
        decimal? tempC,
        bool alertaTemperatura,
        DateTimeOffset inicio,
        CancellationToken ct)
    {
        var record = new FeedingSessionRecord(
            Guid.NewGuid(),
            bebeId,
            perfilId,
            masaInicialG,
            null,
            null,
            tempC,
            alertaTemperatura,
            inicio,
            null);
        _feedingSessions[record.Id] = record;
        return Task.FromResult(record);
    }

    public Task<FeedingSessionRecord?> GetFeedingSessionByIdAsync(Guid sessionId, CancellationToken ct)
    {
        _feedingSessions.TryGetValue(sessionId, out var record);
        return Task.FromResult(record);
    }

    public Task<FeedingSessionRecord?> CompleteFeedingSessionAsync(
        Guid sessionId,
        decimal masaFinalG,
        decimal mlConsumidos,
        DateTimeOffset fin,
        CancellationToken ct)
    {
        lock (_seqLock)
        {
            if (!_feedingSessions.TryGetValue(sessionId, out var current))
            {
                return Task.FromResult<FeedingSessionRecord?>(null);
            }

            var updated = current with
            {
                MasaFinalG = masaFinalG,
                MlConsumidos = mlConsumidos,
                Fin = fin
            };
            _feedingSessions[sessionId] = updated;
            return Task.FromResult<FeedingSessionRecord?>(updated);
        }
    }

    public Task<IReadOnlyList<FeedingSessionRecord>> ListFeedingSessionsByBabyAsync(Guid bebeId, CancellationToken ct)
    {
        var list = _feedingSessions.Values
            .Where(s => s.BebeId == bebeId)
            .OrderByDescending(s => s.Inicio)
            .ToList();
        return Task.FromResult<IReadOnlyList<FeedingSessionRecord>>(list);
    }

    public Task<SleepSessionRecord> CreateSleepSessionAsync(
        Guid bebeId,
        Guid cunaId,
        string tipo,
        DateTimeOffset inicio,
        DateTimeOffset? fin,
        decimal? movimientoPromedio,
        int interrupciones,
        CancellationToken ct)
    {
        var record = new SleepSessionRecord(
            Guid.NewGuid(),
            bebeId,
            cunaId,
            tipo,
            inicio,
            fin,
            movimientoPromedio,
            interrupciones);
        _sleepSessions[record.Id] = record;
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<SleepSessionRecord>> ListSleepSessionsByBabyAsync(Guid bebeId, CancellationToken ct)
    {
        var list = _sleepSessions.Values
            .Where(s => s.BebeId == bebeId)
            .OrderByDescending(s => s.Inicio)
            .ToList();
        return Task.FromResult<IReadOnlyList<SleepSessionRecord>>(list);
    }

    public Task<IReadOnlyList<SongCatalogItem>> ListSongCatalogAsync(CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<SongCatalogItem>>(_songs.ToList());
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

    public async Task<ClaimableDeviceRecord?> GetClaimableDeviceAsync(string deviceId, CancellationToken ct)
    {
        var encodedId = Uri.EscapeDataString(deviceId);
        var rows = await _http.GetFromJsonAsync<List<ClaimableDeviceRowDto>>(
            $"dispositivos?device_id=eq.{encodedId}&select=id,device_id,cuna_id,tipo,hash_reclamo,reclamo_expira_en,reclamo_usado,intentos_reclamo,hash_token,estado,bateria_pct,fw_version&limit=1",
            JsonOptions,
            ct);
        var d = rows?.FirstOrDefault();
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

        return new ClaimableDeviceRecord(
            d.Id, d.DeviceId, d.CunaId, familyId, d.Tipo, d.HashReclamo, d.ReclamoExpiraEn,
            d.ReclamoUsado, d.IntentosReclamo, d.HashToken, d.Estado, d.BateriaPct, d.FwVersion);
    }

    public async Task IncrementClaimAttemptAsync(Guid deviceRowId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<ClaimableDeviceRowDto>>(
            $"dispositivos?id=eq.{deviceRowId}&select=id,device_id,cuna_id,tipo,hash_reclamo,reclamo_expira_en,reclamo_usado,intentos_reclamo,hash_token,estado,bateria_pct,fw_version&limit=1",
            JsonOptions,
            ct);
        var current = rows?.FirstOrDefault();
        if (current is null || current.IntentosReclamo >= 5)
        {
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Patch, $"dispositivos?id=eq.{deviceRowId}");
        req.Content = JsonContent.Create(new { intentos_reclamo = current.IntentosReclamo + 1 }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task<CribRecord> CreateCribWithDefaultsAsync(Guid familyId, string nombre, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "cunas");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new
        {
            family_id = familyId,
            nombre,
            estado_conexion = "desconectada",
            modo_operacion = "monitoreo",
            escala_masa = 0.20m
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var created = await res.Content.ReadFromJsonAsync<List<CribDto>>(JsonOptions, ct);
        var crib = created!.Single().ToModel();

        using var umbReq = new HttpRequestMessage(HttpMethod.Post, "umbrales");
        umbReq.Content = JsonContent.Create(new
        {
            cuna_id = crib.Id,
            temp_min = 18.0m,
            temp_max = 26.0m,
            hum_min = 40.0m,
            hum_max = 60.0m,
            movimiento_umbral = 0.150m,
            movimiento_alerta_activa = true,
            ruido_masa_piso = 0.010m
        }, options: JsonOptions);
        using var umbRes = await _http.SendAsync(umbReq, ct);
        umbRes.EnsureSuccessStatusCode();

        return crib;
    }

    public async Task<CribRecord?> GetCribByIdAsync(Guid cunaId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<CribDto>>(
            $"cunas?id=eq.{cunaId}&select=id,family_id,nombre,estado_conexion,modo_operacion,modo_atencion_expira_en,luz_encendida,luz_nivel,escala_masa,ultimo_heartbeat,creado_en&limit=1",
            JsonOptions,
            ct);
        return rows?.FirstOrDefault()?.ToModel();
    }

    public async Task<IReadOnlyList<CribRecord>> ListCribsByFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<CribDto>>(
            $"cunas?family_id=eq.{familyId}&select=id,family_id,nombre,estado_conexion,modo_operacion,modo_atencion_expira_en,luz_encendida,luz_nivel,escala_masa,ultimo_heartbeat,creado_en&order=creado_en.asc",
            JsonOptions,
            ct) ?? [];
        return rows.Select(r => r.ToModel()).ToList();
    }

    public async Task<CribRecord?> RenameCribAsync(Guid familyId, Guid cunaId, string newName, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"cunas?id=eq.{cunaId}&family_id=eq.{familyId}");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new { nombre = newName }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<CribDto>>(JsonOptions, ct);
        return rows?.FirstOrDefault()?.ToModel();
    }

    public async Task<ClaimableDeviceRecord> BindClaimedDeviceAsync(
        Guid deviceRowId,
        Guid cunaId,
        Guid familyId,
        string hashToken,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"dispositivos?id=eq.{deviceRowId}");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new
        {
            cuna_id = cunaId,
            reclamo_usado = true,
            hash_token = hashToken,
            estado = "vinculado"
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<ClaimableDeviceRowDto>>(JsonOptions, ct);
        var d = rows!.Single();
        return new ClaimableDeviceRecord(
            d.Id, d.DeviceId, d.CunaId, familyId, d.Tipo, d.HashReclamo, d.ReclamoExpiraEn,
            d.ReclamoUsado, d.IntentosReclamo, d.HashToken, d.Estado, d.BateriaPct, d.FwVersion);
    }

    public async Task<IReadOnlyList<ClaimableDeviceRecord>> ListDevicesByCribAsync(Guid cunaId, CancellationToken ct)
    {
        var crib = await GetCribByIdAsync(cunaId, ct);
        var rows = await _http.GetFromJsonAsync<List<ClaimableDeviceRowDto>>(
            $"dispositivos?cuna_id=eq.{cunaId}&select=id,device_id,cuna_id,tipo,hash_reclamo,reclamo_expira_en,reclamo_usado,intentos_reclamo,hash_token,estado,bateria_pct,fw_version&order=tipo.asc",
            JsonOptions,
            ct) ?? [];
        return rows.Select(d => new ClaimableDeviceRecord(
            d.Id, d.DeviceId, d.CunaId, crib?.FamilyId, d.Tipo, d.HashReclamo, d.ReclamoExpiraEn,
            d.ReclamoUsado, d.IntentosReclamo, d.HashToken, d.Estado, d.BateriaPct, d.FwVersion)).ToList();
    }

    public async Task<IReadOnlyList<string>> UnlinkCribAndRevokeDevicesAsync(Guid familyId, Guid cunaId, CancellationToken ct)
    {
        var crib = await GetCribByIdAsync(cunaId, ct);
        if (crib is null || crib.FamilyId != familyId)
        {
            return [];
        }

        var devices = await ListDevicesByCribAsync(cunaId, ct);
        var deviceIds = devices.Select(d => d.DeviceId).ToList();

        using var revReq = new HttpRequestMessage(HttpMethod.Patch, $"dispositivos?cuna_id=eq.{cunaId}");
        revReq.Content = JsonContent.Create(new
        {
            cuna_id = (Guid?)null,
            hash_token = (string?)null,
            estado = "revocado"
        }, options: JsonOptions);
        using var revRes = await _http.SendAsync(revReq, ct);
        revRes.EnsureSuccessStatusCode();

        using var babyReq = new HttpRequestMessage(HttpMethod.Patch, $"bebes?cuna_id=eq.{cunaId}&family_id=eq.{familyId}");
        babyReq.Content = JsonContent.Create(new { cuna_id = (Guid?)null }, options: JsonOptions);
        using var babyRes = await _http.SendAsync(babyReq, ct);
        babyRes.EnsureSuccessStatusCode();

        using var delReq = new HttpRequestMessage(HttpMethod.Delete, $"cunas?id=eq.{cunaId}&family_id=eq.{familyId}");
        using var delRes = await _http.SendAsync(delReq, ct);
        delRes.EnsureSuccessStatusCode();

        return deviceIds;
    }

    public async Task<IReadOnlyList<BabyRecord>> ListBabiesByFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<BabyDto>>(
            $"bebes?family_id=eq.{familyId}&select=id,family_id,cuna_id,nombre,fecha_nacimiento,notas,creado_en&order=creado_en.asc",
            JsonOptions,
            ct) ?? [];
        return rows.Select(r => r.ToModel()).ToList();
    }

    public async Task<BabyRecord> CreateBabyAsync(
        Guid familyId,
        Guid cunaId,
        string nombre,
        DateOnly fechaNacimiento,
        string? notas,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "bebes");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new
        {
            family_id = familyId,
            cuna_id = cunaId,
            nombre,
            fecha_nacimiento = fechaNacimiento,
            notas
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<BabyDto>>(JsonOptions, ct);
        return rows!.Single().ToModel();
    }

    public async Task<BabyRecord?> UpdateBabyAsync(
        Guid familyId,
        Guid bebeId,
        Guid cunaId,
        string nombre,
        DateOnly fechaNacimiento,
        string? notas,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"bebes?id=eq.{bebeId}&family_id=eq.{familyId}");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new
        {
            cuna_id = cunaId,
            nombre,
            fecha_nacimiento = fechaNacimiento,
            notas
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<BabyDto>>(JsonOptions, ct);
        return rows?.FirstOrDefault()?.ToModel();
    }

    public async Task<IReadOnlyList<RegisteredDevice>> ListActiveDevicesForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<DeviceRowDto>>(
            $"dispositivos?cuna_id=eq.{cunaId}&select=id,device_id,cuna_id,tipo,hash_token,estado,bateria_pct,ultimo_seq,ultimo_heartbeat,fw_version",
            JsonOptions,
            ct);
        if (rows is null || rows.Count == 0)
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

    public async Task<CribRecord?> UpdateCribModeAsync(
        Guid cunaId,
        string modoOperacion,
        DateTimeOffset? modoAtencionExpiraEn,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"cunas?id=eq.{cunaId}");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new
        {
            modo_operacion = modoOperacion,
            modo_atencion_expira_en = modoAtencionExpiraEn
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<CribDto>>(JsonOptions, ct);
        return rows?.FirstOrDefault()?.ToModel();
    }

    public async Task<CribRecord?> UpdateCribLightAsync(
        Guid cunaId,
        bool luzEncendida,
        int luzNivel,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"cunas?id=eq.{cunaId}");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new
        {
            luz_encendida = luzEncendida,
            luz_nivel = luzNivel
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<CribDto>>(JsonOptions, ct);
        return rows?.FirstOrDefault()?.ToModel();
    }

    public async Task<CribConfig> UpdateCribThresholdsAsync(
        Guid cunaId,
        Guid familyId,
        UpdateCribThresholdsRequest request,
        CancellationToken ct)
    {
        using var cribReq = new HttpRequestMessage(HttpMethod.Patch, $"cunas?id=eq.{cunaId}&family_id=eq.{familyId}");
        cribReq.Content = JsonContent.Create(new { escala_masa = request.EscalaMasa }, options: JsonOptions);
        using var cribRes = await _http.SendAsync(cribReq, ct);
        cribRes.EnsureSuccessStatusCode();

        using var umbReq = new HttpRequestMessage(HttpMethod.Patch, $"umbrales?cuna_id=eq.{cunaId}");
        umbReq.Content = JsonContent.Create(new
        {
            temp_min = request.TempMin,
            temp_max = request.TempMax,
            hum_min = request.HumMin,
            hum_max = request.HumMax,
            movimiento_umbral = request.MovimientoUmbral,
            movimiento_alerta_activa = request.MovimientoAlertaActiva,
            ruido_masa_piso = request.RuidoMasaPiso,
            actualizado_en = DateTimeOffset.UtcNow
        }, options: JsonOptions);
        using var umbRes = await _http.SendAsync(umbReq, ct);
        umbRes.EnsureSuccessStatusCode();

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

    public async Task<CribRecord?> UpdateCribConnectionStateAsync(
        Guid cunaId,
        string estadoConexion,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"cunas?id=eq.{cunaId}");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new { estado_conexion = estadoConexion }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<CribDto>>(JsonOptions, ct);
        return rows?.FirstOrDefault()?.ToModel();
    }

    public async Task<IReadOnlyList<CribRecord>> ListAllCribsAsync(CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<CribDto>>(
            "cunas?order=creado_en.asc",
            JsonOptions,
            ct);
        return rows?.Select(r => r.ToModel()).ToList() ?? [];
    }

    public async Task LogCaregiverActionAsync(CaregiverAuditLogEntry entry, CancellationToken ct)
    {
        var payload = new
        {
            family_id = entry.FamilyId,
            usuario_id = entry.UsuarioId,
            perfil_id = entry.PerfilId,
            accion = entry.Accion,
            detalle = entry.DetalleJson is not null ? JsonDocument.Parse(entry.DetalleJson).RootElement : (JsonElement?)null,
            ts = entry.Ts
        };
        using var res = await _http.PostAsJsonAsync("bitacora", payload, JsonOptions, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<CaregiverAuditLogEntry>> ListCaregiverActionsAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<CaregiverAuditDto>>(
            $"bitacora?family_id=eq.{familyId}&order=ts.asc",
            JsonOptions,
            ct);
        return rows?.Select(r => new CaregiverAuditLogEntry(
            r.FamilyId,
            r.UsuarioId,
            r.PerfilId,
            r.Accion,
            r.Detalle?.GetRawText(),
            r.Ts)).ToList() ?? [];
    }

    public async Task<BabyRecord?> GetBabyByIdAsync(Guid bebeId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<BabyDto>>(
            $"bebes?id=eq.{bebeId}&limit=1",
            JsonOptions,
            ct);
        return rows?.FirstOrDefault()?.ToModel();
    }

    public async Task<FeedingSessionRecord> CreateFeedingSessionAsync(
        Guid bebeId,
        Guid? perfilId,
        decimal masaInicialG,
        decimal? tempC,
        bool alertaTemperatura,
        DateTimeOffset inicio,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "sesiones_alimentacion");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new
        {
            bebe_id = bebeId,
            perfil_id = perfilId,
            m_inicial = masaInicialG,
            temp_c = tempC,
            alerta_temperatura = alertaTemperatura,
            inicio
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<FeedingSessionDto>>(JsonOptions, ct);
        return rows!.Single().ToModel();
    }

    public async Task<FeedingSessionRecord?> GetFeedingSessionByIdAsync(Guid sessionId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<FeedingSessionDto>>(
            $"sesiones_alimentacion?id=eq.{sessionId}&limit=1",
            JsonOptions,
            ct);
        return rows?.FirstOrDefault()?.ToModel();
    }

    public async Task<FeedingSessionRecord?> CompleteFeedingSessionAsync(
        Guid sessionId,
        decimal masaFinalG,
        decimal mlConsumidos,
        DateTimeOffset fin,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"sesiones_alimentacion?id=eq.{sessionId}");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new
        {
            m_final = masaFinalG,
            ml = mlConsumidos,
            fin
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<FeedingSessionDto>>(JsonOptions, ct);
        return rows?.FirstOrDefault()?.ToModel();
    }

    public async Task<IReadOnlyList<FeedingSessionRecord>> ListFeedingSessionsByBabyAsync(Guid bebeId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<FeedingSessionDto>>(
            $"sesiones_alimentacion?bebe_id=eq.{bebeId}&order=inicio.desc",
            JsonOptions,
            ct);
        return rows?.Select(r => r.ToModel()).ToList() ?? [];
    }

    public async Task<SleepSessionRecord> CreateSleepSessionAsync(
        Guid bebeId,
        Guid cunaId,
        string tipo,
        DateTimeOffset inicio,
        DateTimeOffset? fin,
        decimal? movimientoPromedio,
        int interrupciones,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "sesiones_sueno");
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(new
        {
            bebe_id = bebeId,
            cuna_id = cunaId,
            tipo,
            inicio,
            fin,
            movimiento_promedio = movimientoPromedio,
            interrupciones
        }, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<SleepSessionDto>>(JsonOptions, ct);
        return rows!.Single().ToModel();
    }

    public async Task<IReadOnlyList<SleepSessionRecord>> ListSleepSessionsByBabyAsync(Guid bebeId, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<SleepSessionDto>>(
            $"sesiones_sueno?bebe_id=eq.{bebeId}&order=inicio.desc",
            JsonOptions,
            ct);
        return rows?.Select(r => r.ToModel()).ToList() ?? [];
    }

    public async Task<IReadOnlyList<SongCatalogItem>> ListSongCatalogAsync(CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<SongCatalogDto>>(
            "canciones?order=nombre.asc",
            JsonOptions,
            ct);
        return rows?.Select(r => new SongCatalogItem(
            r.Id,
            r.Codigo,
            r.Nombre,
            r.DuracionS,
            r.NotasJson?.GetRawText() ?? "[]")).ToList() ?? [];
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
}
