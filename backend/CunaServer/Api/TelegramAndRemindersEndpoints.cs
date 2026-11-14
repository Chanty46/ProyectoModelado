using CunaServer.Data;
using CunaServer.Services;
using CunaServer.Telegram;

namespace CunaServer.Api;

public static class TelegramAndRemindersEndpoints
{
    public static IEndpointRouteBuilder MapTelegramAndRemindersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api");

        group.MapGet("/telegram/link", (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            TelegramBotService telegramService,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, _) => telegramService.GetLinkStatusAsync(identity, ct),
                ct));

        group.MapPost("/telegram/link-code", (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            TelegramBotService telegramService,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => telegramService.GenerateLinkCodeAsync(identity, ip, ct),
                ct));

        group.MapDelete("/telegram/link", (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            TelegramBotService telegramService,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => telegramService.UnlinkMyTelegramAsync(identity, ip, ct),
                ct));

        group.MapGet("/telegram/status", (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            TelegramBotService telegramService,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, _) => telegramService.GetServiceStatusAsync(identity, ct),
                ct));

        group.MapPost("/telegram/test", (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            TelegramBotService telegramService,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => telegramService.SendTestNotificationAsync(identity, ip, ct),
                ct));

        group.MapGet("/reminders", (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            TelegramBotService telegramService,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, _) => telegramService.GetFeedingReminderAsync(identity, ct),
                ct));

        group.MapPut("/reminders", (
            HttpContext http,
            UpdateFeedingReminderRequest request,
            IJwtTokenValidator jwtValidator,
            TelegramBotService telegramService,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => telegramService.UpdateFeedingReminderAsync(identity, request, ip, ct),
                ct));

        return endpoints;
    }
}
