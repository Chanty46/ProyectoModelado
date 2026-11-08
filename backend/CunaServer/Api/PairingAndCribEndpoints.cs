using CunaServer.Data;
using CunaServer.Services;

namespace CunaServer.Api;

public static class PairingAndCribEndpoints
{
    public static IEndpointRouteBuilder MapPairingAndCribEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api");

        group.MapPost("/pairing/window", async (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.OpenPairingWindowAsync(identity, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapPost("/pairing/claim", async (
            HttpContext http,
            ClaimDeviceRequest request,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.ClaimDeviceAsync(identity, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapGet("/cribs", async (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.ListCribsAsync(identity, ct);
            return ToHttpResult(result);
        });

        group.MapPatch("/cribs/{cunaId:guid}", async (
            Guid cunaId,
            HttpContext http,
            RenameCribRequest request,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.RenameCribAsync(identity, cunaId, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapPost("/cribs/{cunaId:guid}/unlink", async (
            Guid cunaId,
            HttpContext http,
            UnlinkCribRequest request,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.UnlinkCribAsync(identity, cunaId, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapGet("/babies", async (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.ListBabiesAsync(identity, ct);
            return ToHttpResult(result);
        });

        group.MapPost("/babies", async (
            HttpContext http,
            UpsertBabyRequest request,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.CreateBabyAsync(identity, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapPatch("/babies/{bebeId:guid}", async (
            Guid bebeId,
            HttpContext http,
            UpsertBabyRequest request,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.UpdateBabyAsync(identity, bebeId, request, GetClientIp(http), ct);
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
