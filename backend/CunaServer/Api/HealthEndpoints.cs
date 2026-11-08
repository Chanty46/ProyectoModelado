namespace CunaServer.Api;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            service = "CunaServer",
            timestamp = DateTimeOffset.UtcNow
        }));

        return endpoints;
    }
}
