using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Releases;
using Axis.Core.Tenancy;
using Axis.Data.Audit;
using Axis.Data.Profiles;
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
/// <c>POST /api/users/{id}/give-role</c> and <c>remove-role</c> give a person a role, or take it away. They take
/// <c>{ "applicationId": "...", "role": "..." }</c> for an application role, or <c>{ "role": "..." }</c> for a platform role
/// (<c>author</c>, <c>publisher</c>, <c>operator</c> or <c>administrator</c>), and answer 204. They check in the order above, up
/// to the realm user (404), and only then read the body, so a refused caller learns nothing about the body. A body that is
/// missing or malformed, a role that is blank, a role without an application id that is not a platform role, an application
/// without an active release, and for give a role that the active release does not declare, answer 400 with a fixed title. A
/// platform role then needs a login at the identity provider in the last <see cref="Session.ReauthenticationWindow"/> (403), and
/// removing one's own administrator role answers 409. A change writes one audit record, <c>user.roleGiven</c> or
/// <c>user.roleRemoved</c>, with the administrator as actor, the application id (null for a platform role), and the user and
/// role in <c>details</c>, with <c>platform: true</c> for a platform role. Giving a role the person holds, or removing one they
/// do not hold, answers 204 and writes nothing. A session reads the given roles at every request, so a change applies at the
/// person's next request.
/// </para>
/// <para>
/// <c>POST /api/users/invite</c> takes <c>{ "email", "displayName", "roles": [{ "applicationId", "role" }] }</c> and answers 201 with
/// <c>{ "id" }</c>. <c>POST /api/users/{id}/resend-invitation</c> answers 204. Both take a session only and check in the order of
/// the routes above. Invite then reads the body (400 with a fixed title for a malformed body, a blank display name, an email that
/// is not an address or a role that the give-role route refuses), requires a recent login when a role is a platform role (403) and
/// answers 409 for an email that an account uses. It creates the realm user, commits the given roles and the audit records, and
/// only then emails the link, so a failed email is a 502 and the account stays invited. Resend answers 404 for an unknown user and
/// 409 for a brokered user, a person who has signed in and a user with a password. It deletes the realm user and creates it again,
/// which ends the old link and gives a new id, moves the given roles to it and emails a new link. Each call writes one
/// <c>user.invited</c> or <c>user.invitationResent</c> record, with <c>details.userId</c> and <c>details.email</c> and never the
/// link. Invite also writes a <c>user.roleGiven</c> record for each role.
/// </para>
/// </summary>
internal static partial class UserCommandEndpoints
{
    public static void MapUserCommandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/users/invite", (HttpContext httpContext, CancellationToken cancellationToken) =>
            InviteAsync(httpContext, cancellationToken));
        endpoints.MapPost("/api/users/{id}/resend-invitation", (string id, HttpContext httpContext, CancellationToken cancellationToken) =>
            ResendAsync(id, httpContext, cancellationToken));
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
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is null)
        {
            return refusal!;
        }

        var (authenticated, caller, tenant, access) = (opening.Principal, opening.Caller, opening.Tenant, opening.Access);

        // These two checks come before the user is looked up, so a refused caller learns nothing about which ids exist.
        if (command == Command.Disable)
        {
            if (!Session.MayEndSessions(authenticated, services.GetRequiredService<TimeProvider>().GetUtcNow()))
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
            new AuditEntry(caller.Id, Action(command), null, null, null, null, new JsonObject { ["userId"] = id }),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>
    /// Gives or removes an application or platform role. The body is read by hand and last, because binding it would answer 400 before the
    /// 401 and 403 checks.
    /// </summary>
    private static async Task<IResult> RunRoleAsync(bool give, string id, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var services = httpContext.RequestServices;
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is null)
        {
            return refusal!;
        }

        var (authenticated, caller, tenant, access) = (opening.Principal, opening.Caller, opening.Tenant, opening.Access);

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
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The request needs a role, and an application id for an application role.");
        }

        var database = services.GetRequiredService<TenantDatabase>();
        if (await CheckRoleAsync(services, database, body.ApplicationId, body.Role, give, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (body.ApplicationId is null)
        {
            if (!Session.MayEndSessions(authenticated, services.GetRequiredService<TimeProvider>().GetUtcNow()))
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Log in again at the identity provider to change a platform role.");
            }

            if (!give && id == caller.Id && body.Role == PlatformRoles.AdministratorKind)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "An administrator cannot remove their own administrator role.");
            }
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var changed = give
            ? await GivenRoles.GiveAsync(transaction, id, body.ApplicationId, body.Role, cancellationToken)
            : await GivenRoles.RemoveAsync(transaction, id, body.ApplicationId, body.Role, cancellationToken);
        if (changed)
        {
            await AuditRecords.AppendAsync(
                transaction,
                new AuditEntry(
                    caller.Id,
                    give ? AuditActions.UserRoleGiven : AuditActions.UserRoleRemoved,
                    body.ApplicationId,
                    null,
                    null,
                    null,
                    UserInvitations.RoleDetails(id, body.ApplicationId, body.Role)),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>Invites a person: the realm user, the given roles and the audit records, then the email.</summary>
    private static async Task<IResult> InviteAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var services = httpContext.RequestServices;
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is null)
        {
            return refusal!;
        }

        InviteRequest? body;
        try
        {
            body = await httpContext.Request.ReadFromJsonAsync<InviteRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            body = null;
        }

        if (body is null || RealmUsers.NormalizeEmail(body.Email) is not { } email)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The request needs an email address.");
        }

        if (string.IsNullOrWhiteSpace(body.DisplayName))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The request needs a display name.");
        }

        var roles = body.Roles ?? [];
        var database = services.GetRequiredService<TenantDatabase>();
        foreach (var role in roles)
        {
            if (role is null || string.IsNullOrWhiteSpace(role.Role))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Each role needs a role, and an application id for an application role.");
            }

            if (await CheckRoleAsync(services, database, role.ApplicationId, role.Role, give: true, cancellationToken) is { } invalid)
            {
                return invalid;
            }
        }

        if (roles.Any(role => role.ApplicationId is null)
            && !Session.MayEndSessions(opening.Principal, services.GetRequiredService<TimeProvider>().GetUtcNow()))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Log in again at the identity provider to give a platform role.");
        }

        var step = "search the users by email";
        string id;
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(UserCommandEndpoints));
        var realm = new RealmUsers(client, opening.Access);
        try
        {
            if (await realm.AccountUsesEmailAsync(email, cancellationToken))
            {
                return EmailInUse();
            }

            step = "create the invited user";
            id = await realm.CreateInvitedAsync(email, body.DisplayName.Trim(), null, cancellationToken);
        }
        catch (RealmUserConflictException)
        {
            return EmailInUse();
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            LogCommandFailed(services.GetRequiredService<ILogger<UserLog>>(), opening.Tenant.TenantId, "Invite", step, exception.Message);
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The identity provider could not run the command.");
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await UserInvitations.RecordAsync(
                transaction,
                opening.Caller.Id,
                opening.Caller.DisplayName,
                id,
                email,
                [.. roles.Select(role => new GivenRole(role.ApplicationId, role.Role!))],
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (await SendInvitationAsync(realm, id, httpContext, opening, "Invite", cancellationToken) is { } failed)
        {
            return failed;
        }

        return Results.Json(new { id }, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>
    /// Sends a person who has not signed in a new invitation. The realm cannot revoke a link it issued, so the realm user is deleted
    /// and created again, which gives it a new id, and the given roles move to that id.
    /// </summary>
    private static async Task<IResult> ResendAsync(string id, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var services = httpContext.RequestServices;
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is null)
        {
            return refusal!;
        }

        var database = services.GetRequiredService<TenantDatabase>();
        var step = "find the user";
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(UserCommandEndpoints));
        var realm = new RealmUsers(client, opening.Access);
        string newId;
        string email;
        try
        {
            if (await realm.FindAsync(id, cancellationToken) is not { } user)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The tenant has no user with this id.");
            }

            var notInvited = Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Only a person who has not set a password can get a new invitation.");
            if (user.ProviderAlias is not null
                || await UserProfiles.ExistsAsync(await database.GetConnectionAsync(cancellationToken), null, id, cancellationToken))
            {
                return notInvited;
            }

            step = "read the user";
            var invitee = await realm.ReadInviteeAsync(id, cancellationToken);
            if (invitee.HasPassword || invitee.Email is not { } invitedEmail)
            {
                return notInvited;
            }

            email = invitedEmail;
            step = "delete the user";
            await realm.DeleteAsync(id, cancellationToken);
            step = "create the invited user";
            newId = await realm.CreateInvitedAsync(email, invitee.FirstName, invitee.LastName, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            LogCommandFailed(services.GetRequiredService<ILogger<UserLog>>(), opening.Tenant.TenantId, "ResendInvitation", step, exception.Message);
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The identity provider could not run the command.");
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await GivenRoles.MoveAsync(transaction, id, newId, cancellationToken);
            await AuditRecords.AppendAsync(
                transaction,
                new AuditEntry(
                    opening.Caller.Id,
                    AuditActions.UserInvitationResent,
                    null,
                    null,
                    null,
                    null,
                    new JsonObject { ["userId"] = newId, ["previousUserId"] = id, ["email"] = email }),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await SendInvitationAsync(realm, newId, httpContext, opening, "ResendInvitation", cancellationToken) ?? Results.NoContent();
    }

    /// <summary>Emails the link, with the tenant's home as the place it leads to. Returns the 502 answer when the realm fails, or null.</summary>
    private static async Task<IResult?> SendInvitationAsync(
        RealmUsers realm, string userId, HttpContext httpContext, Opening opening, string command, CancellationToken cancellationToken)
    {
        try
        {
            await realm.SendInvitationAsync(userId, new Uri($"{httpContext.Request.Scheme}://{httpContext.Request.Host}/"), cancellationToken);
            return null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            LogCommandFailed(httpContext.RequestServices.GetRequiredService<ILogger<UserLog>>(), opening.Tenant.TenantId, command, "send the invitation", exception.Message);
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The identity provider could not send the invitation.");
        }
    }

    private static IResult EmailInUse() =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "An account already uses this email.");

    /// <summary>
    /// Checks the opening of every command: a signed-in user from the session only, because CurrentUsers would also accept a service
    /// account's bearer token (401), the administrator role (403) and a tenant with a users client (404). It returns the refusal
    /// when a check fails.
    /// </summary>
    private static async Task<(Opening? Opening, IResult? Refusal)> OpenAsync(HttpContext httpContext)
    {
        var services = httpContext.RequestServices;
        var authenticated = await httpContext.AuthenticateAsync(Session.Scheme);
        if (!authenticated.Succeeded || Session.Read(authenticated.Principal) is not { } caller)
        {
            return (null, Grants.SignInRequired());
        }

        if (!services.GetRequiredService<UsersSettings>().Allows(caller.Roles))
        {
            return (null, Grants.Forbidden());
        }

        var catalog = services.GetRequiredService<ITenantCatalog>();
        var tenants = services.GetRequiredService<ITenantContextAccessor>();
        if (tenants.Current is not { } tenant || catalog.Find(tenant.TenantId) is not { PreviewOf: null, RealmUsers: { } access })
        {
            return (null, Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This tenant has no users page."));
        }

        return (new Opening(authenticated.Principal, caller, tenant, access), null);
    }

    /// <summary>
    /// Checks a role: an application role needs an application with an active release, which for give declares the role, and a
    /// platform role must be a kind. Remove needs no declaration, so an administrator can clear a row for a role that a later
    /// release dropped. Returns the 400 answer, or null.
    /// </summary>
    private static async Task<IResult?> CheckRoleAsync(
        IServiceProvider services, TenantDatabase database, Guid? applicationId, string role, bool give, CancellationToken cancellationToken)
    {
        if (applicationId is { } id)
        {
            var active = await new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken)).FindByApplicationIdAsync(id, cancellationToken);
            var model = active is null
                ? null
                : await services.GetRequiredService<ActiveApplicationResolver>().FindReleaseModelAsync(id, active.ReleaseId, cancellationToken);
            if (model is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The application has no active release.");
            }

            if (give && !model.Roles.Any(declared => string.Equals(declared.Name, role, StringComparison.Ordinal)))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The active release of the application does not declare this role.");
            }

            return null;
        }

        return PlatformRoles.IsKind(role)
            ? null
            : Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "A platform role is author, publisher, operator or administrator.");
    }

    private sealed record Opening(ClaimsPrincipal Principal, CurrentUserResponse Caller, TenantContext Tenant, TenantRealmUsers Access);

    private static string Action(Command command) => command switch
    {
        Command.Disable => AuditActions.UserDisabled,
        Command.Enable => AuditActions.UserEnabled,
        _ => AuditActions.UserAuthenticatorReset,
    };

    [LoggerMessage(LogLevel.Warning, "The command {Command} of tenant {TenantId} failed at the step '{Step}'. {Reason}")]
    private static partial void LogCommandFailed(ILogger logger, string tenantId, string command, string step, string reason);
}

/// <summary>The body of the invite route. <c>Roles</c> are optional, each as in the give-role route.</summary>
internal sealed record InviteRequest(string? Email, string? DisplayName, IReadOnlyList<UserRoleRequest>? Roles);

/// <summary>The body of the give and remove role routes. A null <c>ApplicationId</c> means a platform role.</summary>
internal sealed record UserRoleRequest(Guid? ApplicationId, string? Role);
