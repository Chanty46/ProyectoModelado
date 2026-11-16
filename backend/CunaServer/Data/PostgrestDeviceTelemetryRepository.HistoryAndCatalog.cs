namespace CunaServer.Data;

public sealed partial class PostgrestDeviceTelemetryRepository
{
    #region Alimentación, sueño y catálogo de canciones (M7: RF12–RF17, RF30–RF37)

    /// <inheritdoc />
    public async Task<BabyRecord?> GetBabyByIdAsync(Guid bebeId, CancellationToken ct)
    {
        var dto = await _client.GetFirstOrDefaultAsync<BabyDto>($"bebes?id=eq.{bebeId}&limit=1", ct);
        return dto?.ToModel();
    }

    /// <inheritdoc />
    public async Task<FeedingSessionRecord> CreateFeedingSessionAsync(
        Guid bebeId,
        Guid? perfilId,
        decimal masaInicialG,
        decimal? tempC,
        bool alertaTemperatura,
        DateTimeOffset inicio,
        CancellationToken ct)
    {
        var created = await _client.PostAndReadSingleAsync<object, FeedingSessionDto>(
            "sesiones_alimentacion",
            new
            {
                bebe_id = bebeId,
                perfil_id = perfilId,
                m_inicial = masaInicialG,
                temp_c = tempC,
                alerta_temperatura = alertaTemperatura,
                inicio
            },
            ct);
        return created.ToModel();
    }

    /// <inheritdoc />
    public async Task<FeedingSessionRecord?> GetFeedingSessionByIdAsync(Guid sessionId, CancellationToken ct)
    {
        var dto = await _client.GetFirstOrDefaultAsync<FeedingSessionDto>(
            $"sesiones_alimentacion?id=eq.{sessionId}&limit=1",
            ct);
        return dto?.ToModel();
    }

    /// <inheritdoc />
    public async Task<FeedingSessionRecord?> CompleteFeedingSessionAsync(
        Guid sessionId,
        decimal masaFinalG,
        decimal mlConsumidos,
        DateTimeOffset fin,
        CancellationToken ct)
    {
        var dto = await _client.PatchAndReadFirstAsync<object, FeedingSessionDto>(
            $"sesiones_alimentacion?id=eq.{sessionId}",
            new
            {
                m_final = masaFinalG,
                ml = mlConsumidos,
                fin
            },
            ct);
        return dto?.ToModel();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeedingSessionRecord>> ListFeedingSessionsByBabyAsync(Guid bebeId, CancellationToken ct)
    {
        var rows = await _client.GetListAsync<FeedingSessionDto>(
            $"sesiones_alimentacion?bebe_id=eq.{bebeId}&order=inicio.desc",
            ct);
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <inheritdoc />
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
        var created = await _client.PostAndReadSingleAsync<object, SleepSessionDto>(
            "sesiones_sueno",
            new
            {
                bebe_id = bebeId,
                cuna_id = cunaId,
                tipo,
                inicio,
                fin,
                movimiento_promedio = movimientoPromedio,
                interrupciones
            },
            ct);
        return created.ToModel();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SleepSessionRecord>> ListSleepSessionsByBabyAsync(Guid bebeId, CancellationToken ct)
    {
        var rows = await _client.GetListAsync<SleepSessionDto>(
            $"sesiones_sueno?bebe_id=eq.{bebeId}&order=inicio.desc",
            ct);
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SongCatalogItem>> ListSongCatalogAsync(CancellationToken ct)
    {
        var rows = await _client.GetListAsync<SongCatalogDto>("canciones?order=nombre.asc", ct);
        return rows.Select(r => new SongCatalogItem(
            r.Id,
            r.Codigo,
            r.Nombre,
            r.DuracionS,
            r.NotasJson?.GetRawText() ?? "[]")).ToList();
    }

    #endregion
}
