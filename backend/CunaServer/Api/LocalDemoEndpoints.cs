using CunaServer.Data;
using CunaServer.Services;

namespace CunaServer.Api;

public static class LocalDemoEndpoints
{
    public static IEndpointRouteBuilder MapLocalDemoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/mode", (LocalDemoAuthService localAuth) =>
            Results.Ok(localAuth.GetAuthMode()));

        endpoints.MapPost("/api/auth/local-login", async (
            HttpContext http,
            LocalLoginRequest? request,
            LocalDemoAuthService localAuth,
            CancellationToken ct) =>
        {
            var result = await localAuth.LoginAsync(request, EndpointHelpers.GetClientIp(http), ct);
            return result.Status == ServiceStatus.Unauthorized
                ? Results.Json(new { error = result.ErrorMessage }, statusCode: StatusCodes.Status401Unauthorized)
                : EndpointHelpers.ToHttpResult(result);
        });

        endpoints.MapPost("/api/demo/cribs/{cunaId:guid}/simulate", (
            Guid cunaId,
            HttpContext http,
            DemoSimulateRequest? request,
            IJwtTokenValidator jwtValidator,
            LocalDemoAuthService localAuth,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => localAuth.SimulateCribEventAsync(identity, cunaId, request, ip, ct),
                ct));

        return endpoints;
    }
}
