using System.Text.Json;
using Axis.Administration;
using Axis.Core.Tenancy;
using Axis.Data.Storage;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Server.CellApi;

/// <summary>
/// The routes of a tenant administrator's outside MCP client switch and connected apps on the tenant's own host, see
/// <c>docs/reference/user-administration.md#outside-mcp-clients</c>. They check, in order, a session (<c>401</c>), the administrator
/// role (<c>403</c>) and a tenant with a directory row that is not a preview tenant and has realm details (<c>404</c>), and for
/// <c>PUT</c> then the body (<c>400</c>). The connected apps route also needs the tenant's users client details (<c>404</c>).
/// <para>
/// <c>PUT</c> replaces <c>Ai:OutsideClients</c> and <c>Ai:AllowedClients</c> with a platform audit record in the same transaction
/// that names <c>outsideClients</c> and <c>allowedClients</c> and never holds a client id, and then brings the realm in step. A
/// failure at the identity provider is a <c>502</c> with a fixed title, and the stored change and its record stay. <c>GET</c>
/// answers both stored values.
/// </para>
/// <para>
/// <c>GET /api/tenant/connected-apps</c> answers each outside client that holds a consent, with the people who consented and the
/// client's last use. A failure at the identity provider is a <c>502</c> with a fixed title.
/// </para>
/// </summary>
internal static partial class TenantOutsideClientsEndpoints
{
    internal const string SetCommand = "tenant outside-clients set";

