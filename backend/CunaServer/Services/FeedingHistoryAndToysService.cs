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

        var sleepAll = await _deviceRepo.ListSleepSessionsByBabyAsync(bebeId, ct);
        var feedingAll = await _deviceRepo.ListFeedingSessionsByBabyAsync(bebeId, ct);

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
            await _tcpServer.SendCommandAsync(
                central.DeviceId,
                "song_play",
                new
                {
                    action = accion,
                    song = song?.Codigo ?? request.Codigo,
                    duration_s = song?.DuracionSegundos ?? 0,
                    notes = song is not null ? JsonDocument.Parse(song.NotasJson).RootElement : (object?)null
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

        var usage = await _deviceRepo.ListToyUsageForCribAsync(cunaId, ct);
        var devices = await _deviceRepo.ListDevicesByCribAsync(cunaId, ct);
        var telemetry = await _deviceRepo.ListTelemetryForCribAsync(cunaId, ct);

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
        var lines = new List<string>
        {
            $"Reporte Historico Cuna Inteligente (RF32) - Bebe: {EscapePdfText(baby.Nombre)}",
            $"Fecha de nacimiento: {baby.FechaNacimiento:yyyy-MM-dd} | Generado: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC",
            $"Sesiones de sueno: {sleepSessions.Count} | Sesiones de alimentacion: {feedingSessions.Count}",
            "------------------------------------------------------------------------"
        };

        foreach (var f in feedingSessions.Take(15))
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"[Toma] {f.Inicio:yyyy-MM-dd HH:mm} | Inicial: {f.MasaInicialG:F1}g | Final: {f.MasaFinalG ?? 0:F1}g | Vol: {f.MlConsumidos ?? 0:F1} ml | T: {f.TempC ?? 0:F1} C"));
        }

        foreach (var s in sleepSessions.Take(15))
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"[Sueno {s.Tipo}] {s.Inicio:yyyy-MM-dd HH:mm} | Mov prom: {s.MovimientoPromedio ?? 0:F3} kg/m | Interrupciones: {s.Interrupciones}"));
        }

        var contentBuilder = new StringBuilder();
        contentBuilder.AppendLine("BT");
        contentBuilder.AppendLine("/F1 10 Tf");
        var y = 760;
        foreach (var line in lines)
        {
            contentBuilder.AppendLine($"50 {y} Td ({line}) Tj");
            contentBuilder.AppendLine($"-50 -{y} Td");
            y -= 16;
            if (y < 60)
            {
                break;
            }
        }
        contentBuilder.AppendLine("ET");
        var streamContent = contentBuilder.ToString();

        var pdf = new StringBuilder();
        pdf.AppendLine("%PDF-1.4");
        pdf.AppendLine("1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj");
        pdf.AppendLine("2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj");
        pdf.AppendLine("3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >> endobj");
        pdf.AppendLine($"4 0 obj << /Length {Encoding.ASCII.GetByteCount(streamContent)} >> stream");
        pdf.Append(streamContent);
        pdf.AppendLine("endstream endobj");
        pdf.AppendLine("5 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj");
        pdf.AppendLine("trailer << /Root 1 0 R >>");
        pdf.AppendLine("%%EOF");

        return Encoding.ASCII.GetBytes(pdf.ToString());
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
        => input.Replace("(", "[").Replace(")", "]").Replace("\\", "/");

    private static string SanitizeFileName(string input)
    {
        var chars = input.Where(char.IsLetterOrDigit).ToArray();
        return chars.Length == 0 ? "bebe" : new string(chars).ToLowerInvariant();
    }

    private async Task<(UserMembership? Membership, BabyRecord? Baby, string? ForbiddenMessage)> VerifyBabyAccessAsync(
        AuthenticatedIdentity identity,
        Guid bebeId,
        string action,
        string? clientIp,
        CancellationToken ct)
    {
        var membership = await _accountRepo.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                null,
                identity.UserId,
                null,
                "acceso_denegado_403",
                clientIp,
                $"sin_familia:{action}",
                DateTimeOffset.UtcNow), ct);
            return (null, null, "Acceso denegado: el usuario no pertenece a una familia activa.");
        }

        var baby = await _deviceRepo.GetBabyByIdAsync(bebeId, ct);
        if (baby is null || baby.FamilyId != membership.FamilyId)
        {
            await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                membership.FamilyId,
                identity.UserId,
                null,
                "acceso_denegado_403",
                clientIp,
                $"bebe_ajeno:{action}:{bebeId}",
                DateTimeOffset.UtcNow), ct);
            return (null, null, "Acceso denegado: el perfil de bebé no pertenece a tu familia.");
        }

        return (membership, baby, null);
    }

    private async Task<(UserMembership? Membership, CribRecord? Crib, string? ForbiddenMessage)> VerifyCribAccessAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        string action,
        string? clientIp,
        CancellationToken ct)
    {
        var membership = await _accountRepo.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                null,
                identity.UserId,
                null,
                "acceso_denegado_403",
                clientIp,
                $"sin_familia:{action}",
                DateTimeOffset.UtcNow), ct);
            return (null, null, "Acceso denegado: el usuario no pertenece a una familia activa.");
        }

        var crib = await _deviceRepo.GetCribByIdAsync(cunaId, ct);
        if (crib is null || crib.FamilyId != membership.FamilyId)
        {
            await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                membership.FamilyId,
                identity.UserId,
                null,
                "acceso_denegado_403",
                clientIp,
                $"cuna_ajena:{action}:{cunaId}",
                DateTimeOffset.UtcNow), ct);
            return (null, null, "Acceso denegado: la cuna no pertenece a tu familia.");
        }

        return (membership, crib, null);
    }
}
