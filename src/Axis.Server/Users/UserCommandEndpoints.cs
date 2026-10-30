using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Releases;
using Axis.Core.Tenancy;
using Axis.Data.Audit;
using Axis.Data.Storage;
using Axis.Server.Applications;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Tenancy;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Authentication;

namespace Axis.Server.Users;

/// <summary>
/// The commands of the users page. <c>POST /api/users/{id}/disable</c> stops every sign-in of a person and ends their Axis
/// sessions and realm sessions, <c>POST /api/users/{id}/enable</c> lets them sign in again and
/// <c>POST /api/users/{id}/reset-authenticator</c> removes an account's authenticator and ends the person's sessions. Each
/// answers 204 and writes one audit record: <c>user.disabled</c>, <c>user.enabled</c> or <c>user.authenticatorReset</c>, with
/// the administrator as actor and the user in <c>details.userId</c>. The routes take a session only, so a service account's
/// bearer token gets a 401. A request is checked in order: signed-in user (401), administrator role (403), a tenant with a
/// users client (404), for disable a login at the identity provider in the last <see cref="Session.ReauthenticationWindow"/>
/// (403), for disable not oneself (409), the realm user (404, also for a service account), and for reset an account (409). A
/// failure at the identity provider is a 502 with a fixed title, and the log names the step and the status code, never a body
/// or a secret. The realm steps run first and the Axis sessions and the audit record follow in one transaction, so a
/// command that stopped partway can run again.
/// <para>
/// <c>POST /api/users/{id}/give-role</c> and <c>remove-role</c> give a person an application role, or take it away. They take
/// <c>{ "applicationId": "...", "role": "..." }</c> and answer 204. They check in the order above, up to the realm user (404),
/// and only then read the body, so a refused caller learns nothing about the body. A body that is missing, malformed or
/// without an application id (a platform role, which is not built yet), a role that is blank, an application without an active
/// release, and for give a role that the active release does not declare, answer 400 with a fixed title. A change writes one
/// audit record, <c>user.roleGiven</c> or <c>user.roleRemoved</c>, with the administrator as actor, the application id, and the
/// user and role in <c>details</c>. Giving a role the person holds, or removing one they do not hold, answers 204 and writes
/// nothing. A session reads the given roles at every request, so a change applies at the person's next request.
/// </para>
/// </summary>
internal static partial class UserCommandEndpoints
{
    public static void MapUserCommandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/users/{id}/disable", (string id, HttpContext httpContext, CancellationToken cancellationToken) =>
            RunAsync(Command.Disable, id, httpContext, cancellationToken));
        endpoints.MapPost("/api/users/{id}/enable", (string id, HttpContext httpContext, CancellationToken cancellationToken) =>
            RunAsync(Command.Enable, id, httpContext, cancellationToken));
        endpoints.MapPost("/api/users/{id}/reset-authenticator", (string id, HttpContext httpContext, CancellationToken cancellationToken) =>
            RunAsync(Command.ResetAuthenticator, id, httpContext, cancellationToken));
        endpoints.MapPost("/api/users/{id}/give-role", (string id, HttpContext httpContext, CancellationToken cancellationToken) =>
            RunRoleAsync(give: true, id, httpContext, cancellationToken));
        endpoints.MapPost("/api/users/{id}/remove-role", (string id, HttpContext httpContext, CancellationToken cancellationToken) =>
            RunRoleAsync(give: false, id, httpContext, cancellationToken));
    }

    private enum Command
    {
        Disable,
        Enable,
        ResetAuthenticator,
    }

    private static async Task<IResult> RunAsync(
        Command command, string id, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var services = httpContext.RequestServices;
        // The session only: CurrentUsers would also accept a service account's bearer token.
        var authenticated = await httpContext.AuthenticateAsync(Session.Scheme);
        if (!authenticated.Succeeded || Session.Read(authenticated.Principal) is not { } caller)
        {
            return Grants.SignInRequired();
        }

        if (!services.GetRequiredService<UsersSettings>().Allows(caller.Roles))
        {
            return Grants.Forbidden();
        }

        var catalog = services.GetRequiredService<ITenantCatalog>();
        var tenants = services.GetRequiredService<ITenantContextAccessor>();
        if (tenants.Current is not { } tenant || catalog.Find(tenant.TenantId) is not { PreviewOf: null, RealmUsers: { } access })
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This tenant has no users page.");
        }

        // These two checks come before the user is looked up, so a refused caller learns nothing about which ids exist.
        if (command == Command.Disable)
        {
            if (!Session.MayEndSessions(authenticated.Principal, services.GetRequiredService<TimeProvider>().GetUtcNow()))
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Log in again at the identity provider to disable a person.");
            }

            if (id == caller.Id)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "An administrator cannot disable themselves.");
            }
        }

        var step = "find the user";
        try
        {
            using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(UserCommandEndpoints));
            var realm = new RealmUsers(client, access);
            if (await realm.FindAsync(id, cancellationToken) is not { } user)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The tenant has no user with this id.");
            }

            if (command == Command.ResetAuthenticator && user.ProviderAlias is not null)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Only an account has an authenticator to reset.");
            }

            switch (command)
            {
                case Command.Disable:
                    step = "disable the user";
                    await realm.SetEnabledAsync(id, enabled: false, cancellationToken);
                    step = "end the realm sessions";
                    await realm.LogOutAsync(id, cancellationToken);
                    break;
                case Command.Enable:
                    step = "enable the user";
                    await realm.SetEnabledAsync(id, enabled: true, cancellationToken);
                    break;
                default:
                    step = "remove the authenticator";
                    await realm.RemoveAuthenticatorsAsync(id, cancellationToken);
                    step = "end the realm sessions";
                    await realm.LogOutAsync(id, cancellationToken);
                    break;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            LogCommandFailed(services.GetRequiredService<ILogger<UserLog>>(), tenant.TenantId, command.ToString(), step, exception.Message);
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The identity provider could not run the command.");
        }

        var connection = await services.GetRequiredService<TenantDatabase>().GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (command != Command.Enable)
        {
            await SessionRevocation.EndForUserAsync(connection, id, cancellationToken);
        }

        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(caller.Id, Action(command), null, null, null, null, new JsonObject { ["userId"] = id }, caller.DisplayName),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>
    /// Gives or removes an application role. The body is read by hand and last, because binding it would answer 400 before the
    /// 401 and 403 checks.
    /// </summary>
    private static async Task<IResult> RunRoleAsync(bool give, string id, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var services = httpContext.RequestServices;
        var authenticated = await httpContext.AuthenticateAsync(Session.Scheme);
        if (!authenticated.Succeeded || Session.Read(authenticated.Principal) is not { } caller)
        {
            return Grants.SignInRequired();
        }

        if (!services.GetRequiredService<UsersSettings>().Allows(caller.Roles))
        {
            return Grants.Forbidden();
        }

        var catalog = services.GetRequiredService<ITenantCatalog>();
        var tenants = services.GetRequiredService<ITenantContextAccessor>();
        if (tenants.Current is not { } tenant || catalog.Find(tenant.TenantId) is not { PreviewOf: null, RealmUsers: { } access })
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This tenant has no users page.");
        }

        try
        {
            using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(UserCommandEndpoints));
            if (await new RealmUsers(client, access).FindAsync(id, cancellationToken) is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The tenant has no user with this id.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            LogCommandFailed(services.GetRequiredService<ILogger<UserLog>>(), tenant.TenantId, give ? "GiveRole" : "RemoveRole", "find the user", exception.Message);
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The identity provider could not run the command.");
        }

        UserRoleRequest? body;
        try
        {
            body = await httpContext.Request.ReadFromJsonAsync<UserRoleRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            body = null;
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Role))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The request needs an application id and a role.");
        }

        if (body.ApplicationId is not { } applicationId)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Platform roles cannot be given yet.");
        }

        var database = services.GetRequiredService<TenantDatabase>();
        var active = await new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken)).FindByApplicationIdAsync(applicationId, cancellationToken);
        var model = active is null
            ? null
            : await services.GetRequiredService<ActiveApplicationResolver>().FindReleaseModelAsync(applicationId, active.ReleaseId, cancellationToken);
        if (model is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The application has no active release.");
        }

        // Removing needs no declaration, so an administrator can clear a row for a role that a later release dropped.
        if (give && !model.Roles.Any(role => string.Equals(role.Name, body.Role, StringComparison.Ordinal)))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The active release of the application does not declare this role.");
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var changed = give
            ? await GivenRoles.GiveAsync(transaction, id, applicationId, body.Role, cancellationToken)
            : await GivenRoles.RemoveAsync(transaction, id, applicationId, body.Role, cancellationToken);
        if (changed)
        {
            await AuditRecords.AppendAsync(
                transaction,
                new AuditEntry(
                    caller.Id,
                    give ? AuditActions.UserRoleGiven : AuditActions.UserRoleRemoved,
                    applicationId,
                    null,
                    null,
                    null,
                    new JsonObject { ["userId"] = id, ["role"] = body.Role },
                    caller.DisplayName),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static string Action(Command command) => command switch
    {
        Command.Disable => AuditActions.UserDisabled,
        Command.Enable => AuditActions.UserEnabled,
        _ => AuditActions.UserAuthenticatorReset,
    };

    [LoggerMessage(LogLevel.Warning, "The command {Command} of tenant {TenantId} failed at the step '{Step}'. {Reason}")]
    private static partial void LogCommandFailed(ILogger logger, string tenantId, string command, string step, string reason);
}

/// <summary>The body of the give and remove role routes. A null <c>ApplicationId</c> would mean a platform role.</summary>
internal sealed record UserRoleRequest(Guid? ApplicationId, string? Role);
