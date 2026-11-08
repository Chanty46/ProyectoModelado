using CunaServer.Data;
using CunaServer.Services;
using CunaServer.Telegram;

namespace CunaServer.Api;

public static class TelegramAndRemindersEndpoints
{
    public static IEndpointRouteBuilder MapTelegramAndRemindersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api");

        group.MapGet("/telegram/link", async (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            TelegramBotService telegramService,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await telegramService.GetLinkStatusAsync(identity, ct);
            return ToHttpResult(result);
        });

        group.MapPost("/telegram/link-code", async (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            TelegramBotService telegramService,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await telegramService.GenerateLinkCodeAsync(identity, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapDelete("/telegram/link", async (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            TelegramBotService telegramService,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await telegramService.UnlinkMyTelegramAsync(identity, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapGet("/reminders", async (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            TelegramBotService telegramService,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await telegramService.GetFeedingReminderAsync(identity, ct);
            return ToHttpResult(result);
        });

        group.MapPut("/reminders", async (
            HttpContext http,
            UpdateFeedingReminderRequest request,
            IJwtTokenValidator jwtValidator,
            TelegramBotService telegramService,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await telegramService.UpdateFeedingReminderAsync(identity, request, GetClientIp(http), ct);
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
