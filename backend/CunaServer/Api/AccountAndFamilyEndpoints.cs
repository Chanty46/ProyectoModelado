using CunaServer.Data;
using CunaServer.Services;

namespace CunaServer.Api;

public static class AccountAndFamilyEndpoints
{
    public static IEndpointRouteBuilder MapAccountAndFamilyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api");

        group.MapGet("/account/me", (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteAuthenticatedAsync(
                http,
                jwtValidator,
                async (identity, _) => Results.Ok(await service.GetAccountContextAsync(identity, ct)),
                ct));

        group.MapPost("/account/bootstrap", (
            HttpContext http,
            BootstrapAccountRequest? request,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, _) => service.BootstrapPrincipalAccountAsync(identity, request, ct),
                ct));

        group.MapPatch("/family", (
            HttpContext http,
            UpdateFamilyRequest request,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.UpdateFamilyNameAsync(identity, request, ip, ct),
                ct));

        group.MapPost("/family/profiles", (
            HttpContext http,
            UpsertCaregiverProfileRequest request,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.CreateProfileAsync(identity, request, ip, ct),
                ct));

        group.MapPatch("/family/profiles/{profileId:guid}", (
            Guid profileId,
            HttpContext http,
            UpsertCaregiverProfileRequest request,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.UpdateProfileAsync(identity, profileId, request, ip, ct),
                ct));

        group.MapDelete("/family/profiles/{profileId:guid}", (
            Guid profileId,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.SoftDeleteProfileAsync(identity, profileId, ip, ct),
                ct));

        group.MapPost("/family/invitations", (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.CreateInvitationAsync(identity, ip, ct),
                ct));

        group.MapPost("/family/invitations/accept", (
            HttpContext http,
            AcceptInvitationRequest request,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.AcceptInvitationAsync(identity, request, ip, ct),
                ct));

        group.MapDelete("/family/members/{memberUserId:guid}", (
            Guid memberUserId,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.RevokeSecondaryMemberAsync(identity, memberUserId, ip, ct),
                ct));

        group.MapGet("/family/security-audit", (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
            EndpointHelpers.ExecuteServiceAsync(
                http,
                jwtValidator,
                (identity, ip) => service.ListSecurityAuditAsync(identity, ip, ct),
                ct));

        return endpoints;
    }
}
