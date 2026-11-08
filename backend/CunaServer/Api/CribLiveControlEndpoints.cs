using CunaServer.Data;
using CunaServer.Services;

namespace CunaServer.Api;

public static class CribLiveControlEndpoints
{
    public static IEndpointRouteBuilder MapCribLiveControlEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/cribs/{cunaId:guid}");

        group.MapGet("/live", async (
            Guid cunaId,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            CribLiveControlService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.GetLiveCribStatusAsync(identity, cunaId, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapPost("/mode", async (
            Guid cunaId,
            HttpContext http,
            SetCribModeRequest request,
            IJwtTokenValidator jwtValidator,
            CribLiveControlService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.SetCribModeAsync(identity, cunaId, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapPost("/light", async (
            Guid cunaId,
            HttpContext http,
            SetCribLightRequest request,
            IJwtTokenValidator jwtValidator,
            CribLiveControlService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.SetCribLightAsync(identity, cunaId, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapGet("/thresholds", async (
            Guid cunaId,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            CribLiveControlService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.GetCribThresholdsAsync(identity, cunaId, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapPut("/thresholds", async (
            Guid cunaId,
            HttpContext http,
            UpdateCribThresholdsRequest request,
            IJwtTokenValidator jwtValidator,
            CribLiveControlService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.UpdateCribThresholdsAsync(identity, cunaId, request, GetClientIp(http), ct);
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
