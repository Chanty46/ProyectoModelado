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

public interface ITelegramBotTransport
{
    Task<IReadOnlyList<TelegramInboundMessage>> GetUpdatesAsync(long offset, CancellationToken ct);
    Task<bool> SendMessageAsync(long chatId, string text, CancellationToken ct);
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
    private readonly ILogger<HttpTelegramBotTransport> _logger;

    public HttpTelegramBotTransport(HttpClient http, string botToken, ILogger<HttpTelegramBotTransport> logger)
    {
        _http = http;
        _botToken = botToken.Trim();
        _logger = logger;
    }

    public async Task<IReadOnlyList<TelegramInboundMessage>> GetUpdatesAsync(long offset, CancellationToken ct)
    {
        try
        {
            var url = $"https://api.telegram.org/bot{_botToken}/getUpdates?offset={offset}&timeout=2";
            var resp = await _http.GetFromJsonAsync<TelegramUpdatesEnvelope>(url, JsonOptions, ct);
            if (resp?.Ok != true || resp.Result is null)
            {
                return Array.Empty<TelegramInboundMessage>();
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
            _logger.LogDebug("Fallo temporal consultando getUpdates de Telegram ({ErrorType}).", ex.GetType().Name);
            return Array.Empty<TelegramInboundMessage>();
        }
    }

    public async Task<bool> SendMessageAsync(long chatId, string text, CancellationToken ct)
    {
        try
        {
            var sanitizedText = text.Length > 4000 ? text[..4000] + "..." : text;
            var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";
            using var res = await _http.PostAsJsonAsync(url, new
            {
                chat_id = chatId,
                text = sanitizedText
            }, JsonOptions, ct);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug("Fallo enviando mensaje a Telegram (chat_id={ChatId}, {ErrorType}).", chatId, ex.GetType().Name);
            return false;
        }
    }

    private sealed record TelegramUpdatesEnvelope(bool Ok, List<TelegramUpdateItem>? Result);
    private sealed record TelegramUpdateItem(long UpdateId, TelegramMessageItem? Message);
    private sealed record TelegramMessageItem(TelegramChatItem? Chat, string? Text);
    private sealed record TelegramChatItem(long Id);
}

public sealed class TelegramBotService
{
    public const int LinkCodeExpirationMinutes = 10;

    private readonly IFamilyAccountRepository _accountRepo;
    private readonly IDeviceTelemetryRepository _deviceRepo;
    private readonly ITelegramBotTransport _transport;
    private readonly string _botUsername;

    public TelegramBotService(
        IFamilyAccountRepository accountRepo,
        IDeviceTelemetryRepository deviceRepo,
        ITelegramBotTransport transport,
        IConfiguration? config = null)
    {
        _accountRepo = accountRepo;
        _deviceRepo = deviceRepo;
        _transport = transport;
        _botUsername = config?["TELEGRAM_BOT_USERNAME"] ?? "@CunasInteligentes_Modelado_Bot";
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

        return ServiceResult<GenerateTelegramCodeResponse>.Created(new GenerateTelegramCodeResponse(
            code,
            expiresAt,
            _botUsername,
            $"/vincular {code}"));
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

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            await _transport.SendMessageAsync(
                chatId,
                "Bienvenido al bot de Cuna Inteligente. Genera un código temporal (10 min) desde tu panel web y envía: /vincular CODIGO",
                ct);
            return;
        }

        if (text.StartsWith("/vincular", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[1].Length < 6)
            {
                await _transport.SendMessageAsync(
                    chatId,
                    "Formato inválido. Usa: /vincular CODIGO (generado desde el panel web).",
                    ct);
                return;
            }

            var rawCode = parts[1].Trim();
            var hash = CryptoTokenHelper.ComputeSha256Hex(rawCode);
            var linked = await _accountRepo.CompleteTelegramLinkByCodeAsync(hash, chatId, now, ct);

            if (linked is null)
            {
                await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                    null,
                    null,
                    null,
                    "telegram_vinculo_fallido",
                    $"chat:{chatId}",
                    "codigo_invalido_expirado_o_usado",
                    now), ct);

                await _transport.SendMessageAsync(
                    chatId,
                    "Código de vinculación inválido, expirado o bloqueado tras superar el límite de intentos.",
                    ct);
                return;
            }

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
            return;
        }

        if (text.StartsWith("/estado", StringComparison.OrdinalIgnoreCase))
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

            var sb = new StringBuilder();
            sb.AppendLine("Estado actual de Cuna Inteligente (RF39):");
            foreach (var c in cribs)
            {
                var telemetry = await _deviceRepo.ListTelemetryForCribAsync(c.Id, ct);
                var latest = telemetry.LastOrDefault();
                var tempStr = latest?.TempC is { } t ? $"{t:F1} °C" : "S/D";
                var humStr = latest?.Humedad is { } h ? $"{h:F1} %" : "S/D";
                var massStr = latest?.MasaKg is { } m ? $"{m:F3} kg" : "S/D";

                sb.AppendLine(
                    $"- {c.Nombre}: conexión={c.EstadoConexion}, modo={c.ModoOperacion}, T={tempStr}, H={humStr}, Masa={massStr}");
            }

            await _transport.SendMessageAsync(chatId, sb.ToString().TrimEnd(), ct);
            return;
        }

        if (text.StartsWith("/ayuda", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
        {
            await _transport.SendMessageAsync(
                chatId,
                "Comandos disponibles de Cuna Inteligente:\n• /vincular CODIGO — Vincula tu chat de Telegram con tu cuenta familiar (código temporal de 10 min generado desde la web).\n• /estado — Consulta el estado en vivo de las cunas de tu familia (< 2 s).\n• /ayuda — Muestra esta información de ayuda.",
                ct);
            return;
        }

        await _transport.SendMessageAsync(
            chatId,
            "Comando no reconocido. Comandos disponibles: /vincular CODIGO, /estado, /ayuda",
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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var updates = await _transport.GetUpdatesAsync(_nextOffset, stoppingToken);
                foreach (var upd in updates)
                {
                    _nextOffset = Math.Max(_nextOffset, upd.UpdateId + 1);
                    await _botService.ProcessInboundCommandAsync(upd.ChatId, upd.Text, DateTimeOffset.UtcNow, stoppingToken);
                }

                await _botService.EvaluateFeedingRemindersAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error en ciclo de sondeo de Telegram / recordatorios RF18.");
            }
        }
    }
}
