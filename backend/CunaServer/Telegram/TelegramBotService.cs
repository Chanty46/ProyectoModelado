using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CunaServer.Data;
using CunaServer.Services;

namespace CunaServer.Telegram;

public sealed record TelegramInboundMessage(
    long UpdateId,
    long ChatId,
    string Text);

public sealed record TelegramSentMessage(
    long ChatId,
    string Text,
    DateTimeOffset SentAt);

public sealed record TelegramBotProbeResult(
    bool TokenConfigured,
    bool Reachable,
    string? ResolvedUsername,
    int? LastHttpStatusCode,
    string? Detail);

public interface ITelegramBotTransport
{
    Task<IReadOnlyList<TelegramInboundMessage>> GetUpdatesAsync(long offset, CancellationToken ct);
    Task<bool> SendMessageAsync(long chatId, string text, CancellationToken ct);
    Task<TelegramBotProbeResult> ProbeBotAsync(CancellationToken ct)
        => Task.FromResult(new TelegramBotProbeResult(false, true, null, null, "inmemory"));
}

public sealed class InMemoryTelegramBotTransport : ITelegramBotTransport
{
    private readonly ConcurrentQueue<TelegramInboundMessage> _inbound = new();
    private readonly ConcurrentQueue<TelegramSentMessage> _sent = new();

    public void EnqueueInbound(long chatId, string text, long updateId = 0)
    {
        _inbound.Enqueue(new TelegramInboundMessage(
            updateId == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : updateId,
            chatId,
            text));
    }

    public IReadOnlyList<TelegramSentMessage> GetSentMessages()
        => _sent.ToList();

    public Task<IReadOnlyList<TelegramInboundMessage>> GetUpdatesAsync(long offset, CancellationToken ct)
    {
        var list = new List<TelegramInboundMessage>();
        while (_inbound.TryDequeue(out var msg))
        {
            if (msg.UpdateId >= offset)
            {
                list.Add(msg);
            }
        }
        return Task.FromResult<IReadOnlyList<TelegramInboundMessage>>(list);
    }

    public Task<bool> SendMessageAsync(long chatId, string text, CancellationToken ct)
    {
        _sent.Enqueue(new TelegramSentMessage(chatId, text, DateTimeOffset.UtcNow));
        return Task.FromResult(true);
    }

    public Task<TelegramBotProbeResult> ProbeBotAsync(CancellationToken ct)
        => Task.FromResult(new TelegramBotProbeResult(
            TokenConfigured: false,
            Reachable: true,
            ResolvedUsername: null,
            LastHttpStatusCode: null,
            Detail: "Transporte local en memoria activo (sin TELEGRAM_BOT_TOKEN)."));
}

