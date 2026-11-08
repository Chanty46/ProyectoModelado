using CunaServer.Services;

namespace CunaServer.Api;

public static class IntercomEndpoints
{
    public static IEndpointRouteBuilder MapIntercomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/cribs");

        group.MapPost("/{cunaId:guid}/intercom/chunk", async (
            Guid cunaId,
            HttpContext http,
            IntercomChunkRequest request,
            IJwtTokenValidator jwtValidator,
            IntercomAudioService service,
            CancellationToken ct) =>
        {
            var authHeader = http.Request.Headers.Authorization.ToString();
            var identity = await jwtValidator.ValidateBearerTokenAsync(authHeader, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var clientIp = http.Connection.RemoteIpAddress?.ToString();
            var result = await service.SendAudioChunkAsync(identity, cunaId, request, clientIp, ct);

            return result.Status switch
            {
                ServiceStatus.Ok => Results.Ok(result.Data),
                ServiceStatus.BadRequest => Results.BadRequest(new { error = result.ErrorMessage }),
                ServiceStatus.Forbidden => Results.Json(new { error = result.ErrorMessage }, statusCode: StatusCodes.Status403Forbidden),
                ServiceStatus.NotFound => Results.NotFound(new { error = result.ErrorMessage }),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        });

        return endpoints;
    }
}
