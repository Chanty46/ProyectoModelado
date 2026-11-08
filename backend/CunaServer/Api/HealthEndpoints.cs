namespace CunaServer.Api;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", (IConfiguration config) => Results.Ok(new
        {
            status = "ok",
            service = "CunaServer",
            tcpPort = int.TryParse(config["TCP_PORT"], out var p) ? p : 9000,
            timestamp = DateTimeOffset.UtcNow
        }));

        return endpoints;
    }
}
