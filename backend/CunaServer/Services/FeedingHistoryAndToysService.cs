using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CunaServer.Data;
using CunaServer.Tcp;
using CunaServer.Telegram;

namespace CunaServer.Services;

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

    public static bool IsFoodTemperatureOutOfRange(decimal? tempC)
        => tempC.HasValue && (tempC.Value < SafeFoodTempMinC || tempC.Value > SafeFoodTempMaxC);

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
            var alertRecord = new EventAlertRecord(
                Guid.NewGuid(),
                baby.CunaId.Value,
                Guid.Empty,
                "food_temp_out_of_range",
                "advertencia",
                $"Alerta térmica de alimento (RF16): temperatura {request.TempC!.Value:F1} °C fuera del rango seguro (35.0–38.5 °C).",
                JsonSerializer.Serialize(new { temp_c = request.TempC.Value, session_id = session.Id }),
                now);
            await _deviceRepo.SaveEventAlertAsync(alertRecord, ct);
            await _telegramService.DispatchAlertForCribAsync(baby.CunaId.Value, alertRecord, ct);
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

        if (request.CunaId != Guid.Empty)
        {
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
        }
        else
        {
            return ServiceResult<SleepSessionRecord>.BadRequest("Se requiere el identificador válido de la cuna.");
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

        var sleepAll = await sleepTask;
        var feedingAll = await feedingTask;

        var sleepFiltered = FilterByDateRange(sleepAll, s => s.Inicio, request.Desde, request.Hasta);
        var feedingFiltered = FilterByDateRange(feedingAll, f => f.Inicio, request.Desde, request.Hasta);

        byte[] fileBytes;
        string contentType;
        string extension;

        if (formato == "csv")
        {
            fileBytes = BuildCsvBytes(baby, sleepFiltered, feedingFiltered);
            contentType = "text/csv; charset=utf-8";
            extension = "csv";
        }
        else
        {
            fileBytes = BuildPdfBytes(baby, sleepFiltered, feedingFiltered);
            contentType = "application/pdf";
            extension = "pdf";
        }

        var now = DateTimeOffset.UtcNow;

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
        var fileName = $"historial_{SanitizeFileName(baby.Nombre)}_{now:yyyyMMdd}.{extension}";

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

    private static byte[] BuildCsvBytes(
        BabyRecord baby,
        IReadOnlyList<SleepSessionRecord> sleepSessions,
        IReadOnlyList<FeedingSessionRecord> feedingSessions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Categoria,FechaInicio,FechaFin,Detalle1,Detalle2,Detalle3,Alerta");

        foreach (var s in sleepSessions)
        {
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Sueno ({s.Tipo}),{s.Inicio:O},{s.Fin?.ToString("O") ?? ""},mov_prom={s.MovimientoPromedio ?? 0:F3},interrupciones={s.Interrupciones},bebe={EscapeCsv(baby.Nombre)},NO"));
        }

        foreach (var f in feedingSessions)
        {
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Alimentacion,{f.Inicio:O},{f.Fin?.ToString("O") ?? ""},m_inicial_g={f.MasaInicialG:F1},m_final_g={f.MasaFinalG ?? 0:F1},ml={f.MlConsumidos ?? 0:F1},{(f.AlertaTemperatura ? "SI_TEMP" : "NO")}"));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] BuildPdfBytes(
        BabyRecord baby,
        IReadOnlyList<SleepSessionRecord> sleepSessions,
        IReadOnlyList<FeedingSessionRecord> feedingSessions)
    {
        const int maxPdfCharsPerLine = 92;
        const float maxPdfWidthPt = 500f;
        const int maxBabyHeaderLines = 8;

        var allBodyLines = new List<string>();
        foreach (var f in feedingSessions)
        {
            var alertaStr = f.AlertaTemperatura ? " [ALERTA TEMP]" : "";
            var finalStr = f.MasaFinalG.HasValue
                ? string.Create(CultureInfo.InvariantCulture, $"{f.MasaFinalG.Value:F1}g")
                : "N/D";
            var volStr = f.MlConsumidos.HasValue
                ? string.Create(CultureInfo.InvariantCulture, $"{f.MlConsumidos.Value:F1} ml")
                : "N/D";
            var tempStr = f.TempC.HasValue
                ? string.Create(CultureInfo.InvariantCulture, $"{f.TempC.Value:F1} °C")
                : "N/D";
            var rawLine = string.Create(
                CultureInfo.InvariantCulture,
                $"[Toma] {f.Inicio:yyyy-MM-dd HH:mm} | Inicial: {f.MasaInicialG:F1}g | Final: {finalStr} | Vol: {volStr} | T: {tempStr}{alertaStr}");
            allBodyLines.AddRange(WrapPdfLine(EscapePdfText(rawLine), maxPdfCharsPerLine, maxPdfWidthPt));
        }

        foreach (var s in sleepSessions)
        {
            var movStr = s.MovimientoPromedio.HasValue
                ? string.Create(CultureInfo.InvariantCulture, $"{s.MovimientoPromedio.Value:F3} kg/m")
                : "N/D";
            var rawLine = string.Create(
                CultureInfo.InvariantCulture,
                $"[Sueño {s.Tipo}] {s.Inicio:yyyy-MM-dd HH:mm} | Mov prom: {movStr} | Interrupciones: {s.Interrupciones}");
            allBodyLines.AddRange(WrapPdfLine(EscapePdfText(rawLine), maxPdfCharsPerLine, maxPdfWidthPt));
        }

        if (allBodyLines.Count == 0)
        {
            allBodyLines.Add("Sin sesiones registradas para este perfil.");
        }

        var generatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var babyNameEscaped = EscapePdfText(baby.Nombre);
        var probeFullTitle = $"Reporte Histórico Cuna Inteligente (RF32) - Bebé: {babyNameEscaped} (Pág. 999/999)";
        var singleLineHeaderFits = probeFullTitle.Length <= maxPdfCharsPerLine
            && EstimateHelveticaWidthPt(probeFullTitle) <= maxPdfWidthPt;

        var wrappedBabyHeaderLines = singleLineHeaderFits
            ? Array.Empty<string>()
            : BoundHeaderLines(
                WrapPdfLine($"Bebé: {babyNameEscaped}", maxPdfCharsPerLine, maxPdfWidthPt),
                maxBabyHeaderLines);

        var headerLineCountEstimate = (singleLineHeaderFits ? 1 : 1 + wrappedBabyHeaderLines.Count) + 3;
        var availableBodyHeightPt = 760 - (headerLineCountEstimate * 16) - 8 - 50;
        var linesPerPage = Math.Max(1, Math.Min(38, (availableBodyHeightPt / 16) + 1));

        var pageChunks = new List<List<string>>();
        for (var i = 0; i < allBodyLines.Count; i += linesPerPage)
        {
            pageChunks.Add(allBodyLines.GetRange(i, Math.Min(linesPerPage, allBodyLines.Count - i)));
        }

        var totalPages = pageChunks.Count;

        // Objetos PDF: 1=Catalog, 2=Pages, 3=Font Helvetica, luego por página p: Page(4+p*2) y Contents(5+p*2)
        var pageObjIds = new int[totalPages];
        var contentStreams = new byte[totalPages][];
        for (var p = 0; p < totalPages; p++)
        {
            pageObjIds[p] = 4 + (p * 2);
            var cb = new StringBuilder();
            cb.Append("BT\n/F1 10 Tf\n");

            var y = 760;
            var headerLines = new List<string>();
            if (singleLineHeaderFits)
            {
                headerLines.Add($"Reporte Histórico Cuna Inteligente (RF32) - Bebé: {babyNameEscaped} (Pág. {p + 1}/{totalPages})");
            }
            else
            {
                headerLines.Add($"Reporte Histórico Cuna Inteligente (RF32) - Pág. {p + 1}/{totalPages}");
                headerLines.AddRange(wrappedBabyHeaderLines);
            }
            headerLines.Add(EscapePdfText($"Fecha de nacimiento: {baby.FechaNacimiento:yyyy-MM-dd} | Generado: {generatedAt} UTC"));
            headerLines.Add(EscapePdfText($"Sesiones de sueño: {sleepSessions.Count} | Sesiones de alimentación: {feedingSessions.Count}"));
            headerLines.Add("------------------------------------------------------------------------");

            foreach (var h in headerLines)
            {
                cb.Append($"1 0 0 1 50 {y} Tm ({h}) Tj\n");
                y -= 16;
            }

            // Separación vertical adicional tras el encabezado antes de la primera fila de datos
            y -= 8;

            foreach (var row in pageChunks[p])
            {
                cb.Append($"1 0 0 1 50 {y} Tm ({row}) Tj\n");
                y -= 16;
            }

            cb.Append("ET\n");
            contentStreams[p] = Encoding.Latin1.GetBytes(cb.ToString());
        }

        var totalObjects = 3 + (totalPages * 2);
        var offsets = new long[totalObjects + 1];

        using var ms = new MemoryStream();
        void WriteAscii(string text)
        {
            var bytes = Encoding.Latin1.GetBytes(text);
            ms.Write(bytes, 0, bytes.Length);
        }

        WriteAscii("%PDF-1.4\n");

        offsets[1] = ms.Position;
        WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = ms.Position;
        var kidsList = string.Join(" ", pageObjIds.Select(id => $"{id} 0 R"));
        WriteAscii($"2 0 obj\n<< /Type /Pages /Kids [{kidsList}] /Count {totalPages} >>\nendobj\n");

        offsets[3] = ms.Position;
        WriteAscii("3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

        for (var p = 0; p < totalPages; p++)
        {
            var pageId = 4 + (p * 2);
            var contentId = 5 + (p * 2);

            offsets[pageId] = ms.Position;
            WriteAscii(
                $"{pageId} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {contentId} 0 R /Resources << /Font << /F1 3 0 R >> >> >>\nendobj\n");

            offsets[contentId] = ms.Position;
            WriteAscii($"{contentId} 0 obj\n<< /Length {contentStreams[p].Length} >>\nstream\n");
            ms.Write(contentStreams[p], 0, contentStreams[p].Length);
            WriteAscii("endstream\nendobj\n");
        }

        var xrefOffset = ms.Position;
        WriteAscii($"xref\n0 {totalObjects + 1}\n");
        WriteAscii("0000000000 65535 f \n");
        for (var objId = 1; objId <= totalObjects; objId++)
        {
            WriteAscii($"{offsets[objId]:D10} 00000 n \n");
        }

        WriteAscii($"trailer\n<< /Size {totalObjects + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return ms.ToArray();
    }

    private static string EscapeCsv(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return "\"\"";
        }

        var sanitized = input.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ");
        if (sanitized.Length > 0 && (sanitized[0] is '=' or '+' or '-' or '@' or '\t'))
        {
            sanitized = "'" + sanitized;
        }

        return $"\"{sanitized}\"";
    }

    private static string EscapePdfText(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            switch (ch)
            {
                case '(':
                    sb.Append('[');
                    break;
                case ')':
                    sb.Append(']');
                    break;
                case '\\':
                    sb.Append('/');
                    break;
                case '\r':
                case '\n':
                case '\t':
                    sb.Append(' ');
                    break;
                default:
                    if ((ch >= 0x20 && ch <= 0x7E) || (ch >= 0xA0 && ch <= 0xFF))
                    {
                        sb.Append(ch);
                    }
                    else if (ch >= 0x7F)
                    {
                        sb.Append('?');
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    private static IReadOnlyList<string> BoundHeaderLines(IReadOnlyList<string> lines, int maxLines)
    {
        if (lines.Count <= maxLines)
        {
            return lines;
        }

        var bounded = new List<string>(maxLines);
        for (var i = 0; i < maxLines; i++)
        {
            bounded.Add(lines[i]);
        }

        var last = bounded[maxLines - 1];
        bounded[maxLines - 1] = last.Length > 3 ? last[..^3].TrimEnd() + "..." : "...";
        return bounded;
    }

    private static IReadOnlyList<string> WrapPdfLine(string escapedText, int maxChars, float maxWidthPt)
    {
        if (string.IsNullOrEmpty(escapedText))
        {
            return new[] { string.Empty };
        }

        if (escapedText.Length <= maxChars && EstimateHelveticaWidthPt(escapedText) <= maxWidthPt)
        {
            return new[] { escapedText };
        }

        var lines = new List<string>();
        var remaining = escapedText.AsSpan();
        var isFirst = true;

        while (remaining.Length > 0)
        {
            var prefix = isFirst ? string.Empty : "  ";
            var prefixWidth = EstimateHelveticaWidthPt(prefix);
            var charLimit = Math.Max(8, maxChars - prefix.Length);
            var widthLimit = Math.Max(80f, maxWidthPt - prefixWidth);

            var fitCount = 0;
            var accWidth = 0f;
            while (fitCount < remaining.Length && fitCount < charLimit)
            {
                var charWidth = GetHelveticaCharWidthPt(remaining[fitCount]);
                if (fitCount > 0 && accWidth + charWidth > widthLimit)
                {
                    break;
                }
                accWidth += charWidth;
                fitCount++;
            }

            if (fitCount == 0)
            {
                fitCount = 1;
            }

            if (fitCount >= remaining.Length)
            {
                lines.Add(prefix + remaining.ToString());
                break;
            }

            var slice = remaining[..fitCount];
            var breakIdx = slice.LastIndexOf(' ');
            if (breakIdx < fitCount / 3 || breakIdx <= 0)
            {
                breakIdx = fitCount;
            }

            lines.Add(prefix + remaining[..breakIdx].TrimEnd().ToString());
            remaining = remaining[breakIdx..].TrimStart();
            isFirst = false;
        }

        return lines;
    }

    private static float EstimateHelveticaWidthPt(string text, float fontSize = 10f)
    {
        var total = 0f;
        foreach (var ch in text)
        {
            total += GetHelveticaCharWidthPt(ch, fontSize);
        }
        return total;
    }

    private static float GetHelveticaCharWidthPt(char ch, float fontSize = 10f)
    {
        var thousandths = ch switch
        {
            ' ' => 278,
            '!' => 278,
            '"' => 355,
            '#' or '$' => 556,
            '%' => 889,
            '&' => 667,
            '\'' => 191,
            '(' or ')' => 333,
            '*' => 389,
            '+' => 584,
            ',' => 278,
            '-' => 333,
            '.' or '/' => 278,
            >= '0' and <= '9' => 556,
            ':' or ';' => 278,
            '<' or '=' or '>' => 584,
            '?' => 556,
            '@' => 1015,
            'A' or 'B' => 667,
            'C' or 'D' => 722,
            'E' => 667,
            'F' => 611,
            'G' => 778,
            'H' => 722,
            'I' => 278,
            'J' => 500,
            'K' => 667,
            'L' => 556,
            'M' => 833,
            'N' => 722,
            'O' => 778,
            'P' => 667,
            'Q' => 778,
            'R' => 722,
            'S' => 667,
            'T' => 611,
            'U' => 722,
            'V' => 667,
            'W' => 944,
            'X' or 'Y' => 667,
            'Z' => 611,
            '[' or ']' => 278,
            '_' => 556,
            '`' => 333,
            'a' or 'b' => 556,
            'c' => 500,
            'd' or 'e' => 556,
            'f' => 278,
            'g' or 'h' => 556,
            'i' or 'j' => 222,
            'k' => 500,
            'l' => 222,
            'm' => 833,
            'n' or 'o' or 'p' or 'q' => 556,
            'r' => 333,
            's' => 500,
            't' => 278,
            'u' => 556,
            'v' => 500,
            'w' => 722,
            'x' or 'y' or 'z' => 500,
            '{' or '}' => 334,
            '|' => 260,
            '~' => 584,
            '°' => 400,
            '¡' => 333,
            '¿' => 611,
            'Á' or 'É' or 'Ü' => 667,
            'Í' => 278,
            'Ó' => 778,
            'Ú' or 'Ñ' => 722,
            'á' or 'é' or 'ó' or 'ú' or 'ñ' or 'ü' => 556,
            'í' => 278,
            _ => 778
        };

        return thousandths * fontSize / 1000f;
    }

    private static string SanitizeFileName(string input)
    {
        var chars = input.Where(char.IsLetterOrDigit).ToArray();
        return chars.Length == 0 ? "bebe" : new string(chars).ToLowerInvariant();
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
