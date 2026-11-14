using CunaServer.Services;

namespace CunaServer.Api;

public static class IntercomEndpoints
{
    public static IEndpointRouteBuilder MapIntercomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/cribs");

        group.MapPost("/{cunaId:guid}/intercom/chunk", (
            Guid cunaId,
            HttpContext http,
            IntercomChunkRequest request,
            IJwtTokenValidator jwtValidator,
            IntercomAudioService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.SendAudioChunkAsync(identity, cunaId, request, ip, ct),
                ct));

        return endpoints;
    }
}
