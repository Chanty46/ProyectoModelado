using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>
/// Abstracción del transporte de mensajería hacia Telegram Bot API o su doble local en memoria.
/// </summary>
public interface ITelegramBotTransport
{
    Task<IReadOnlyList<TelegramInboundMessage>> GetUpdatesAsync(long offset, CancellationToken ct);
    Task<bool> SendMessageAsync(long chatId, string text, CancellationToken ct);
    Task<TelegramBotProbeResult> ProbeBotAsync(CancellationToken ct)
        => Task.FromResult(new TelegramBotProbeResult(false, true, null, null, "inmemory"));
}

/// <summary>
/// Transporte local en memoria para pruebas automatizadas y ejecución sin token real de Bot API.
/// </summary>
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

/// <summary>
/// Cliente HTTP hacia la Bot API oficial de Telegram mediante sondeo (<c>getUpdates</c>)
/// con manejo explícito de rate-limit HTTP 429 y limpieza inicial de webhook.
/// </summary>
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
                var err = await TryReadErrorEnvelopeAsync(res, ct);
                _logger.LogWarning(
                    "Telegram getUpdates respondió HTTP {StatusCode} ({Detail}). Verifica TELEGRAM_BOT_TOKEN o conflictos con otro proceso.",
                    (int)res.StatusCode,
                    err?.Description ?? "sin detalle");
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

    /// <summary>
    /// Envía un mensaje de texto truncado a 4000 caracteres y gestiona respuestas HTTP 429
    /// respetando <c>parameters.retry_after</c> (acotado a 1–5 s) con un único reintento para
    /// no bloquear indefinidamente el despacho de alertas críticas.
    /// </summary>
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
                return await RetryAfterRateLimitAsync(url, payload, chatId, res, ct);
            }

            var err = await TryReadErrorEnvelopeAsync(res, ct);
            _logger.LogWarning(
                "Telegram sendMessage rechazado con HTTP {StatusCode} para chat_id={ChatId} ({Detail}).",
                (int)res.StatusCode,
                chatId,
                err?.Description ?? "sin detalle");
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
                var err = await TryReadErrorEnvelopeAsync(res, ct);
                _logger.LogWarning(
                    "Telegram deleteWebhook respondió HTTP {StatusCode} ({Detail}).",
                    (int)res.StatusCode,
                    err?.Description ?? "sin detalle");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Interlocked.Exchange(ref _webhookCleared, 0);
            _logger.LogWarning("No se pudo ejecutar deleteWebhook en Telegram ({ErrorType}).", ex.GetType().Name);
        }
    }

    private async Task<bool> RetryAfterRateLimitAsync(
        string url,
        object payload,
        long chatId,
        HttpResponseMessage rateLimitResponse,
        CancellationToken ct)
    {
        var err = await TryReadErrorEnvelopeAsync(rateLimitResponse, ct);
        var retryAfterSeconds = err?.Parameters?.RetryAfter ?? 1;
        // Acotar espera entre 1 y 5 s para cumplir backoff de Telegram sin congelar alertas ni sondeo
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

        var retryErr = await TryReadErrorEnvelopeAsync(retryRes, ct);
        _logger.LogWarning(
            "Reintento de Telegram sendMessage falló con HTTP {StatusCode} para chat_id={ChatId} ({Detail}).",
            (int)retryRes.StatusCode,
            chatId,
            retryErr?.Description ?? "sin detalle");
        return false;
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

    private static async Task<TelegramErrorEnvelope?> TryReadErrorEnvelopeAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<TelegramErrorEnvelope>(JsonOptions, ct);
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
