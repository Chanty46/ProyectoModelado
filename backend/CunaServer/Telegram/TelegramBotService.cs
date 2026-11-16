using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using CunaServer.Data;
using CunaServer.Services;

namespace CunaServer.Telegram;

/// <summary>
/// Servicio de aplicación para vinculación de Telegram (RF38), consultas autenticadas <c>/estado</c> (RF39),
/// despacho de alertas familiares (RF19) y recordatorios dinámicos de tomas (RF18).
/// </summary>
public sealed class TelegramBotService
{
    public const int LinkCodeExpirationMinutes = 10;
    public const int MaxFailedLinkAttemptsPerChat = 5;
    public static readonly TimeSpan LinkLockoutDuration = TimeSpan.FromMinutes(10);

    private sealed record ChatLinkAttemptState(int FailedCount, DateTimeOffset WindowResetAt, DateTimeOffset? LockedUntil);

    private readonly IFamilyAccountRepository _accountRepo;
    private readonly IDeviceTelemetryRepository _deviceRepo;
    private readonly ITelegramBotTransport _transport;
    private readonly string _botUsername;
    private readonly string _cleanBotHandle;
    private readonly ConcurrentDictionary<long, ChatLinkAttemptState> _linkAttemptsByChat = new();

    public TelegramBotService(
        IFamilyAccountRepository accountRepo,
        IDeviceTelemetryRepository deviceRepo,
        ITelegramBotTransport transport,
        IConfiguration? config = null)
    {
        _accountRepo = accountRepo;
        _deviceRepo = deviceRepo;
        _transport = transport;

        var configuredHandle = config?["TELEGRAM_BOT_USERNAME"];
        _cleanBotHandle = string.IsNullOrWhiteSpace(configuredHandle)
            ? "CunasInteligentes_Modelado_Bot"
            : configuredHandle.Trim().TrimStart('@');
        _botUsername = $"@{_cleanBotHandle}";
    }

    private string TransportMode => _transport is HttpTelegramBotTransport ? "telegram_http" : "inmemory";

    public async Task<ServiceResult<TelegramLinkStatusResponse>> GetLinkStatusAsync(
        AuthenticatedIdentity identity,
        CancellationToken ct)
    {
        var (membership, error) = await RequireMembershipAsync<TelegramLinkStatusResponse>(identity, ct);
        if (error is not null || membership is null)
        {
            return error!;
        }

        var link = await _accountRepo.GetTelegramLinkByUserAsync(identity.UserId, ct);
        var pendingExpiry = link is { CodigoUsado: false, CodigoExpiraEn: { } exp } && exp > DateTimeOffset.UtcNow
            ? exp
            : (DateTimeOffset?)null;

        return ServiceResult<TelegramLinkStatusResponse>.Ok(new TelegramLinkStatusResponse(
            identity.UserId,
            link?.ChatId.HasValue == true,
            link?.ChatId,
            link?.VinculadoEn,
            pendingExpiry));
    }

    /// <summary>
    /// RF38: Genera un código criptográfico de un solo uso (10 min de vigencia) almacenado únicamente como hash SHA-256.
    /// </summary>
    public async Task<ServiceResult<GenerateTelegramCodeResponse>> GenerateLinkCodeAsync(
        AuthenticatedIdentity identity,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, error) = await RequireMembershipAsync<GenerateTelegramCodeResponse>(identity, ct);
        if (error is not null || membership is null)
        {
            return error!;
        }

        var rawToken = CryptoTokenHelper.GenerateOpaqueToken(9).ToUpperInvariant();
        var code = $"TG-{rawToken}";
        var codeHash = CryptoTokenHelper.ComputeSha256Hex(code);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(LinkCodeExpirationMinutes);

        await _accountRepo.SaveTelegramLinkCodeAsync(identity.UserId, codeHash, expiresAt, ct);

