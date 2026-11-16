using CunaServer.Data;
using CunaServer.Services;

namespace CunaServer.Api;

public static class CribLiveControlEndpoints
{
    public static IEndpointRouteBuilder MapCribLiveControlEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/cribs/{cunaId:guid}");

        group.MapGet("/live", (
            Guid cunaId,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            CribLiveControlService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.GetLiveCribStatusAsync(identity, cunaId, ip, ct),
                ct));

        group.MapPost("/mode", (
            Guid cunaId,
            HttpContext http,
            SetCribModeRequest request,
            IJwtTokenValidator jwtValidator,
            CribLiveControlService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.SetCribModeAsync(identity, cunaId, request, ip, ct),
                ct));

        group.MapPost("/light", (
            Guid cunaId,
            HttpContext http,
            SetCribLightRequest request,
            IJwtTokenValidator jwtValidator,
            CribLiveControlService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.SetCribLightAsync(identity, cunaId, request, ip, ct),
                ct));

        group.MapGet("/thresholds", (
            Guid cunaId,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            CribLiveControlService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.GetCribThresholdsAsync(identity, cunaId, ip, ct),
                ct));

        group.MapPut("/thresholds", (
            Guid cunaId,
            HttpContext http,
            UpdateCribThresholdsRequest request,
            IJwtTokenValidator jwtValidator,
            CribLiveControlService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.UpdateCribThresholdsAsync(identity, cunaId, request, ip, ct),
                ct));

        return endpoints;
    }
}
