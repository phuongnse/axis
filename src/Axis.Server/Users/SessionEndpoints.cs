using System.Buffers.Text;
using System.Security.Claims;
using Axis.Core.Tenancy;
using Axis.Data.Storage;
using Axis.Server.Authentication;
using Axis.Server.Tenancy;
using Microsoft.AspNetCore.Authentication;

namespace Axis.Server.Users;

/// <summary>
/// A person's own sessions on the host of the request, which in practice are the sessions of the tenant's own hosts.
/// <c>GET /api/me/sessions</c> lists the live ones, <c>DELETE /api/me/sessions/{id}</c> ends one and
/// <c>DELETE /api/me/sessions</c> ends all but the current one. Ending needs a login at the identity provider in the last
/// <see cref="Session.ReauthenticationWindow"/>, checked before the id is looked up. The routes take a session only, so a
/// service account's bearer token gets a 401. A session's id is the base64url form of the row's hash, which cannot be turned
/// back into the cookie.
/// </summary>
internal static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me/sessions", ListAsync);
        endpoints.MapDelete("/api/me/sessions/{id}", EndOneAsync);
        endpoints.MapDelete("/api/me/sessions", EndOthersAsync);
    }

    private static async Task<IResult> ListAsync(HttpContext httpContext, TenantDatabase database, ITenantContextAccessor tenants, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await SignedInAsync(httpContext) is not { } caller)
        {
            return NobodyIsSignedIn();
        }

        var now = clock.GetUtcNow();
        var currentId = Session.ReadSessionId(caller.Principal);
        var connection = await database.GetConnectionAsync(cancellationToken);
        var sessions = (await OwnSessions.ListAsync(connection, caller.User.Id, tenants.Current?.SitePath, now, cancellationToken))
            .Select(entry =>
            {
                var id = Base64Url.EncodeToString(entry.IdHash);
                return new OwnSessionResponse(id, entry.SignedInAt, entry.LastRequestAt, id == currentId);
            })
            .ToArray();
        return Results.Ok(new OwnSessionsResponse(Session.MayEndSessions(caller.Principal, now), sessions));
    }

    private static async Task<IResult> EndOneAsync(
        string id, HttpContext httpContext, TenantDatabase database, ITenantContextAccessor tenants, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await SignedInAsync(httpContext) is not { } caller)
        {
            return NobodyIsSignedIn();
        }

        // The check comes first, so a caller who may not end sessions learns nothing about which ids exist.
        var now = clock.GetUtcNow();
        if (!Session.MayEndSessions(caller.Principal, now))
        {
            return LoginNeeded();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        return Decode(id) is { } hash && await OwnSessions.EndOneAsync(connection, caller.User.Id, tenants.Current?.SitePath, hash, now, cancellationToken) > 0
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The person has no live session with this id.");
    }

    private static async Task<IResult> EndOthersAsync(
        HttpContext httpContext, TenantDatabase database, ITenantContextAccessor tenants, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await SignedInAsync(httpContext) is not { } caller)
        {
            return NobodyIsSignedIn();
        }

        if (!Session.MayEndSessions(caller.Principal, clock.GetUtcNow()))
        {
            return LoginNeeded();
        }

        if (Session.ReadSessionId(caller.Principal) is not { } currentId || Decode(currentId) is not { } keep)
        {
            return NobodyIsSignedIn();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        await OwnSessions.EndOthersAsync(connection, caller.User.Id, tenants.Current?.SitePath, keep, cancellationToken);
        return Results.NoContent();
    }

    // The session only: CurrentUsers would also accept a service account's bearer token.
    private static async Task<(ClaimsPrincipal Principal, CurrentUserResponse User)?> SignedInAsync(HttpContext httpContext)
    {
        var result = await httpContext.AuthenticateAsync(Session.Scheme);
        return result.Succeeded && Session.Read(result.Principal) is { } user ? (result.Principal, user) : null;
    }

    private static byte[]? Decode(string id) =>
        Base64Url.IsValid(id) ? Base64Url.DecodeFromChars(id) : null;

    private static IResult NobodyIsSignedIn() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Nobody is signed in.");

    private static IResult LoginNeeded() =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Log in again at the identity provider to end sessions.");
}

/// <summary>One live session of the signed-in person. <c>Current</c> marks the session that made the request.</summary>
internal sealed record OwnSessionResponse(string Id, DateTimeOffset SignedInAt, DateTimeOffset LastRequestAt, bool Current);

/// <summary>The answer of <c>GET /api/me/sessions</c>. <c>EndAllowed</c> says whether the session may end sessions now.</summary>
internal sealed record OwnSessionsResponse(bool EndAllowed, IReadOnlyList<OwnSessionResponse> Sessions);
