using System.Diagnostics;
using System.Text.Json;
using CunaServer.Data;
using CunaServer.Tcp;
using CunaServer.Telegram;

namespace CunaServer.Services;

/// <summary>
/// Servicio de aplicación para alimentación del biberón inteligente (RF16, RF17, RF37),
/// historial de sueño (RF30), exportación CSV/PDF (RF32) y juguetes/móvil colgante (RF12, RF13, RF33, RF36).
/// </summary>
public sealed class FeedingHistoryAndToysService
{
    public const decimal MilkDensityGramsPerMl = 1.03m;
    public const decimal MinInitialBottleMassGrams = 20.0m;
    public const decimal SafeFoodTempMinC = 35.0m;
    public const decimal SafeFoodTempMaxC = 38.5m;

    private readonly IFamilyAccountRepository _accountRepo;
    private readonly IDeviceTelemetryRepository _deviceRepo;
    private readonly DeviceTcpServer _tcpServer;
    private readonly TelegramBotService _telegramService;

    public FeedingHistoryAndToysService(
        IFamilyAccountRepository accountRepo,
        IDeviceTelemetryRepository deviceRepo,
        DeviceTcpServer tcpServer,
        TelegramBotService telegramService)
    {
        _accountRepo = accountRepo;
        _deviceRepo = deviceRepo;
        _tcpServer = tcpServer;
        _telegramService = telegramService;
    }

