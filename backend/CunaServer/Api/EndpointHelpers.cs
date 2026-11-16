using CunaServer.Data;
using CunaServer.Services;

namespace CunaServer.Api;

internal static class EndpointHelpers
{
    private static readonly IResult UnauthorizedResult =
        Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);

    public static Task<AuthenticatedIdentity?> AuthenticateAsync(
        HttpContext http,
        IJwtTokenValidator jwtValidator,
        CancellationToken ct)
    {
        var authHeader = http.Request.Headers.Authorization.ToString();
        return jwtValidator.ValidateBearerTokenAsync(authHeader, ct);
    }

    public static string? GetClientIp(HttpContext http)
        => http.Connection.RemoteIpAddress?.ToString();

    public static async Task<IResult> ExecuteAuthenticatedAsync(
        HttpContext http,
        IJwtTokenValidator jwtValidator,
        Func<AuthenticatedIdentity, string?, Task<IResult>> action,
        CancellationToken ct)
    {
        var identity = await AuthenticateAsync(http, jwtValidator, ct);
        if (identity is null)
        {
            return UnauthorizedResult;
        }

        return await action(identity, GetClientIp(http));
    }

    public static Task<IResult> ExecuteServiceAsync<T>(
        HttpContext http,
        IJwtTokenValidator jwtValidator,
        Func<AuthenticatedIdentity, string?, Task<ServiceResult<T>>> action,
        CancellationToken ct)
        => ExecuteAuthenticatedAsync(
            http,
            jwtValidator,
            async (identity, clientIp) => ToHttpResult(await action(identity, clientIp)),
            ct);

    public static IResult ToHttpResult<T>(ServiceResult<T> result)
        => result.Status switch
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