public sealed class HttpTelegramBotTransport : ITelegramBotTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly string _botToken;
    private readonly string _apiBaseUrl;
    private readonly ILogger<HttpTelegramBotTransport> _logger;
    private int _webhookCleared;
    private long _highestSeenUpdateId;

    public HttpTelegramBotTransport(
        HttpClient http,
        string botToken,
        ILogger<HttpTelegramBotTransport> logger,
        string apiBaseUrl = "https://api.telegram.org")
    {
        _http = http;
        _botToken = NormalizeBotToken(botToken);
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
        _logger = logger;
    }

    private static string NormalizeBotToken(string rawToken)
    {
        var token = (rawToken ?? string.Empty).Trim().Trim('"', '\'');
        if (token.StartsWith("bot", StringComparison.OrdinalIgnoreCase) &&
            token.Length > 4 &&
            char.IsDigit(token[3]) &&
            token.Contains(':'))
        {
            token = token[3..];
        }
        return token;
    }

    private async Task EnsureWebhookClearedOnceAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _webhookCleared, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var url = $"{_apiBaseUrl}/bot{_botToken}/deleteWebhook?drop_pending_updates=false";
            using var res = await _http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
            {
                var detail = await TryReadErrorDescriptionAsync(res, ct);
                _logger.LogWarning(
                    "Telegram deleteWebhook respondió HTTP {StatusCode} ({Detail}).",
                    (int)res.StatusCode,
                    detail ?? "sin detalle");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Interlocked.Exchange(ref _webhookCleared, 0);
            _logger.LogWarning("No se pudo ejecutar deleteWebhook en Telegram ({ErrorType}).", ex.GetType().Name);
        }
    }

    public async Task<IReadOnlyList<TelegramInboundMessage>> GetUpdatesAsync(long offset, CancellationToken ct)
    {
        try
        {
            await EnsureWebhookClearedOnceAsync(ct);

            var highest = Volatile.Read(ref _highestSeenUpdateId);
            var effectiveOffset = highest > 0 ? Math.Max(offset, highest + 1) : offset;
            var url = $"{_apiBaseUrl}/bot{_botToken}/getUpdates?offset={effectiveOffset}&timeout=2";
            using var res = await _http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
            {
                var detail = await TryReadErrorDescriptionAsync(res, ct);
                _logger.LogWarning(
                    "Telegram getUpdates respondió HTTP {StatusCode} ({Detail}). Verifica TELEGRAM_BOT_TOKEN o conflictos con otro proceso.",
                    (int)res.StatusCode,
                    detail ?? "sin detalle");
                return Array.Empty<TelegramInboundMessage>();
            }

            var resp = await res.Content.ReadFromJsonAsync<TelegramUpdatesEnvelope>(JsonOptions, ct);
            if (resp?.Ok != true || resp.Result is null)
            {
                return Array.Empty<TelegramInboundMessage>();
            }

            foreach (var upd in resp.Result)
            {
                UpdateHighestSeen(upd.UpdateId);
            }

            return resp.Result
                .Where(u => u.Message?.Chat is not null && !string.IsNullOrWhiteSpace(u.Message.Text))
                .Select(u => new TelegramInboundMessage(
                    u.UpdateId,
                    u.Message!.Chat!.Id,
                    u.Message.Text!))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Fallo de red consultando getUpdates de Telegram ({ErrorType}).", ex.GetType().Name);
            return Array.Empty<TelegramInboundMessage>();
        }
    }

    private void UpdateHighestSeen(long updateId)
    {
        long current;
        while ((current = Volatile.Read(ref _highestSeenUpdateId)) < updateId)
        {
            if (Interlocked.CompareExchange(ref _highestSeenUpdateId, updateId, current) == current)
            {
                break;
            }
        }
    }

    public async Task<bool> SendMessageAsync(long chatId, string text, CancellationToken ct)
    {
        try
        {
            var sanitizedText = text.Length > 4000 ? text[..4000] + "..." : text;
            var url = $"{_apiBaseUrl}/bot{_botToken}/sendMessage";
            var payload = new
            {
                chat_id = chatId,
                text = sanitizedText
            };

            using var res = await _http.PostAsJsonAsync(url, payload, JsonOptions, ct);
            if (res.IsSuccessStatusCode)
            {
                return true;
            }

            if ((int)res.StatusCode == StatusCodes.Status429TooManyRequests)
            {
                var retryAfterSeconds = await TryReadRetryAfterSecondsAsync(res, ct);
                var delay = TimeSpan.FromSeconds(Math.Clamp(retryAfterSeconds, 1, 5));
                _logger.LogWarning(
                    "Telegram sendMessage limitó tasa (HTTP 429) para chat_id={ChatId}; reintentando en {DelaySeconds}s.",
                    chatId,
                    delay.TotalSeconds);
                await Task.Delay(delay, ct);

                using var retryRes = await _http.PostAsJsonAsync(url, payload, JsonOptions, ct);
                if (retryRes.IsSuccessStatusCode)
                {
                    return true;
                }

                var retryDetail = await TryReadErrorDescriptionAsync(retryRes, ct);
                _logger.LogWarning(
                    "Reintento de Telegram sendMessage falló con HTTP {StatusCode} para chat_id={ChatId} ({Detail}).",
                    (int)retryRes.StatusCode,
                    chatId,
                    retryDetail ?? "sin detalle");
                return false;
            }

            var errDetail = await TryReadErrorDescriptionAsync(res, ct);
            _logger.LogWarning(
                "Telegram sendMessage rechazado con HTTP {StatusCode} para chat_id={ChatId} ({Detail}).",
                (int)res.StatusCode,
                chatId,
                errDetail ?? "sin detalle");
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Fallo de red enviando mensaje a Telegram (chat_id={ChatId}, {ErrorType}).", chatId, ex.GetType().Name);
            return false;
        }
    }

    public async Task<TelegramBotProbeResult> ProbeBotAsync(CancellationToken ct)
    {
        try
        {
            var url = $"{_apiBaseUrl}/bot{_botToken}/getMe";
            using var res = await _http.GetAsync(url, ct);
            var statusCode = (int)res.StatusCode;
            if (!res.IsSuccessStatusCode)
            {
                return new TelegramBotProbeResult(
                    TokenConfigured: true,
                    Reachable: false,
                    ResolvedUsername: null,
                    LastHttpStatusCode: statusCode,
                    Detail: $"Telegram API respondió HTTP {statusCode}. Verifica TELEGRAM_BOT_TOKEN.");
            }

            var env = await res.Content.ReadFromJsonAsync<TelegramGetMeEnvelope>(JsonOptions, ct);
            if (env?.Ok != true || env.Result is null)
            {
                return new TelegramBotProbeResult(
                    TokenConfigured: true,
                    Reachable: false,
                    ResolvedUsername: null,
                    LastHttpStatusCode: statusCode,
                    Detail: "Respuesta inesperada de getMe en Telegram API.");
            }

            return new TelegramBotProbeResult(
                TokenConfigured: true,
                Reachable: true,
                ResolvedUsername: env.Result.Username,
                LastHttpStatusCode: statusCode,
                Detail: "Conexión verificada con Bot API de Telegram.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new TelegramBotProbeResult(
                TokenConfigured: true,
                Reachable: false,
                ResolvedUsername: null,
                LastHttpStatusCode: null,
                Detail: $"Error de red consultando Telegram API ({ex.GetType().Name}).");
        }
    }

    private static async Task<int> TryReadRetryAfterSecondsAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var err = await response.Content.ReadFromJsonAsync<TelegramErrorEnvelope>(JsonOptions, ct);
            return err?.Parameters?.RetryAfter ?? 1;
        }
        catch
        {
            return 1;
        }
    }

    private static async Task<string?> TryReadErrorDescriptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var err = await response.Content.ReadFromJsonAsync<TelegramErrorEnvelope>(JsonOptions, ct);
            return string.IsNullOrWhiteSpace(err?.Description) ? null : err.Description;
        }
        catch
        {
            return null;
        }
    }

    private sealed record TelegramUpdatesEnvelope(bool Ok, List<TelegramUpdateItem>? Result);
    private sealed record TelegramUpdateItem(long UpdateId, TelegramMessageItem? Message);
    private sealed record TelegramMessageItem(TelegramChatItem? Chat, string? Text);
    private sealed record TelegramChatItem(long Id);
    private sealed record TelegramGetMeEnvelope(bool Ok, TelegramBotUserItem? Result);
    private sealed record TelegramBotUserItem(long Id, bool IsBot, string? Username);
    private sealed record TelegramErrorEnvelope(bool Ok, string? Description, TelegramRetryParams? Parameters);
    private sealed record TelegramRetryParams(int? RetryAfter);
}

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
        var cleanHandle = string.IsNullOrWhiteSpace(configuredHandle)
            ? "CunasInteligentes_Modelado_Bot"
            : configuredHandle.Trim().TrimStart('@');
        _botUsername = $"@{cleanHandle}";
    }

    public async Task<ServiceResult<TelegramLinkStatusResponse>> GetLinkStatusAsync(
        AuthenticatedIdentity identity,
        CancellationToken ct)
    {
        var membership = await _accountRepo.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            return ServiceResult<TelegramLinkStatusResponse>.Forbidden("El usuario no pertenece a una familia activa.");
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

    public async Task<ServiceResult<GenerateTelegramCodeResponse>> GenerateLinkCodeAsync(
        AuthenticatedIdentity identity,
        string? clientIp,
        CancellationToken ct)
    {
        var membership = await _accountRepo.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            return ServiceResult<GenerateTelegramCodeResponse>.Forbidden("El usuario no pertenece a una familia activa.");
        }

        // Código aleatorio criptográfico de un solo uso con vigencia de 10 minutos (RF38)
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

        var cleanHandle = _botUsername.Trim().TrimStart('@');
        var deepLinkUrl = $"https://t.me/{cleanHandle}?start={Uri.EscapeDataString(code)}";

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
        var membership = await _accountRepo.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            return ServiceResult<TelegramBotStatusResponse>.Forbidden("El usuario no pertenece a una familia activa.");
        }

        var link = await _accountRepo.GetTelegramLinkByUserAsync(identity.UserId, ct);
        var probe = await _transport.ProbeBotAsync(ct);
        var mode = _transport is HttpTelegramBotTransport ? "telegram_http" : "inmemory";

        const string phoneRuleNote =
            "Por políticas de Telegram Bot API no es posible enviar mensajes directo a un número telefónico; el cuidador debe abrir el bot y vincular su Chat ID con /vincular o mediante el enlace profundo /start.";

        return ServiceResult<TelegramBotStatusResponse>.Ok(new TelegramBotStatusResponse(
            probe.TokenConfigured,
            mode,
            probe.Reachable,
            _botUsername,
            probe.ResolvedUsername,
            link?.ChatId.HasValue == true,
            link?.ChatId,
            $"{probe.Detail ?? mode} {phoneRuleNote}"));
    }

    public async Task<ServiceResult<TelegramTestMessageResponse>> SendTestNotificationAsync(
        AuthenticatedIdentity identity,
        string? clientIp,
        CancellationToken ct)
    {
        var membership = await _accountRepo.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            return ServiceResult<TelegramTestMessageResponse>.Forbidden("El usuario no pertenece a una familia activa.");
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

        var mode = _transport is HttpTelegramBotTransport ? "telegram_http" : "inmemory";
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
            mode,
            sent
                ? "Mensaje de prueba entregado al Chat ID vinculado."
                : "No se pudo entregar el mensaje de prueba; revisa la conectividad del bot y tu TELEGRAM_BOT_TOKEN."));
    }

    public async Task<ServiceResult<bool>> UnlinkMyTelegramAsync(
        AuthenticatedIdentity identity,
        string? clientIp,
        CancellationToken ct)
    {
        var membership = await _accountRepo.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            return ServiceResult<bool>.Forbidden("El usuario no pertenece a una familia activa.");
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
        var membership = await _accountRepo.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            return ServiceResult<FeedingReminderRecord>.Forbidden("El usuario no pertenece a una familia activa.");
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
        var membership = await _accountRepo.GetUserMembershipAsync(identity.UserId, ct);
        if (membership is null)
        {
            return ServiceResult<FeedingReminderRecord>.Forbidden("El usuario no pertenece a una familia activa.");
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
    /// Procesa comandos entrantes del bot de Telegram (/start, /vincular CODIGO, /estado).
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
        var commandToken = parts[0];
        var atIdx = commandToken.IndexOf('@');
        if (atIdx > 0)
        {
            commandToken = commandToken[..atIdx];
        }
        var cmd = commandToken.ToLowerInvariant();

        if (cmd == "/start")
        {
            if (parts.Length >= 2)
            {
                await HandleLinkAttemptAsync(chatId, parts[1], now, ct);
                return;
            }

            await _transport.SendMessageAsync(
                chatId,
                $"Bienvenido al bot de Cuna Inteligente. Genera un código temporal (10 min) desde tu panel web y envía: /vincular CODIGO (Chat ID actual: {chatId}).",
                ct);
            return;
        }

        if (cmd == "/vincular")
        {
            if (parts.Length < 2)
            {
                await _transport.SendMessageAsync(
                    chatId,
                    "Formato inválido. Usa: /vincular CODIGO (generado desde el panel web).",
                    ct);
                return;
            }

            await HandleLinkAttemptAsync(chatId, parts[1], now, ct);
            return;
        }

        if (cmd == "/mi_chat")
        {
            var existing = await _accountRepo.GetTelegramLinkByChatIdAsync(chatId, ct);
            var linkedMembership = existing is not null
                ? await _accountRepo.GetUserMembershipAsync(existing.UsuarioId, ct)
                : null;
            var statusLabel = linkedMembership is not null
                ? $"vinculado a {linkedMembership.Email} ({linkedMembership.Rol})"
                : "no vinculado";
            await _transport.SendMessageAsync(
                chatId,
                $"Chat ID: {chatId}\nEstado: {statusLabel}.",
                ct);
            return;
        }

        if (cmd == "/estado")
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

                await _transport.SendMessageAsync(
                    chatId,
                    "Acceso denegado (RF39): este chat no está vinculado a ninguna familia activa.",
                    ct);
                return;
            }

            var cribs = await _deviceRepo.ListCribsByFamilyAsync(membership.FamilyId, ct);
            if (cribs.Count == 0)
            {
                await _transport.SendMessageAsync(
                    chatId,
                    "Tu familia aún no tiene cunas registradas.",
                    ct);
                return;
            }

            var cribSnapshots = await Task.WhenAll(cribs.Select(async c =>
            {
                var telemetryTask = _deviceRepo.ListTelemetryForCribAsync(c.Id, ct);
                var alertsTask = _deviceRepo.ListAlertsForCribAsync(c.Id, ct);
                await Task.WhenAll(telemetryTask, alertsTask);
                return (Crib: c, Telemetry: await telemetryTask, Alerts: await alertsTask);
            }));

            var sb = new StringBuilder();
            sb.AppendLine("Estado actual de Cuna Inteligente (RF39):");
            foreach (var snap in cribSnapshots)
            {
                var c = snap.Crib;
                var latest = CribLiveControlService.ConsolidateLatestTelemetry(snap.Telemetry);
                var tempStr = latest?.TempC is { } t ? $"{t:F1} °C" : "S/D";
                var humStr = latest?.Humedad is { } h ? $"{h:F1} %" : "S/D";
                var massStr = latest?.MasaKg is { } m ? $"{m:F3} kg" : "S/D";
                var bottleSuffix = latest?.TempBiberonC is not null || latest?.MasaBiberonG is not null
                    ? $", Biberón={latest?.TempBiberonC?.ToString("F1") ?? "S/D"} °C / {latest?.MasaBiberonG?.ToString("F0") ?? "S/D"} g"
                    : "";
                var recentAlert = snap.Alerts.LastOrDefault();
                var alertSuffix = recentAlert is not null
                    ? $", Última alerta={recentAlert.Mensaje ?? recentAlert.Tipo}"
                    : "";

                sb.AppendLine(
                    $"- {c.Nombre}: conexión={c.EstadoConexion}, modo={c.ModoOperacion}, T={tempStr}, H={humStr}, Masa={massStr}{bottleSuffix}{alertSuffix}");
            }

            await _transport.SendMessageAsync(chatId, sb.ToString().TrimEnd(), ct);
            return;
        }

        if (cmd is "/ayuda" or "/help")
        {
            await _transport.SendMessageAsync(
                chatId,
                "Comandos disponibles de Cuna Inteligente:\n• /vincular CODIGO — Vincula tu chat de Telegram con tu cuenta familiar (código temporal de 10 min).\n• /estado — Consulta el estado en vivo de las cunas de tu familia (< 2 s).\n• /mi_chat — Muestra tu Chat ID y estado de vinculación.\n• /ayuda — Muestra esta información de ayuda.",
                ct);
            return;
        }

        await _transport.SendMessageAsync(
            chatId,
            "Comando no reconocido. Comandos disponibles: /vincular CODIGO, /estado, /mi_chat, /ayuda",
            ct);
    }

    private async Task HandleLinkAttemptAsync(
        long chatId,
        string candidateCode,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (_linkAttemptsByChat.TryGetValue(chatId, out var existingState) &&
            existingState.LockedUntil is { } lockedUntil &&
            now < lockedUntil)
        {
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
            return;
        }

        var rawCode = candidateCode.Trim();
        if (rawCode.Length < 6)
        {
            await _transport.SendMessageAsync(
                chatId,
                "Formato inválido. Usa: /vincular CODIGO (generado desde el panel web).",
                ct);
            return;
        }

        var hash = CryptoTokenHelper.ComputeSha256Hex(rawCode);
        var linked = await _accountRepo.CompleteTelegramLinkByCodeAsync(hash, chatId, now, ct);

        if (linked is null)
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
                    : "Código de vinculación inválido, expirado o bloqueado tras superar el límite de intentos.",
                ct);
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

        await _transport.SendMessageAsync(
            chatId,
            "Chat de Telegram vinculado correctamente a tu cuenta familiar (RF38). Usa /estado para consultar la cuna.",
            ct);
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

        var prefix = alert.Severidad == "critica" ? "[ALERTA CRÍTICA]" : "[AVISO]";
        var text = $"{prefix} {crib.Nombre}: {alert.Mensaje ?? alert.Tipo}";

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

            await _accountRepo.UpdateFeedingReminderAsync(
                rem.FamilyId,
                rem.IntervaloHoras,
                rem.Activo,
                now,
                ct);

            var cribs = await _deviceRepo.ListCribsByFamilyAsync(rem.FamilyId, ct);
            var msg = $"Recordatorio de alimentación (RF18): han transcurrido {rem.IntervaloHoras} h desde la última toma.";

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
                await _transport.SendMessageAsync(chatId, msg, ct);
            }

            sentFamilies++;
        }

        return sentFamilies;
    }
}

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
            var probe = await _transport.ProbeBotAsync(stoppingToken);
            if (probe.Reachable && !string.IsNullOrWhiteSpace(probe.ResolvedUsername))
            {
                _logger.LogInformation("Sondeo Telegram conectado como @{Username}.", probe.ResolvedUsername);
            }
            else if (!probe.Reachable)
            {
                _logger.LogWarning("Sondeo Telegram no pudo verificar getMe: {Detail}", probe.Detail);
            }
        }

        var pollDelay = isHttp ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested)
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
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error en ciclo de sondeo de Telegram / recordatorios RF18.");
            }

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
}
