using CunaServer.Data;
using CunaServer.Services;

namespace CunaServer.Api;

public static class PairingAndCribEndpoints
{
    public static IEndpointRouteBuilder MapPairingAndCribEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api");

        group.MapPost("/pairing/window", (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.OpenPairingWindowAsync(identity, ip, ct),
                ct));

        group.MapPost("/pairing/claim", (
            HttpContext http,
            ClaimDeviceRequest request,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.ClaimDeviceAsync(identity, request, ip, ct),
                ct));

        group.MapGet("/cribs", (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, _) => service.ListCribsAsync(identity, ct),
                ct));

        group.MapPatch("/cribs/{cunaId:guid}", (
            Guid cunaId,
            HttpContext http,
            RenameCribRequest request,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.RenameCribAsync(identity, cunaId, request, ip, ct),
                ct));

        group.MapPost("/cribs/{cunaId:guid}/unlink", (
            Guid cunaId,
            HttpContext http,
            UnlinkCribRequest request,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.UnlinkCribAsync(identity, cunaId, request, ip, ct),
                ct));

        group.MapGet("/babies", (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, _) => service.ListBabiesAsync(identity, ct),
                ct));

        group.MapPost("/babies", (
            HttpContext http,
            UpsertBabyRequest request,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.CreateBabyAsync(identity, request, ip, ct),
                ct));

        group.MapPatch("/babies/{bebeId:guid}", (
            Guid bebeId,
            HttpContext http,
            UpsertBabyRequest request,
            IJwtTokenValidator jwtValidator,
            PairingAndCribService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.UpdateBabyAsync(identity, bebeId, request, ip, ct),
                ct));

        return endpoints;
    }
}