        await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
            membership.FamilyId,
            identity.UserId,
            null,
            "telegram_codigo_generado_rf38",
            clientIp,
            $"expira_en={expiresAt:O}",
            DateTimeOffset.UtcNow), ct);

        var deepLinkUrl = $"https://t.me/{_cleanBotHandle}?start={Uri.EscapeDataString(code)}";

        return ServiceResult<GenerateTelegramCodeResponse>.Created(new GenerateTelegramCodeResponse(
            code,
            expiresAt,
            _botUsername,
            $"/vincular {code}",
            deepLinkUrl));
    }

    public async Task<ServiceResult<TelegramBotStatusResponse>> GetServiceStatusAsync(
        AuthenticatedIdentity identity,
        CancellationToken ct)
    {
        var (membership, error) = await RequireMembershipAsync<TelegramBotStatusResponse>(identity, ct);
        if (error is not null || membership is null)
        {
            return error!;
        }

        var linkTask = _accountRepo.GetTelegramLinkByUserAsync(identity.UserId, ct);
        var probeTask = _transport.ProbeBotAsync(ct);
        await Task.WhenAll(linkTask, probeTask);

        var link = await linkTask;
        var probe = await probeTask;

        const string phoneRuleNote =
            "Por políticas de Telegram Bot API no es posible enviar mensajes directo a un número telefónico; el cuidador debe abrir el bot y vincular su Chat ID con /vincular o mediante el enlace profundo /start.";

        return ServiceResult<TelegramBotStatusResponse>.Ok(new TelegramBotStatusResponse(
            probe.TokenConfigured,
            TransportMode,
            probe.Reachable,
            _botUsername,
            probe.ResolvedUsername,
            link?.ChatId.HasValue == true,
            link?.ChatId,
            $"{probe.Detail ?? TransportMode} {phoneRuleNote}"));
    }

    public async Task<ServiceResult<TelegramTestMessageResponse>> SendTestNotificationAsync(
        AuthenticatedIdentity identity,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, error) = await RequireMembershipAsync<TelegramTestMessageResponse>(identity, ct);
        if (error is not null || membership is null)
        {
            return error!;
        }

        var familyTask = _accountRepo.GetFamilyAsync(membership.FamilyId, ct);
        var linkTask = _accountRepo.GetTelegramLinkByUserAsync(identity.UserId, ct);
        await Task.WhenAll(familyTask, linkTask);

        var family = await familyTask;
        var link = await linkTask;
        if (link?.ChatId is not { } chatId)
        {
            return ServiceResult<TelegramTestMessageResponse>.BadRequest(
                "Tu cuenta aún no tiene un Chat ID de Telegram vinculado. Genera un código y envía /vincular CODIGO al bot primero.");
        }

        var familyLabel = string.IsNullOrWhiteSpace(family?.Nombre) ? "tu familia" : family.Nombre;
        var text = $"[PRUEBA] Cuna Inteligente: Verificación de notificaciones activa para {familyLabel} ({DateTimeOffset.UtcNow:HH:mm:ss} UTC).";
        var sent = await _transport.SendMessageAsync(chatId, text, ct);

        await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
            membership.FamilyId,
            identity.UserId,
            null,
            sent ? "telegram_prueba_enviada" : "telegram_prueba_fallida",
            clientIp,
            $"chat:{chatId}",
            DateTimeOffset.UtcNow), ct);

        return ServiceResult<TelegramTestMessageResponse>.Ok(new TelegramTestMessageResponse(
            sent,
            chatId,
            TransportMode,
            sent
                ? "Mensaje de prueba entregado al Chat ID vinculado."
                : "No se pudo entregar el mensaje de prueba; revisa la conectividad del bot y tu TELEGRAM_BOT_TOKEN."));
    }

    public async Task<ServiceResult<bool>> UnlinkMyTelegramAsync(
        AuthenticatedIdentity identity,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, error) = await RequireMembershipAsync<bool>(identity, ct);
        if (error is not null || membership is null)
        {
            return error!;
        }

        await _accountRepo.DeleteTelegramLinkByUserAsync(identity.UserId, ct);
        await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
            membership.FamilyId,
            identity.UserId,
            null,
            "telegram_desvinculado_rf38",
            clientIp,
            null,
            DateTimeOffset.UtcNow), ct);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<FeedingReminderRecord>> GetFeedingReminderAsync(
        AuthenticatedIdentity identity,
        CancellationToken ct)
    {
        var (membership, error) = await RequireMembershipAsync<FeedingReminderRecord>(identity, ct);
        if (error is not null || membership is null)
        {
            return error!;
        }

        var reminder = await _accountRepo.GetOrCreateFeedingReminderAsync(membership.FamilyId, ct);
        return ServiceResult<FeedingReminderRecord>.Ok(reminder);
    }

    public async Task<ServiceResult<FeedingReminderRecord>> UpdateFeedingReminderAsync(
        AuthenticatedIdentity identity,
        UpdateFeedingReminderRequest request,
        string? clientIp,
        CancellationToken ct)
    {
        var (membership, error) = await RequireMembershipAsync<FeedingReminderRecord>(identity, ct);
        if (error is not null || membership is null)
        {
            return error!;
        }

        if (request.IntervaloHoras is < 1 or > 8)
        {
            return ServiceResult<FeedingReminderRecord>.BadRequest(
                "El intervalo del recordatorio de tomas debe estar entre 1 y 8 horas (RF18).");
        }

        var current = await _accountRepo.GetOrCreateFeedingReminderAsync(membership.FamilyId, ct);
        var updated = await _accountRepo.UpdateFeedingReminderAsync(
            membership.FamilyId,
            request.IntervaloHoras,
            request.Activo,
            current.UltimoEnvio ?? DateTimeOffset.UtcNow,
            ct);

        await _deviceRepo.LogCaregiverActionAsync(new CaregiverAuditLogEntry(
            membership.FamilyId,
            identity.UserId,
            request.PerfilId,
            "configurar_recordatorio_rf18",
            JsonSerializer.Serialize(new { intervalo_h = request.IntervaloHoras, activo = request.Activo }),
            DateTimeOffset.UtcNow), ct);

        return ServiceResult<FeedingReminderRecord>.Ok(updated);
    }

    /// <summary>
    /// Procesa comandos entrantes del bot de Telegram (<c>/start</c>, <c>/vincular</c>, <c>/estado</c>, <c>/mi_chat</c>, <c>/ayuda</c>).
    /// </summary>
    public async Task ProcessInboundCommandAsync(
        long chatId,
        string rawText,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var text = (rawText ?? "").Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = NormalizeCommandToken(parts[0]);

        switch (cmd)
        {
            case "/start":
                await HandleStartCommandAsync(chatId, parts, now, ct);
                break;

            case "/vincular":
                await HandleVincularCommandAsync(chatId, parts, now, ct);
                break;

            case "/mi_chat":
                await HandleMiChatCommandAsync(chatId, ct);
                break;

            case "/estado":
                await HandleEstadoCommandAsync(chatId, now, ct);
                break;

            case "/ayuda":
            case "/help":
                await _transport.SendMessageAsync(chatId, TelegramMessages.Help, ct);
                break;

            default:
                await _transport.SendMessageAsync(chatId, TelegramMessages.UnknownCommand, ct);
                break;
        }
    }

    /// <summary>
    /// RF19 / D6: Despacha una alerta de la cuna a TODOS los usuarios vinculados de la familia.
    /// </summary>
    public async Task<int> DispatchAlertForCribAsync(
        Guid cunaId,
        EventAlertRecord alert,
        CancellationToken ct)
    {
        var crib = await _deviceRepo.GetCribByIdAsync(cunaId, ct);
        if (crib is null)
        {
            return 0;
        }

        var chats = await _accountRepo.ListLinkedTelegramChatsByFamilyAsync(crib.FamilyId, ct);
        if (chats.Count == 0)
        {
            return 0;
        }

        var text = TelegramMessages.Alert(crib, alert);

        var delivered = 0;
        foreach (var chatId in chats)
        {
            if (await _transport.SendMessageAsync(chatId, text, ct))
            {
                delivered++;
            }
        }
        return delivered;
    }

    /// <summary>
    /// RF18: Evalúa los recordatorios dinámicos de tomas (1–8 h) y notifica a los miembros vinculados.
    /// </summary>
    public async Task<int> EvaluateFeedingRemindersAsync(DateTimeOffset now, CancellationToken ct)
    {
        var activeReminders = await _accountRepo.ListActiveFeedingRemindersAsync(ct);
        var sentFamilies = 0;

        foreach (var rem in activeReminders)
        {
            var interval = TimeSpan.FromHours(rem.IntervaloHoras);
            var referenceTime = rem.UltimoEnvio ?? rem.ActualizadoEn;
            if ((now - referenceTime) < interval)
            {
                continue;
            }

            await DispatchFamilyFeedingReminderAsync(rem, now, ct);
            sentFamilies++;
        }

        return sentFamilies;
    }

    private async Task<(UserMembership? Membership, ServiceResult<T>? Error)> RequireMembershipAsync<T>(
        AuthenticatedIdentity identity,
        CancellationToken ct)
    {
        var membership = await _accountRepo.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            return (null, ServiceResult<T>.Forbidden("El usuario no pertenece a una familia activa."));
        }
        return (membership, null);
    }

    private static string NormalizeCommandToken(string rawCommandToken)
    {
        var atIdx = rawCommandToken.IndexOf('@');
        var withoutBotHandle = atIdx > 0 ? rawCommandToken[..atIdx] : rawCommandToken;
        return withoutBotHandle.ToLowerInvariant();
    }

    private Task HandleStartCommandAsync(long chatId, string[] parts, DateTimeOffset now, CancellationToken ct)
    {
        if (parts.Length >= 2)
        {
            return HandleLinkAttemptAsync(chatId, parts[1], now, ct);
        }

        return _transport.SendMessageAsync(chatId, TelegramMessages.Welcome, ct);
    }

    private Task HandleVincularCommandAsync(long chatId, string[] parts, DateTimeOffset now, CancellationToken ct)
    {
        if (parts.Length < 2)
        {
            return _transport.SendMessageAsync(chatId, TelegramMessages.LinkMissingCode, ct);
        }

        return HandleLinkAttemptAsync(chatId, parts[1], now, ct);
    }

    private async Task HandleMiChatCommandAsync(long chatId, CancellationToken ct)
    {
        var existing = await _accountRepo.GetTelegramLinkByChatIdAsync(chatId, ct);
        var linkedMembership = existing is not null
            ? await _accountRepo.GetUserMembershipAsync(existing.UsuarioId, ct)
            : null;
        var statusLabel = linkedMembership is not null
            ? $"vinculado a {linkedMembership.Email} ({linkedMembership.Rol})"
            : "no vinculado";

        await _transport.SendMessageAsync(chatId, $"Chat ID: {chatId}\nEstado: {statusLabel}.", ct);
    }

    private async Task HandleEstadoCommandAsync(long chatId, DateTimeOffset now, CancellationToken ct)
    {
        // RF39: Solo responde a chats vinculados con membresía activa en usuarios
        var link = await _accountRepo.GetTelegramLinkByChatIdAsync(chatId, ct);
        var membership = link is not null
            ? await _accountRepo.GetUserMembershipAsync(link.UsuarioId, ct)
            : null;

        if (link is null || membership is null)
        {
            await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                null,
                link?.UsuarioId,
                null,
                "telegram_estado_rechazado_no_vinculado",
                $"chat:{chatId}",
                "chat_no_vinculado_o_usuario_revocado",
                now), ct);

            await _transport.SendMessageAsync(chatId, TelegramMessages.AccessDenied, ct);
            return;
        }

        var cribs = await _deviceRepo.ListCribsByFamilyAsync(membership.FamilyId, ct);
        if (cribs.Count == 0)
        {
            await _transport.SendMessageAsync(chatId, TelegramMessages.NoCribs, ct);
            return;
        }

        var babies = await _deviceRepo.ListBabiesByFamilyAsync(membership.FamilyId, ct);
        var statuses = await Task.WhenAll(cribs.Select(c => BuildCribStatusAsync(c, babies, ct)));

        await _transport.SendMessageAsync(chatId, TelegramMessages.Status(statuses, now), ct);
    }

    /// <summary>
    /// Junta para /estado el último valor conocido de cada sensor (central, colchón y biberón),
    /// el bebé asignado a la cuna y su última toma.
    /// </summary>
    private async Task<TelegramMessages.CribStatus> BuildCribStatusAsync(
        CribRecord crib,
        IReadOnlyList<BabyRecord> familyBabies,
        CancellationToken ct)
    {
        var telemetry = await _deviceRepo.ListTelemetryForCribAsync(crib.Id, ct);
        var latest = CribLiveControlService.ConsolidateLatestTelemetry(telemetry);
        var baby = familyBabies.FirstOrDefault(b => b.CunaId == crib.Id);

        FeedingSessionRecord? lastFeeding = null;
        if (baby is not null)
        {
            lastFeeding = (await _deviceRepo.ListFeedingSessionsByBabyAsync(baby.Id, ct))
                .OrderByDescending(f => f.Inicio)
                .FirstOrDefault();
        }

        return new TelegramMessages.CribStatus(
            crib,
            baby,
            latest?.TempC,
            latest?.Humedad,
            latest?.MasaKg,
            latest?.Ts,
            lastFeeding,
            latest?.TempBiberonC,
            latest?.MasaBiberonG);
    }

    private async Task HandleLinkAttemptAsync(
        long chatId,
        string candidateCode,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (await IsChatLockedOutAsync(chatId, now, ct))
        {
            return;
        }

        var rawCode = candidateCode.Trim();
        if (rawCode.Length < 6)
        {
            await _transport.SendMessageAsync(chatId, TelegramMessages.LinkMissingCode, ct);
            return;
        }

        var hash = CryptoTokenHelper.ComputeSha256Hex(rawCode);
        var linked = await _accountRepo.CompleteTelegramLinkByCodeAsync(hash, chatId, now, ct);
        if (linked is null)
        {
            await RegisterFailedLinkAttemptAsync(chatId, now, ct);
            return;
        }

        _linkAttemptsByChat.TryRemove(chatId, out _);

        var membership = await _accountRepo.GetUserMembershipAsync(linked.UsuarioId, ct);
        await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
            membership?.FamilyId,
            linked.UsuarioId,
            null,
            "telegram_vinculado_rf38",
            $"chat:{chatId}",
            null,
            now), ct);

        await _transport.SendMessageAsync(chatId, TelegramMessages.LinkOk, ct);
    }

    private async Task<bool> IsChatLockedOutAsync(long chatId, DateTimeOffset now, CancellationToken ct)
    {
        if (!_linkAttemptsByChat.TryGetValue(chatId, out var existingState) ||
            existingState.LockedUntil is not { } lockedUntil ||
            now >= lockedUntil)
        {
            return false;
        }

        await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
            null,
            null,
            null,
            "telegram_vinculo_bloqueado",
            $"chat:{chatId}",
            "limite_intentos_por_chat",
            now), ct);

        await _transport.SendMessageAsync(
            chatId,
            "Demasiados intentos fallidos desde este chat. Espera 10 minutos antes de intentar nuevamente.",
            ct);
        return true;
    }

    private async Task RegisterFailedLinkAttemptAsync(long chatId, DateTimeOffset now, CancellationToken ct)
    {
        var updatedState = _linkAttemptsByChat.AddOrUpdate(
            chatId,
            _ => new ChatLinkAttemptState(1, now.Add(LinkLockoutDuration), null),
            (_, prev) =>
            {
                if (now >= prev.WindowResetAt)
                {
                    return new ChatLinkAttemptState(1, now.Add(LinkLockoutDuration), null);
                }

                var nextCount = prev.FailedCount + 1;
                DateTimeOffset? nextLock = nextCount >= MaxFailedLinkAttemptsPerChat
                    ? now.Add(LinkLockoutDuration)
                    : null;
                return new ChatLinkAttemptState(nextCount, prev.WindowResetAt, nextLock);
            });

        var isLockedNow = updatedState.LockedUntil.HasValue && now < updatedState.LockedUntil.Value;
        await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
            null,
            null,
            null,
            isLockedNow ? "telegram_vinculo_bloqueado" : "telegram_vinculo_fallido",
            $"chat:{chatId}",
            isLockedNow ? "limite_intentos_por_chat" : "codigo_invalido_expirado_o_usado",
            now), ct);

        await _transport.SendMessageAsync(
            chatId,
            isLockedNow
                ? "Demasiados intentos fallidos desde este chat. Espera 10 minutos antes de intentar nuevamente."
                : TelegramMessages.LinkFailed,
            ct);
    }

    private async Task DispatchFamilyFeedingReminderAsync(
        FeedingReminderRecord rem,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await _accountRepo.UpdateFeedingReminderAsync(
            rem.FamilyId,
            rem.IntervaloHoras,
            rem.Activo,
            now,
            ct);

        var cribs = await _deviceRepo.ListCribsByFamilyAsync(rem.FamilyId, ct);
        // El evento guardado conserva el texto técnico; por Telegram va la versión para el cuidador.
        var msg = $"Recordatorio de alimentación (RF18): han transcurrido {rem.IntervaloHoras} h desde la última toma.";
        var telegramMsg = TelegramMessages.FeedingReminder(rem.IntervaloHoras);

        foreach (var crib in cribs)
        {
            await _deviceRepo.SaveEventAlertAsync(new EventAlertRecord(
                Guid.NewGuid(),
                crib.Id,
                Guid.Empty,
                "feeding_reminder",
                "info",
                msg,
                null,
                now), ct);
        }

        var chats = await _accountRepo.ListLinkedTelegramChatsByFamilyAsync(rem.FamilyId, ct);
        foreach (var chatId in chats)
        {
            await _transport.SendMessageAsync(chatId, telegramMsg, ct);
        }
    }
}