    /// <summary>
    /// RF17: Calcula el volumen consumido en mililitros a partir de la diferencia de masa con densidad 1.03 g/ml.
    /// </summary>
    public static decimal ComputeVolumeMl(decimal masaInicialG, decimal masaFinalG)
    {
        var diff = Math.Max(0.0m, masaInicialG - masaFinalG);
        return Math.Round(diff / MilkDensityGramsPerMl, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// RF16: Indica si la temperatura del biberón está fuera de la ventana segura (35.0–38.5 °C).
    /// </summary>
    public static bool IsFoodTemperatureOutOfRange(decimal? tempC)
        => tempC.HasValue && (tempC.Value < SafeFoodTempMinC || tempC.Value > SafeFoodTempMaxC);

    /// <summary>
    /// RF37: Inicia una sesión de alimentación validando masa inicial mínima (20 g) y alertando si la temperatura es insegura.
    /// </summary>
    public async Task<ServiceResult<FeedingSessionRecord>> StartFeedingSessionAsync(
        AuthenticatedIdentity identity,
        StartFeedingSessionRequest request,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, baby, forbidden) = await VerifyBabyAccessAsync(
            identity,
            request.BebeId,
            "iniciar_alimentacion_rf37",
            clientIp,
            ct);
        if (forbidden is not null || membership is null || baby is null)
        {
            return ServiceResult<FeedingSessionRecord>.Forbidden(forbidden ?? "Acceso denegado.");
        }

        if (request.MasaInicialG < MinInitialBottleMassGrams || request.MasaInicialG > 2000.0m)
        {
            return ServiceResult<FeedingSessionRecord>.BadRequest(
                $"Precondición RF37: la masa inicial del biberón debe ser de al menos {MinInitialBottleMassGrams:F1} g (20–2000 g).");
        }

        if (request.TempC.HasValue && request.TempC.Value is < 0.0m or > 100.0m)
        {
            return ServiceResult<FeedingSessionRecord>.BadRequest("Temperatura de biberón fuera de rango físico (0–100 °C).");
        }

        var now = DateTimeOffset.UtcNow;
        var tempAlert = IsFoodTemperatureOutOfRange(request.TempC);

        var session = await _deviceRepo.CreateFeedingSessionAsync(
            baby.Id,
            request.PerfilId,
            Math.Round(request.MasaInicialG, 1),
            request.TempC.HasValue ? Math.Round(request.TempC.Value, 2) : null,
            tempAlert,
            now,
            ct);

        if (tempAlert && baby.CunaId.HasValue)
        {
            await DispatchFoodTemperatureAlertAsync(baby.CunaId.Value, session.Id, request.TempC!.Value, now, ct);
        }

        await _deviceRepo.LogCaregiverActionAsync(new CaregiverAuditLogEntry(
            membership.FamilyId,
            identity.UserId,
            request.PerfilId,
            "inicio_toma_rf37",
            JsonSerializer.Serialize(new
            {
                session_id = session.Id,
                bebe_id = baby.Id,
                m_inicial_g = session.MasaInicialG,
                temp_c = session.TempC,
                alerta_temperatura = session.AlertaTemperatura
            }),
            now), ct);

        return ServiceResult<FeedingSessionRecord>.Created(session);
    }

    /// <summary>
    /// RF17 + RF18: Cierra la toma, calcula mililitros consumidos y reinicia el temporizador del recordatorio dinámico.
    /// </summary>
    public async Task<ServiceResult<FeedingSessionRecord>> FinishFeedingSessionAsync(
        AuthenticatedIdentity identity,
        Guid sessionId,
        FinishFeedingSessionRequest request,
        string? clientIp,
        CancellationToken ct)
    {
        var session = await _deviceRepo.GetFeedingSessionByIdAsync(sessionId, ct);
        if (session is null)
        {
            return ServiceResult<FeedingSessionRecord>.NotFound("Sesión de alimentación no encontrada.");
        }

        var (membership, baby, forbidden) = await VerifyBabyAccessAsync(
            identity,
            session.BebeId,
            "finalizar_alimentacion_rf17",
            clientIp,
            ct);
        if (forbidden is not null || membership is null || baby is null)
        {
            return ServiceResult<FeedingSessionRecord>.Forbidden(forbidden ?? "Acceso denegado.");
        }

        if (session.Fin.HasValue)
        {
            return ServiceResult<FeedingSessionRecord>.Conflict("Esta sesión de alimentación ya fue finalizada.");
        }

        if (request.MasaFinalG < 0.0m || request.MasaFinalG > session.MasaInicialG)
        {
            return ServiceResult<FeedingSessionRecord>.BadRequest(
                "La masa final del biberón debe ser mayor o igual a 0 g y no superar la masa inicial (RF17).");
        }

        var now = DateTimeOffset.UtcNow;
        var ml = ComputeVolumeMl(session.MasaInicialG, request.MasaFinalG);
        var updated = await _deviceRepo.CompleteFeedingSessionAsync(
            sessionId,
            Math.Round(request.MasaFinalG, 1),
            ml,
            now,
            ct);

        // Reiniciar el temporizador del recordatorio dinámico de tomas (RF18)
        var reminder = await _accountRepo.GetOrCreateFeedingReminderAsync(membership.FamilyId, ct);
        await _accountRepo.UpdateFeedingReminderAsync(
            membership.FamilyId,
            reminder.IntervaloHoras,
            reminder.Activo,
            now,
            ct);

        await _deviceRepo.LogCaregiverActionAsync(new CaregiverAuditLogEntry(
            membership.FamilyId,
            identity.UserId,
            request.PerfilId ?? session.PerfilId,
            "fin_toma_rf17",
            JsonSerializer.Serialize(new
            {
                session_id = session.Id,
                bebe_id = baby.Id,
                m_inicial_g = session.MasaInicialG,
                m_final_g = request.MasaFinalG,
                ml_consumidos = ml
            }),
            now), ct);

        return ServiceResult<FeedingSessionRecord>.Ok(updated!);
    }

    public async Task<ServiceResult<IReadOnlyList<FeedingSessionRecord>>> GetFeedingHistoryAsync(
        AuthenticatedIdentity identity,
        Guid bebeId,
        DateOnly? desde,
        DateOnly? hasta,
        string? clientIp,
        CancellationToken ct)
    {
        var (_, _, forbidden) = await VerifyBabyAccessAsync(
            identity,
            bebeId,
            "consultar_historial_alimentacion_rf31",
            clientIp,
            ct);
        if (forbidden is not null)
        {
            return ServiceResult<IReadOnlyList<FeedingSessionRecord>>.Forbidden(forbidden);
        }

        var all = await _deviceRepo.ListFeedingSessionsByBabyAsync(bebeId, ct);
        var filtered = FilterByDateRange(all, s => s.Inicio, desde, hasta);
        return ServiceResult<IReadOnlyList<FeedingSessionRecord>>.Ok(filtered);
    }

    public async Task<ServiceResult<SleepSessionRecord>> CreateSleepSessionAsync(
        AuthenticatedIdentity identity,
        Guid bebeId,
        CreateSleepSessionRequest request,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, baby, forbidden) = await VerifyBabyAccessAsync(
            identity,
            bebeId,
            "registrar_sueno_rf30",
            clientIp,
            ct);
        if (forbidden is not null || membership is null || baby is null)
        {
            return ServiceResult<SleepSessionRecord>.Forbidden(forbidden ?? "Acceso denegado.");
        }

        var tipo = (request.Tipo ?? "").Trim().ToLowerInvariant();
        if (tipo is not ("siesta" or "noche"))
        {
            return ServiceResult<SleepSessionRecord>.BadRequest("El tipo de sueño debe ser 'siesta' o 'noche' (RF30).");
        }

        if (request.Fin.HasValue && request.Fin.Value < request.Inicio)
        {
            return ServiceResult<SleepSessionRecord>.BadRequest("La hora de fin no puede ser anterior al inicio.");
        }

        if (request.Interrupciones < 0)
        {
            return ServiceResult<SleepSessionRecord>.BadRequest("El número de interrupciones no puede ser negativo.");
        }

        if (request.CunaId == Guid.Empty)
        {
            return ServiceResult<SleepSessionRecord>.BadRequest("Se requiere el identificador válido de la cuna.");
        }

        var crib = await _deviceRepo.GetCribByIdAsync(request.CunaId, ct);
        if (crib is null || crib.FamilyId != membership.FamilyId)
        {
            await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                membership.FamilyId,
                identity.UserId,
                null,
                "acceso_denegado_403",
                clientIp,
                $"cuna_ajena:registrar_sueno_rf30:{request.CunaId}",
                DateTimeOffset.UtcNow), ct);
            return ServiceResult<SleepSessionRecord>.Forbidden("Acceso denegado: la cuna no pertenece a tu familia.");
        }

        var created = await _deviceRepo.CreateSleepSessionAsync(
            bebeId,
            request.CunaId,
            tipo,
            request.Inicio,
            request.Fin,
            request.MovimientoPromedio,
            request.Interrupciones,
            ct);

        await _deviceRepo.LogCaregiverActionAsync(new CaregiverAuditLogEntry(
            membership.FamilyId,
            identity.UserId,
            request.PerfilId,
            "registro_sueno_rf30",
            JsonSerializer.Serialize(new { sleep_id = created.Id, bebe_id = bebeId, tipo }),
            DateTimeOffset.UtcNow), ct);

        return ServiceResult<SleepSessionRecord>.Created(created);
    }

    public async Task<ServiceResult<IReadOnlyList<SleepSessionRecord>>> GetSleepHistoryAsync(
        AuthenticatedIdentity identity,
        Guid bebeId,
        DateOnly? desde,
        DateOnly? hasta,
        string? tipo,
        string? clientIp,
        CancellationToken ct)
    {
        var (_, _, forbidden) = await VerifyBabyAccessAsync(
            identity,
            bebeId,
            "consultar_historial_sueno_rf30",
            clientIp,
            ct);
        if (forbidden is not null)
        {
            return ServiceResult<IReadOnlyList<SleepSessionRecord>>.Forbidden(forbidden);
        }

        var all = await _deviceRepo.ListSleepSessionsByBabyAsync(bebeId, ct);
        var filtered = FilterByDateRange(all, s => s.Inicio, desde, hasta);
        if (!string.IsNullOrWhiteSpace(tipo))
        {
            var normalized = tipo.Trim().ToLowerInvariant();
            filtered = filtered.Where(s => s.Tipo == normalized).ToList();
        }

        return ServiceResult<IReadOnlyList<SleepSessionRecord>>.Ok(filtered);
    }

    /// <summary>
    /// RF32: Exporta el historial del infante en PDF o CSV en &lt; 10 s y registra la operación en bitácora y bitacora_seguridad.
    /// </summary>
    public async Task<ServiceResult<ExportHistoryResponse>> ExportBabyHistoryAsync(
        AuthenticatedIdentity identity,
        Guid bebeId,
        ExportHistoryRequest request,
        string? clientIp,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var (membership, baby, forbidden) = await VerifyBabyAccessAsync(
            identity,
            bebeId,
            "exportar_historial_rf32",
            clientIp,
            ct);
        if (forbidden is not null || membership is null || baby is null)
        {
            return ServiceResult<ExportHistoryResponse>.Forbidden(forbidden ?? "Acceso denegado.");
        }

        var formato = (request.Formato ?? "").Trim().ToLowerInvariant();
        if (formato is not ("csv" or "pdf"))
        {
            return ServiceResult<ExportHistoryResponse>.BadRequest("El formato de exportación debe ser 'csv' o 'pdf' (RF32).");
        }

        var sleepTask = _deviceRepo.ListSleepSessionsByBabyAsync(bebeId, ct);
        var feedingTask = _deviceRepo.ListFeedingSessionsByBabyAsync(bebeId, ct);
        await Task.WhenAll(sleepTask, feedingTask);

        var sleepFiltered = FilterByDateRange(await sleepTask, s => s.Inicio, request.Desde, request.Hasta);
        var feedingFiltered = FilterByDateRange(await feedingTask, f => f.Inicio, request.Desde, request.Hasta);

        var now = DateTimeOffset.UtcNow;
        var (fileBytes, contentType, fileName) = HistoryReportExporter.BuildReport(
            formato,
            baby,
            sleepFiltered,
            feedingFiltered,
            now);

        await _deviceRepo.LogCaregiverActionAsync(new CaregiverAuditLogEntry(
            membership.FamilyId,
            identity.UserId,
            request.PerfilId,
            "exportar_historial_rf32",
            JsonSerializer.Serialize(new
            {
                bebe_id = baby.Id,
                formato,
                sesiones_sueno = sleepFiltered.Count,
                sesiones_alimentacion = feedingFiltered.Count
            }),
            now), ct);

        await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
            membership.FamilyId,
            identity.UserId,
            null,
            "exportacion_historial_rf32",
            clientIp,
            $"formato={formato};sueno={sleepFiltered.Count};alimentacion={feedingFiltered.Count}",
            now), ct);

        sw.Stop();
        return ServiceResult<ExportHistoryResponse>.Ok(new ExportHistoryResponse(
            fileName,
            contentType,
            Convert.ToBase64String(fileBytes),
            sleepFiltered.Count,
            feedingFiltered.Count,
            sw.ElapsedMilliseconds));
    }

    public async Task<ServiceResult<IReadOnlyList<SongCatalogItem>>> ListSongsAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        string? clientIp,
        CancellationToken ct)
    {
        var (_, _, forbidden) = await VerifyCribAccessAsync(identity, cunaId, "listar_canciones_rf12", clientIp, ct);
        if (forbidden is not null)
        {
            return ServiceResult<IReadOnlyList<SongCatalogItem>>.Forbidden(forbidden);
        }

        var songs = await _deviceRepo.ListSongCatalogAsync(ct);
        return ServiceResult<IReadOnlyList<SongCatalogItem>>.Ok(songs);
    }

    public async Task<ServiceResult<bool>> PlaySongAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        PlaySongRequest request,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, _, forbidden) = await VerifyCribAccessAsync(identity, cunaId, "reproducir_cancion_rf12", clientIp, ct);
        if (forbidden is not null || membership is null)
        {
            return ServiceResult<bool>.Forbidden(forbidden ?? "Acceso denegado.");
        }

        var accion = (request.Accion ?? "play").Trim().ToLowerInvariant();
        if (accion is not ("play" or "stop"))
        {
            return ServiceResult<bool>.BadRequest("La acción debe ser 'play' o 'stop'.");
        }

        var songs = await _deviceRepo.ListSongCatalogAsync(ct);
        var song = songs.FirstOrDefault(s => string.Equals(s.Codigo, request.Codigo?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (accion == "play" && song is null)
        {
            return ServiceResult<bool>.NotFound("Canción no encontrada en el catálogo (RF12).");
        }

        var central = await _deviceRepo.GetCentralDeviceForCribAsync(cunaId, ct);
        if (central is not null)
        {
            using var notesDoc = song is not null ? JsonDocument.Parse(song.NotasJson) : null;
            await _tcpServer.SendCommandAsync(
                central.DeviceId,
                "song_play",
                new
                {
                    action = accion,
                    song = song?.Codigo ?? request.Codigo,
                    duration_s = song?.DuracionSegundos ?? 0,
                    notes = notesDoc?.RootElement
                },
                requireAck: false,
                ct: ct);
        }

        await _deviceRepo.LogCaregiverActionAsync(new CaregiverAuditLogEntry(
            membership.FamilyId,
            identity.UserId,
            request.PerfilId,
            "cancion_cuna_rf12",
            JsonSerializer.Serialize(new { cuna_id = cunaId, codigo = request.Codigo, accion }),
            DateTimeOffset.UtcNow), ct);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<ToyUsageRecord>> PlayMobileAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        PlayMobileRequest request,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, _, forbidden) = await VerifyCribAccessAsync(identity, cunaId, "activar_movil_leds_rf13", clientIp, ct);
        if (forbidden is not null || membership is null)
        {
            return ServiceResult<ToyUsageRecord>.Forbidden(forbidden ?? "Acceso denegado.");
        }

        var velocidad = (request.Velocidad ?? "").Trim().ToLowerInvariant();
        if (velocidad is not ("lenta" or "media" or "rapida"))
        {
            return ServiceResult<ToyUsageRecord>.BadRequest(
                "La velocidad del móvil colgante debe ser 'lenta', 'media' o 'rapida' (RF13).");
        }

        var patron = string.IsNullOrWhiteSpace(request.Patron) ? "ola_circular" : request.Patron.Trim();
        if (request.DuracionSegundos is < 5 or > 1800)
        {
            return ServiceResult<ToyUsageRecord>.BadRequest(
                "La duración de la animación del móvil debe estar entre 5 y 1800 segundos (RF13).");
        }

        var devices = await _deviceRepo.ListActiveDevicesForCribAsync(cunaId, ct);
        var mobileDev = devices.FirstOrDefault(d => d.Tipo == DeviceTypes.Movil);
        if (mobileDev is not null)
        {
            await _tcpServer.SendCommandAsync(
                mobileDev.DeviceId,
                "mobile_play",
                new
                {
                    pattern = patron,
                    speed = velocidad,
                    duration_s = request.DuracionSegundos
                },
                requireAck: false,
                ct: ct);
        }

        var now = DateTimeOffset.UtcNow;
        var usage = new ToyUsageRecord(
            Guid.NewGuid(),
            cunaId,
            "movil",
            request.PerfilId,
            now,
            request.DuracionSegundos,
            null,
            patron,
            velocidad);
        await _deviceRepo.SaveToyUsageAsync(usage, ct);

        await _deviceRepo.LogCaregiverActionAsync(new CaregiverAuditLogEntry(
            membership.FamilyId,
            identity.UserId,
            request.PerfilId,
            "movil_leds_rf13_rf33",
            JsonSerializer.Serialize(new
            {
                cuna_id = cunaId,
                patron,
                velocidad,
                duracion_s = request.DuracionSegundos
            }),
            now), ct);

        return ServiceResult<ToyUsageRecord>.Created(usage);
    }

    public async Task<ServiceResult<ToysAndPeripheralsStatusResponse>> GetToysAndPeripheralsStatusAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        string? clientIp,
        CancellationToken ct)
    {
        var (_, _, forbidden) = await VerifyCribAccessAsync(identity, cunaId, "consultar_juguetes_rf33_rf36", clientIp, ct);
        if (forbidden is not null)
        {
            return ServiceResult<ToysAndPeripheralsStatusResponse>.Forbidden(forbidden);
        }

        var usageTask = _deviceRepo.ListToyUsageForCribAsync(cunaId, ct);
        var devicesTask = _deviceRepo.ListDevicesByCribAsync(cunaId, ct);
        var telemetryTask = _deviceRepo.ListTelemetryForCribAsync(cunaId, ct);
        await Task.WhenAll(usageTask, devicesTask, telemetryTask);

        var usage = await usageTask;
        var devices = await devicesTask;
        var telemetry = await telemetryTask;

        var toyDev = devices.FirstOrDefault(d => d.Tipo == DeviceTypes.Juguete);
        var bottleDev = devices.FirstOrDefault(d => d.Tipo == DeviceTypes.Biberon);

        var latestBottleTelemetry = telemetry.LastOrDefault(t => t.MasaBiberonG.HasValue || t.TempBiberonC.HasValue);
        var toyBattery = toyDev?.BateriaPct;
        var lowBatteryAlert = toyBattery.HasValue && toyBattery.Value < 20;

        return ServiceResult<ToysAndPeripheralsStatusResponse>.Ok(new ToysAndPeripheralsStatusResponse(
            usage.TakeLast(25).Reverse().ToList(),
            toyBattery,
            lowBatteryAlert,
            bottleDev?.BateriaPct ?? latestBottleTelemetry?.BateriaPct,
            latestBottleTelemetry?.MasaBiberonG,
            latestBottleTelemetry?.TempBiberonC));
    }

    private async Task DispatchFoodTemperatureAlertAsync(
        Guid cunaId,
        Guid sessionId,
        decimal tempC,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var alertRecord = new EventAlertRecord(
            Guid.NewGuid(),
            cunaId,
            Guid.Empty,
            "food_temp_out_of_range",
            "advertencia",
            $"Alerta térmica de alimento (RF16): temperatura {tempC:F1} °C fuera del rango seguro (35.0–38.5 °C).",
            JsonSerializer.Serialize(new { temp_c = tempC, session_id = sessionId }),
            now);
        await _deviceRepo.SaveEventAlertAsync(alertRecord, ct);
        await _telegramService.DispatchAlertForCribAsync(cunaId, alertRecord, ct);
    }

    private static IReadOnlyList<T> FilterByDateRange<T>(
        IReadOnlyList<T> items,
        Func<T, DateTimeOffset> getTimestamp,
        DateOnly? desde,
        DateOnly? hasta)
    {
        return items
            .Where(item =>
            {
                var date = DateOnly.FromDateTime(getTimestamp(item).UtcDateTime);
                if (desde.HasValue && date < desde.Value)
                {
                    return false;
                }
                if (hasta.HasValue && date > hasta.Value)
                {
                    return false;
                }
                return true;
            })
            .ToList();
    }

    private Task<(UserMembership? Membership, BabyRecord? Baby, string? ForbiddenMessage)> VerifyBabyAccessAsync(
        AuthenticatedIdentity identity,
        Guid bebeId,
        string action,
        string? clientIp,
        CancellationToken ct)
        => FamilyAuthorizationHelper.RequireFamilyBabyAccessAsync(
            _accountRepo,
            _deviceRepo,
            identity,
            bebeId,
            action,
            clientIp,
            ct);

    private Task<(UserMembership? Membership, CribRecord? Crib, string? ForbiddenMessage)> VerifyCribAccessAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        string action,
        string? clientIp,
        CancellationToken ct)
        => FamilyAuthorizationHelper.RequireFamilyCribAccessAsync(
            _accountRepo,
            _deviceRepo,
            identity,
            cunaId,
            action,
            requirePrincipal: false,
            clientIp,
            ct);
}
