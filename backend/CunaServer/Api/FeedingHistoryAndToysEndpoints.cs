using CunaServer.Data;
using CunaServer.Services;

namespace CunaServer.Api;

public static class FeedingHistoryAndToysEndpoints
{
    public static IEndpointRouteBuilder MapFeedingHistoryAndToysEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost("/feeding/sessions/start", (
            HttpContext http,
            StartFeedingSessionRequest request,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.StartFeedingSessionAsync(identity, request, ip, ct),
                ct));

        api.MapPost("/feeding/sessions/{sessionId:guid}/finish", (
            Guid sessionId,
            HttpContext http,
            FinishFeedingSessionRequest request,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.FinishFeedingSessionAsync(identity, sessionId, request, ip, ct),
                ct));

        api.MapGet("/babies/{bebeId:guid}/history/feeding", (
            Guid bebeId,
            DateOnly? desde,
            DateOnly? hasta,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.GetFeedingHistoryAsync(identity, bebeId, desde, hasta, ip, ct),
                ct));

        api.MapPost("/babies/{bebeId:guid}/history/sleep", (
            Guid bebeId,
            HttpContext http,
            CreateSleepSessionRequest request,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.CreateSleepSessionAsync(identity, bebeId, request, ip, ct),
                ct));

        api.MapGet("/babies/{bebeId:guid}/history/sleep", (
            Guid bebeId,
            DateOnly? desde,
            DateOnly? hasta,
            string? tipo,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.GetSleepHistoryAsync(identity, bebeId, desde, hasta, tipo, ip, ct),
                ct));

        api.MapPost("/babies/{bebeId:guid}/history/export", (
            Guid bebeId,
            HttpContext http,
            ExportHistoryRequest request,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.ExportBabyHistoryAsync(identity, bebeId, request, ip, ct),
                ct));

        api.MapGet("/cribs/{cunaId:guid}/songs", (
            Guid cunaId,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.ListSongsAsync(identity, cunaId, ip, ct),
                ct));

        api.MapPost("/cribs/{cunaId:guid}/songs/play", (
            Guid cunaId,
            HttpContext http,
            PlaySongRequest request,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.PlaySongAsync(identity, cunaId, request, ip, ct),
                ct));

        api.MapPost("/cribs/{cunaId:guid}/mobile/play", (
            Guid cunaId,
            HttpContext http,
            PlayMobileRequest request,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.PlayMobileAsync(identity, cunaId, request, ip, ct),
                ct));

        api.MapGet("/cribs/{cunaId:guid}/toys", (
            Guid cunaId,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FeedingHistoryAndToysService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.GetToysAndPeripheralsStatusAsync(identity, cunaId, ip, ct),
                ct));

        return endpoints;
    }
}