/// <summary>
/// Servicio en segundo plano que realiza el sondeo periódico de Telegram (<c>getUpdates</c>)
/// y dispara cada 60 s la evaluación de recordatorios de alimentación RF18.
/// </summary>
public sealed class TelegramPollingAndRemindersBackgroundService : BackgroundService
{
    private readonly TelegramBotService _botService;
    private readonly ITelegramBotTransport _transport;
    private readonly ILogger<TelegramPollingAndRemindersBackgroundService> _logger;
    private long _nextOffset = 1;
    private DateTimeOffset _lastReminderCheckUtc = DateTimeOffset.MinValue;

    public TelegramPollingAndRemindersBackgroundService(
        TelegramBotService botService,
        ITelegramBotTransport transport,
        ILogger<TelegramPollingAndRemindersBackgroundService> logger)
    {
        _botService = botService;
        _transport = transport;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isHttp = _transport is HttpTelegramBotTransport;
        if (isHttp)
        {
            await ProbeBotOnStartupAsync(stoppingToken);
        }

        var pollDelay = isHttp ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunPollingIterationAsync(stoppingToken);

            try
            {
                await Task.Delay(pollDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProbeBotOnStartupAsync(CancellationToken ct)
    {
        var probe = await _transport.ProbeBotAsync(ct);
        if (probe.Reachable && !string.IsNullOrWhiteSpace(probe.ResolvedUsername))
        {
            _logger.LogInformation("Sondeo Telegram conectado como @{Username}.", probe.ResolvedUsername);
        }
        else if (!probe.Reachable)
        {
            _logger.LogWarning("Sondeo Telegram no pudo verificar getMe: {Detail}", probe.Detail);
        }
    }

    private async Task RunPollingIterationAsync(CancellationToken stoppingToken)
    {
        try
        {
            var updates = await _transport.GetUpdatesAsync(_nextOffset, stoppingToken);
            foreach (var upd in updates)
            {
                _nextOffset = Math.Max(_nextOffset, upd.UpdateId + 1);
                await _botService.ProcessInboundCommandAsync(upd.ChatId, upd.Text, DateTimeOffset.UtcNow, stoppingToken);
            }

            var now = DateTimeOffset.UtcNow;
            if (now - _lastReminderCheckUtc >= TimeSpan.FromSeconds(60))
            {
                await _botService.EvaluateFeedingRemindersAsync(now, stoppingToken);
                _lastReminderCheckUtc = now;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Cierre normal del host
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error en ciclo de sondeo de Telegram / recordatorios RF18.");
        }
    }
}