    public static void MapTenantOutsideClientsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenant/outside-clients", ReadAsync);
        endpoints.MapPut("/api/tenant/outside-clients", SetAsync);
        endpoints.MapGet("/api/tenant/connected-apps", ListConnectedAppsAsync);
    }

    private static async Task<IResult> ReadAsync(
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is not { } open)
        {
            return refusal!;
        }

        await using var platform = CellEndpoints.Open(dataSource);
        var stored = await platform.Tenants.AsNoTracking()
            .Where(row => row.Id == open.TenantId && row.PreviewOf == null)
            .Select(row => new { row.AiOutsideClients, row.AiAllowedClients })
            .SingleOrDefaultAsync(cancellationToken);
        return stored is null
            ? NoSetting()
            : Results.Ok(new TenantOutsideClientsResponse(stored.AiOutsideClients, stored.AiAllowedClients));
    }

    private static async Task<IResult> SetAsync(
        HttpContext httpContext,
        CellPlatformWriter writer,
        CancellationToken cancellationToken)
    {
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is not { } open)
        {
            return refusal!;
        }

        TenantOutsideClientsRequest? body = null;
        if (httpContext.Request.HasJsonContentType())
        {
            try
            {
                body = await httpContext.Request.ReadFromJsonAsync<TenantOutsideClientsRequest>(cancellationToken);
            }
            catch (JsonException)
            {
            }
        }

        if (body is null || body.OutsideClients is not (TenantOutsideClients.All or TenantOutsideClients.AllowList or TenantOutsideClients.None))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The request needs an outside clients setting.");
        }

        var requested = body.AllowedClients ?? [];
        if (requested.Any(string.IsNullOrWhiteSpace))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Each allowed client needs a client id.");
        }

        var mode = body.OutsideClients;
        var allowed = requested.Select(clientId => clientId!.Trim()).Distinct(StringComparer.Ordinal).ToArray();

        await using (var platform = writer.Open())
        {
            await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);
            var row = await platform.Tenants.SingleOrDefaultAsync(tenant => tenant.Id == open.TenantId && tenant.PreviewOf == null, cancellationToken);
            if (row is null)
            {
                return NoSetting();
            }

            row.AiOutsideClients = mode;
            row.AiAllowedClients = allowed;
            row.Version++;
            await PlatformAuditRecords.WriteAsync(
                platform, SetCommand, open.TenantId, ["outsideClients", "allowedClients"], open.CallerId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // The realm follows the commit, so a failed update leaves the stored change and its record, and the same PUT again applies it.
        var services = httpContext.RequestServices;
        try
        {
            using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TenantOutsideClientsEndpoints));
            await new RealmSync(client, open.Realm.BaseAddress)
                .SyncOutsideClientsAsync(open.Realm.Name, open.Realm.ActivationClientSecret.Reveal(), mode, allowed, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            LogRealmFailed(services.GetRequiredService<ILogger<UserLog>>(), open.TenantId, exception.Message);
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The identity provider could not apply the change.");
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ListConnectedAppsAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is not { } open)
        {
            return refusal!;
        }

        if (open.Users is null)
        {
            return NoSetting();
        }

        var services = httpContext.RequestServices;
        IReadOnlyList<RealmOutsideClient> outsideClients;
        IReadOnlyList<RealmConsent> consents = [];
        try
        {
            using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TenantOutsideClientsEndpoints));
            outsideClients = await new RealmSync(client, open.Realm.BaseAddress)
                .ListOutsideClientsAsync(open.Realm.Name, open.Realm.ActivationClientSecret.Reveal(), cancellationToken);

            // A realm with no outside client has nothing to list, so its users need no walk.
            if (outsideClients.Count > 0)
            {
                consents = await new RealmUsers(client, open.Users).ListConsentsAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            LogListConnectedAppsFailed(services.GetRequiredService<ILogger<UserLog>>(), open.TenantId, exception.Message);
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The identity provider could not list the connected apps.");
        }

        var names = outsideClients.ToDictionary(client => client.ClientId, client => client.Name, StringComparer.Ordinal);
        var consented = consents
            .Where(consent => names.ContainsKey(consent.ClientId))
            .GroupBy(consent => consent.ClientId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();
        if (consented.Count == 0)
        {
            return Results.Ok(new TenantConnectedAppsResponse([]));
        }

        var connection = await services.GetRequiredService<TenantDatabase>().GetConnectionAsync(cancellationToken);
        var lastUses = await McpClients.ListLastUsesAsync(connection, [.. consented.Select(group => group.Key)], cancellationToken);
        return Results.Ok(new TenantConnectedAppsResponse(
            [.. consented.Select(group => new TenantConnectedApp(
                group.Key,
                names[group.Key],
                lastUses.GetValueOrDefault(group.Key),
                [.. group
                    .OrderBy(consent => consent.ConsentedAt)
                    .ThenBy(consent => consent.UserId, StringComparer.Ordinal)
                    .Select(consent => new TenantConnectedAppPerson(consent.UserId, consent.DisplayName, consent.Email, consent.ConsentedAt))]))]));
    }

    private static IResult NoSetting() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This tenant has no outside clients setting.");

    // A signed-in user from the session only, because a service account's bearer token has no right here (401), the administrator
    // role (403) and a tenant with a directory row that is not a preview tenant and has realm details (404).
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

        if (services.GetRequiredService<ITenantContextAccessor>().Current is not { } tenant
            || services.GetRequiredService<ITenantCatalog>().Find(tenant.TenantId) is not { PreviewOf: null, Realm: { } realm } entry)
        {
            return (null, NoSetting());
        }

        return (new Opening(tenant.TenantId, caller.Id, realm, entry.RealmUsers), null);
    }

    [LoggerMessage(LogLevel.Warning, "The realm of tenant {TenantId} could not apply the outside clients setting. {Reason}")]
    private static partial void LogRealmFailed(ILogger logger, string tenantId, string reason);

    [LoggerMessage(LogLevel.Warning, "The realm of tenant {TenantId} could not list the connected apps. {Reason}")]
    private static partial void LogListConnectedAppsFailed(ILogger logger, string tenantId, string reason);

    private sealed record Opening(string TenantId, string CallerId, TenantRealmAccess Realm, TenantRealmUsers? Users);
}

/// <summary>The body of <c>PUT /api/tenant/outside-clients</c>.</summary>
internal sealed record TenantOutsideClientsRequest(string? OutsideClients, string?[]? AllowedClients);

/// <summary>The answer of <c>GET /api/tenant/outside-clients</c>.</summary>
internal sealed record TenantOutsideClientsResponse(string OutsideClients, IReadOnlyList<string> AllowedClients);

/// <summary>The answer of <c>GET /api/tenant/connected-apps</c>.</summary>
internal sealed record TenantConnectedAppsResponse(IReadOnlyList<TenantConnectedApp> Items);

/// <summary>An outside MCP client with a consent. <c>Name</c> and <c>LastUsedAt</c> are <c>null</c> when the client has no name and was never used.</summary>
internal sealed record TenantConnectedApp(string ClientId, string? Name, DateTimeOffset? LastUsedAt, IReadOnlyList<TenantConnectedAppPerson> People);

/// <summary>A person who consented to an outside MCP client.</summary>
internal sealed record TenantConnectedAppPerson(string UserId, string DisplayName, string? Email, DateTimeOffset ConsentedAt);
