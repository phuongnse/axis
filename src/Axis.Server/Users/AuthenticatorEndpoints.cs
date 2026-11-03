using System.Security.Claims;
using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Tenancy;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Authentication;

namespace Axis.Server.Users;

/// <summary>
/// A person's own authenticators. <c>GET /api/me/authenticators</c> says whether the signed-in person is an account of the
/// tenant's realm and lists their authenticator apps with the credential id, the label and the created date, oldest first.
/// The route takes a session only, so a service account's bearer token gets a 401. A brokered user, who signs in through an
/// outside identity provider, a person whose realm user is missing and a person of a tenant without a users client get
/// <c>account: false</c> and an empty list, because they hold no authenticator that Axis can manage. A failure at the
/// identity provider is a 502 with a fixed title, and the log names the step and the status code, never a body or a secret.
/// </summary>
internal static partial class AuthenticatorEndpoints
{
    public static void MapAuthenticatorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me/authenticators", ListAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        ITenantCatalog catalog,
        ITenantContextAccessor tenants,
        IHttpClientFactory httpClients,
        ILogger<UserLog> logger,
        CancellationToken cancellationToken)
    {
        if (await SignedInAsync(httpContext) is not { } caller)
        {
            return Grants.SignInRequired();
        }

        if (tenants.Current is not { } tenant
            || catalog.Find(tenant.TenantId) is not { PreviewOf: null, RealmUsers: { } access })
        {
            return NotAnAccount();
        }

        try
        {
            using var client = httpClients.CreateClient(nameof(AuthenticatorEndpoints));
            var users = new RealmUsers(client, access);
            if (await users.FindAsync(caller.Id, cancellationToken) is not { ProviderAlias: null })
            {
                return NotAnAccount();
            }

            var authenticators = await users.ListAuthenticatorsAsync(caller.Id, cancellationToken);
            return Results.Ok(new OwnAuthenticatorsResponse(
                true,
                [.. authenticators.Select(authenticator => new OwnAuthenticatorResponse(authenticator.Id, authenticator.Label, authenticator.CreatedAt))]));
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            LogListFailed(logger, tenant.TenantId, exception.Message);
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The identity provider could not read the authenticators.");
        }
    }

    private static IResult NotAnAccount() => Results.Ok(new OwnAuthenticatorsResponse(false, []));

    // The session only: CurrentUsers would also accept a service account's bearer token.
    private static async Task<CurrentUserResponse?> SignedInAsync(HttpContext httpContext)
    {
        var result = await httpContext.AuthenticateAsync(Session.Scheme);
        return result.Succeeded ? Session.Read(result.Principal) : null;
    }

    [LoggerMessage(LogLevel.Warning, "The authenticators of a user of tenant {TenantId} could not be read. {Reason}")]
    private static partial void LogListFailed(ILogger logger, string tenantId, string reason);
}

/// <summary>One authenticator app of the signed-in person.</summary>
internal sealed record OwnAuthenticatorResponse(string Id, string? Label, DateTimeOffset? CreatedAt);

/// <summary>The answer of <c>GET /api/me/authenticators</c>. <c>Account</c> is false for a brokered user, who has no authenticator in the realm.</summary>
internal sealed record OwnAuthenticatorsResponse(bool Account, IReadOnlyList<OwnAuthenticatorResponse> Authenticators);
