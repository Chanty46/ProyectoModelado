using System.Collections.Concurrent;

namespace CunaServer.Data;

public sealed partial class InMemoryDeviceTelemetryRepository
{
    #region Emparejamiento, cunas y perfil del bebé (M4: RF24–RF28)

    /// <inheritdoc />
    public Task<ClaimableDeviceRecord?> GetClaimableDeviceAsync(string deviceId, CancellationToken ct)
    {
        _claimableDevices.TryGetValue(deviceId, out var dev);
        return Task.FromResult(dev);
    }

    /// <inheritdoc />
    public Task IncrementClaimAttemptAsync(Guid deviceRowId, CancellationToken ct)
    {
        lock (_seqLock)
        {
            var entry = _claimableDevices.Values.FirstOrDefault(d => d.Id == deviceRowId);
            if (entry is not null && entry.IntentosReclamo < MaxClaimAttempts)
            {
                _claimableDevices[entry.DeviceId] = entry with { IntentosReclamo = entry.IntentosReclamo + 1 };
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
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
        _cribConfigs[crib.Id] = CreateDefaultCribConfig(crib.Id, familyId);

        return Task.FromResult(crib);
    }

    /// <inheritdoc />
    public Task<CribRecord?> GetCribByIdAsync(Guid cunaId, CancellationToken ct)
    {
        _cribs.TryGetValue(cunaId, out var crib);
        return Task.FromResult(crib);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CribRecord>> ListCribsByFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var list = _cribs.Values
            .Where(c => c.FamilyId == familyId)
            .OrderBy(c => c.CreadoEn)
            .ToList();
        return Task.FromResult<IReadOnlyList<CribRecord>>(list);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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
            _deviceIdByRowId[current.Id] = current.DeviceId;

            return Task.FromResult(updatedClaimable);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ClaimableDeviceRecord>> ListDevicesByCribAsync(Guid cunaId, CancellationToken ct)
    {
        var list = _claimableDevices.Values
            .Where(d => d.CunaId == cunaId && d.Estado != "revocado")
            .OrderBy(d => d.Tipo)
            .ToList();
        return Task.FromResult<IReadOnlyList<ClaimableDeviceRecord>>(list);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task<IReadOnlyList<BabyRecord>> ListBabiesByFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var list = _babies.Values
            .Where(b => b.FamilyId == familyId)
            .OrderBy(b => b.CreadoEn)
            .ToList();
        return Task.FromResult<IReadOnlyList<BabyRecord>>(list);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    #endregion

    #region Cuna en vivo y bitácora del cuidador (M5: RF04, RF05, RF07, RF08, RF09, RF20, RF29)

    /// <inheritdoc />
    public Task<IReadOnlyList<RegisteredDevice>> ListActiveDevicesForCribAsync(Guid cunaId, CancellationToken ct)
    {
        var list = _devicesByDeviceId.Values
            .Where(d => d.CunaId == cunaId && d.Estado is not ("revocado" or "sin_dueno"))
            .ToList();
        return Task.FromResult<IReadOnlyList<RegisteredDevice>>(list);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task<IReadOnlyList<CribRecord>> ListAllCribsAsync(CancellationToken ct)
    {
        var list = _cribs.Values.OrderBy(c => c.CreadoEn).ToList();
        return Task.FromResult<IReadOnlyList<CribRecord>>(list);
    }

    /// <inheritdoc />
    public Task LogCaregiverActionAsync(CaregiverAuditLogEntry entry, CancellationToken ct)
    {
        var queue = _caregiverActionsByFamily.GetOrAdd(entry.FamilyId, _ => new ConcurrentQueue<CaregiverAuditLogEntry>());
        EnqueueBounded(queue, MaxAuditEntries, entry);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CaregiverAuditLogEntry>> ListCaregiverActionsAsync(Guid familyId, CancellationToken ct)
    {
        if (!_caregiverActionsByFamily.TryGetValue(familyId, out var queue))
        {
            return Task.FromResult<IReadOnlyList<CaregiverAuditLogEntry>>(Array.Empty<CaregiverAuditLogEntry>());
        }

        var list = queue.OrderBy(a => a.Ts).ToList();
        return Task.FromResult<IReadOnlyList<CaregiverAuditLogEntry>>(list);
    }

    #endregion
}
