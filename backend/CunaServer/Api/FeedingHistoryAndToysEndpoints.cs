using CunaServer.Data;
using CunaServer.Services;

namespace CunaServer.Api;

public static class FeedingHistoryAndToysEndpoints
{
    public static IEndpointRouteBuilder MapFeedingHistoryAndToysEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost("/feeding/sessions/start", async (
            HttpContext http,
            StartFeedingSessionRequest request,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.StartFeedingSessionAsync(identity, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        api.MapPost("/feeding/sessions/{sessionId:guid}/finish", async (
            Guid sessionId,
            HttpContext http,
            FinishFeedingSessionRequest request,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.FinishFeedingSessionAsync(identity, sessionId, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        api.MapGet("/babies/{bebeId:guid}/history/feeding", async (
            Guid bebeId,
            DateOnly? desde,
            DateOnly? hasta,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.GetFeedingHistoryAsync(identity, bebeId, desde, hasta, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        api.MapPost("/babies/{bebeId:guid}/history/sleep", async (
            Guid bebeId,
            HttpContext http,
            CreateSleepSessionRequest request,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.CreateSleepSessionAsync(identity, bebeId, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        api.MapGet("/babies/{bebeId:guid}/history/sleep", async (
            Guid bebeId,
            DateOnly? desde,
            DateOnly? hasta,
            string? tipo,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.GetSleepHistoryAsync(identity, bebeId, desde, hasta, tipo, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        api.MapPost("/babies/{bebeId:guid}/history/export", async (
            Guid bebeId,
            HttpContext http,
            ExportHistoryRequest request,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.ExportBabyHistoryAsync(identity, bebeId, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        api.MapGet("/cribs/{cunaId:guid}/songs", async (
            Guid cunaId,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.ListSongsAsync(identity, cunaId, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        api.MapPost("/cribs/{cunaId:guid}/songs/play", async (
            Guid cunaId,
            HttpContext http,
            PlaySongRequest request,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.PlaySongAsync(identity, cunaId, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        api.MapPost("/cribs/{cunaId:guid}/mobile/play", async (
            Guid cunaId,
            HttpContext http,
            PlayMobileRequest request,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.PlayMobileAsync(identity, cunaId, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        api.MapGet("/cribs/{cunaId:guid}/toys", async (
            Guid cunaId,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.GetToysAndPeripheralsStatusAsync(identity, cunaId, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        return endpoints;
    }

    private static Task<AuthenticatedIdentity?> AuthenticateAsync(
        HttpContext http,
        IJwtTokenValidator jwtValidator,
        CancellationToken ct)
    {
        var authHeader = http.Request.Headers.Authorization.ToString();
        return jwtValidator.ValidateBearerTokenAsync(authHeader, ct);
    }

    private static string? GetClientIp(HttpContext http)
        => http.Connection.RemoteIpAddress?.ToString();

    private static IResult ToHttpResult<T>(ServiceResult<T> result)
    {
        return result.Status switch
        {
            ServiceStatus.Ok => Results.Ok(result.Data),
            ServiceStatus.Created => Results.Json(result.Data, statusCode: StatusCodes.Status201Created),
            ServiceStatus.BadRequest => Results.BadRequest(new { error = result.ErrorMessage }),
            ServiceStatus.Forbidden => Results.Json(new { error = result.ErrorMessage }, statusCode: StatusCodes.Status403Forbidden),
            ServiceStatus.NotFound => Results.NotFound(new { error = result.ErrorMessage }),
            ServiceStatus.Conflict => Results.Conflict(new { error = result.ErrorMessage }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
