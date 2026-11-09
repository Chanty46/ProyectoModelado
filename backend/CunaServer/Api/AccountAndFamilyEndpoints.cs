using CunaServer.Data;
using CunaServer.Services;

namespace CunaServer.Api;

public static class AccountAndFamilyEndpoints
{
    public static IEndpointRouteBuilder MapAccountAndFamilyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api");

        group.MapGet("/account/me", async (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var context = await service.GetAccountContextAsync(identity, ct);
            return Results.Ok(context);
        });

        group.MapPost("/account/bootstrap", async (
            HttpContext http,
            BootstrapAccountRequest? request,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.BootstrapPrincipalAccountAsync(identity, request, ct);
            return ToHttpResult(result);
        });

        group.MapPatch("/family", async (
            HttpContext http,
            UpdateFamilyRequest request,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.UpdateFamilyNameAsync(identity, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapPost("/family/profiles", async (
            HttpContext http,
            UpsertCaregiverProfileRequest request,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.CreateProfileAsync(identity, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapPatch("/family/profiles/{profileId:guid}", async (
            Guid profileId,
            HttpContext http,
            UpsertCaregiverProfileRequest request,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.UpdateProfileAsync(identity, profileId, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapDelete("/family/profiles/{profileId:guid}", async (
            Guid profileId,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.SoftDeleteProfileAsync(identity, profileId, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapPost("/family/invitations", async (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.CreateInvitationAsync(identity, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapPost("/family/invitations/accept", async (
            HttpContext http,
            AcceptInvitationRequest request,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.AcceptInvitationAsync(identity, request, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapDelete("/family/members/{memberUserId:guid}", async (
            Guid memberUserId,
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.RevokeSecondaryMemberAsync(identity, memberUserId, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        group.MapGet("/family/security-audit", async (
            HttpContext http,
            IJwtTokenValidator jwtValidator,
            FamilyAccountService service,
            CancellationToken ct) =>
        {
            var identity = await AuthenticateAsync(http, jwtValidator, ct);
            if (identity is null)
            {
                return Results.Json(new { error = "No autenticado." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.ListSecurityAuditAsync(identity, GetClientIp(http), ct);
            return ToHttpResult(result);
        });

        return endpoints;
    }

    private static Task<AuthenticatedIdentity?> AuthenticateAsync(
        HttpContext http,
        IJwtTokenValidator jwtValidator,
        CancellationToken ct)
    {
        var authHeader = http.Request.Headers.Authorization.ToString();
        return jwtValidator.ValidateBearerTokenAsync(authHeader, ct);
    }

    private static string? GetClientIp(HttpContext http)
    {
        return http.Connection.RemoteIpAddress?.ToString();
    }

    private static IResult ToHttpResult<T>(ServiceResult<T> result)
    {
        return result.Status switch
        {
            ServiceStatus.Ok => Results.Ok(result.Data),
            ServiceStatus.Created => Results.Json(result.Data, statusCode: StatusCodes.Status201Created),
            ServiceStatus.BadRequest => Results.BadRequest(new { error = result.ErrorMessage }),
            ServiceStatus.Forbidden => Results.Json(new { error = result.ErrorMessage }, statusCode: StatusCodes.Status403Forbidden),
            ServiceStatus.NotFound => Results.NotFound(new { error = result.ErrorMessage }),
            ServiceStatus.Conflict => Results.Conflict(new { error = result.ErrorMessage }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
