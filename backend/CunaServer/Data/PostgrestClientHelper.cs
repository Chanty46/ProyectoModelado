using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CunaServer.Data;

/// <summary>
/// Helper compartido para configurar y ejecutar operaciones HTTP contra PostgREST (Supabase)
/// utilizando la clave secreta de backend y serialización en <c>snake_case</c>.
/// </summary>
internal sealed class PostgrestClientHelper
{
    private readonly HttpClient _http;

    /// <summary>
    /// Opciones JSON compartidas para mapear registros y payloads en formato <c>snake_case</c>.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static readonly JsonSerializerOptions PatchIncludeNullsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Determina si una clave de Supabase es un JWT clásico (<c>eyJ...</c>) que requiere
    /// cabecera <c>Authorization: Bearer</c>. Las claves modernas (<c>sb_secret_...</c> /
    /// <c>sb_publishable_...</c>) son opacas y solo deben enviarse en <c>apikey</c>.
    /// </summary>
    internal static bool IsJwtApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey) ||
            apiKey.StartsWith("sb_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var dotCount = 0;
        foreach (var ch in apiKey)
        {
            if (ch == '.')
            {
                dotCount++;
            }
        }

        return dotCount == 2;
    }

    /// <summary>
    /// Inicializa el cliente HTTP con la URL base <c>/rest/v1/</c> y los encabezados
    /// de autenticación requeridos por Supabase usando exclusivamente <c>SUPABASE_SECRET_KEY</c>.
    /// </summary>
    public PostgrestClientHelper(HttpClient httpClient, IConfiguration configuration)
    {
        _http = httpClient;
        var supabaseUrl = (configuration["SUPABASE_URL"] ?? "").TrimEnd('/');
        var secretKey = (configuration["SUPABASE_SECRET_KEY"] ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(supabaseUrl))
        {
            _http.BaseAddress = new Uri($"{supabaseUrl}/rest/v1/");
        }

        if (!string.IsNullOrWhiteSpace(secretKey))
        {
            _http.DefaultRequestHeaders.Remove("apikey");
            _http.DefaultRequestHeaders.Add("apikey", secretKey);
            _http.DefaultRequestHeaders.Authorization = IsJwtApiKey(secretKey)
                ? new AuthenticationHeaderValue("Bearer", secretKey)
                : null;
        }
    }

    /// <summary>
    /// Ejecuta una consulta GET y devuelve la lista deserializada (vacía si la respuesta es nula).
    /// </summary>
    public async Task<List<T>> GetListAsync<T>(string relativePathAndQuery, CancellationToken ct)
    {
        return await _http.GetFromJsonAsync<List<T>>(relativePathAndQuery, JsonOptions, ct) ?? [];
    }

    /// <summary>
    /// Ejecuta una consulta GET y devuelve el primer elemento o <c>null</c> si no hay coincidencias.
    /// </summary>
    public async Task<T?> GetFirstOrDefaultAsync<T>(string relativePathAndQuery, CancellationToken ct)
    {
        var rows = await _http.GetFromJsonAsync<List<T>>(relativePathAndQuery, JsonOptions, ct);
        return rows is null ? default : rows.FirstOrDefault();
    }

    /// <summary>
    /// Envía un POST sin requerir representación de retorno, validando el código HTTP de éxito.
    /// </summary>
    public async Task PostAsync<TPayload>(
        string relativePath,
        TPayload payload,
        CancellationToken ct,
        string? preferHeader = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, relativePath);
        if (!string.IsNullOrWhiteSpace(preferHeader))
        {
            req.Headers.Add("Prefer", preferHeader);
        }

        req.Content = JsonContent.Create(payload, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Envía un POST con <c>Prefer: return=representation</c> y devuelve la única fila creada.
    /// </summary>
    public async Task<TResult> PostAndReadSingleAsync<TPayload, TResult>(
        string relativePath,
        TPayload payload,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, relativePath);
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(payload, options: JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = await res.Content.ReadFromJsonAsync<List<TResult>>(JsonOptions, ct);
        return rows!.Single();
    }

    /// <summary>
    /// Envía un PATCH y opcionalmente valida éxito HTTP (útil en limpiezas previas tolerantes).
    /// </summary>
    public async Task PatchAsync<TPayload>(
        string relativePathAndQuery,
        TPayload payload,
        CancellationToken ct,
        bool ensureSuccess = true,
        bool includeNulls = false)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, relativePathAndQuery);
        req.Content = JsonContent.Create(payload, options: includeNulls ? PatchIncludeNullsJsonOptions : JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        if (ensureSuccess)
        {
            res.EnsureSuccessStatusCode();
        }
    }

    /// <summary>
    /// Envía un PATCH con <c>Prefer: return=representation</c> y devuelve la lista actualizada.
    /// </summary>
    public async Task<List<TResult>> PatchAndReadListAsync<TPayload, TResult>(
        string relativePathAndQuery,
        TPayload payload,
        CancellationToken ct,
        bool includeNulls = false)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, relativePathAndQuery);
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(payload, options: includeNulls ? PatchIncludeNullsJsonOptions : JsonOptions);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<List<TResult>>(JsonOptions, ct) ?? [];
    }

    /// <summary>
    /// Envía un PATCH con <c>Prefer: return=representation</c> y devuelve la primera fila actualizada.
    /// </summary>
    public async Task<TResult?> PatchAndReadFirstAsync<TPayload, TResult>(
        string relativePathAndQuery,
        TPayload payload,
        CancellationToken ct,
        bool includeNulls = false)
    {
        var rows = await PatchAndReadListAsync<TPayload, TResult>(relativePathAndQuery, payload, ct, includeNulls);
        return rows.FirstOrDefault();
    }

    /// <summary>
    /// Envía un PATCH con <c>Prefer: return=representation</c> y devuelve la única fila esperada.
    /// </summary>
    public async Task<TResult> PatchAndReadSingleAsync<TPayload, TResult>(
        string relativePathAndQuery,
        TPayload payload,
        CancellationToken ct,
        bool includeNulls = false)
    {
        var rows = await PatchAndReadListAsync<TPayload, TResult>(relativePathAndQuery, payload, ct, includeNulls);
        return rows.Single();
    }

    /// <summary>
    /// Envía un DELETE y devuelve si el servidor respondió con un código HTTP exitoso.
    /// </summary>
    public async Task<bool> DeleteAsync(
        string relativePathAndQuery,
        CancellationToken ct,
        bool ensureSuccess = false)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, relativePathAndQuery);
        using var res = await _http.SendAsync(req, ct);
        if (ensureSuccess)
        {
            res.EnsureSuccessStatusCode();
        }

        return res.IsSuccessStatusCode;
    }

    /// <summary>
    /// Envía un DELETE con <c>Prefer: return=representation</c> y devuelve las filas eliminadas.
    /// </summary>
    public async Task<List<TResult>> DeleteAndReadListAsync<TResult>(
        string relativePathAndQuery,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, relativePathAndQuery);
        req.Headers.Add("Prefer", "return=representation");
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<List<TResult>>(JsonOptions, ct) ?? [];
    }

    /// <summary>
    /// Registra un evento en <c>bitacora_seguridad</c> encapsulando el detalle como objeto JSON <c>{"info": ...}</c>.
    /// </summary>
    public Task LogSecurityEventAsync(SecurityAuditEntry entry, CancellationToken ct)
    {
        return PostAsync(
            "bitacora_seguridad",
            new
            {
                family_id = entry.FamilyId,
                usuario_id = entry.UsuarioId,
                dispositivo_id = entry.DispositivoId,
                evento = entry.Evento,
                ip_origen = entry.IpOrigen,
                detalle = entry.Detalle is null ? null : new { info = entry.Detalle },
                ts = entry.Ts
            },
            ct);
    }
}
