namespace CunaServer.Api;

using CunaServer.Tcp;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", (IConfiguration config, DeviceTcpServer? tcpServer) => Results.Ok(new
        {
            status = "ok",
            service = "CunaServer",
            tcpPort = int.TryParse(config["TCP_PORT"], out var p) ? p : 9000,
            connectedDevices = tcpServer?.ConnectedDeviceCount ?? 0,
            timestamp = DateTimeOffset.UtcNow
        }));

        return endpoints;
    }
}
