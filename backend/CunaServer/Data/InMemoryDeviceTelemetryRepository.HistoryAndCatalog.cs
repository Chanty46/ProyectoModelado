namespace CunaServer.Data;

public sealed partial class InMemoryDeviceTelemetryRepository
{
    #region Alimentación, sueño y catálogo de canciones (M7: RF12–RF17, RF30–RF37)

    /// <inheritdoc />
    public Task<BabyRecord?> GetBabyByIdAsync(Guid bebeId, CancellationToken ct)
    {
        _babies.TryGetValue(bebeId, out var baby);
        return Task.FromResult(baby);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task<FeedingSessionRecord?> GetFeedingSessionByIdAsync(Guid sessionId, CancellationToken ct)
    {
        _feedingSessions.TryGetValue(sessionId, out var record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task<IReadOnlyList<FeedingSessionRecord>> ListFeedingSessionsByBabyAsync(Guid bebeId, CancellationToken ct)
    {
        var list = _feedingSessions.Values
            .Where(s => s.BebeId == bebeId)
            .OrderByDescending(s => s.Inicio)
            .ToList();
        return Task.FromResult<IReadOnlyList<FeedingSessionRecord>>(list);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task<IReadOnlyList<SleepSessionRecord>> ListSleepSessionsByBabyAsync(Guid bebeId, CancellationToken ct)
    {
        var list = _sleepSessions.Values
            .Where(s => s.BebeId == bebeId)
            .OrderByDescending(s => s.Inicio)
            .ToList();
        return Task.FromResult<IReadOnlyList<SleepSessionRecord>>(list);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SongCatalogItem>> ListSongCatalogAsync(CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<SongCatalogItem>>(_songs.ToList());
    }

    #endregion
}
