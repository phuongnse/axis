using System.Text.Json;
using Axis.Administration;
using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Users;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Server.CellApi;

/// <summary>
/// The routes of a tenant administrator's outside MCP client switch on the tenant's own host, see
/// <c>docs/reference/user-administration.md#outside-mcp-clients</c>. They check, in order, a session (<c>401</c>), the administrator
/// role (<c>403</c>) and a tenant with a directory row that is not a preview tenant and has realm details (<c>404</c>), and for
/// <c>PUT</c> then the body (<c>400</c>).
/// <para>
/// <c>PUT</c> replaces <c>Ai:OutsideClients</c> and <c>Ai:AllowedClients</c> with a platform audit record in the same transaction
/// that names <c>outsideClients</c> and <c>allowedClients</c> and never holds a client id, and then brings the realm in step. A
/// failure at the identity provider is a <c>502</c> with a fixed title, and the stored change and its record stay. <c>GET</c>
/// answers both stored values.
/// </para>
/// </summary>
internal static partial class TenantOutsideClientsEndpoints
{
    internal const string SetCommand = "tenant outside-clients set";

    public static void MapTenantOutsideClientsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenant/outside-clients", ReadAsync);
        endpoints.MapPut("/api/tenant/outside-clients", SetAsync);
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
            || services.GetRequiredService<ITenantCatalog>().Find(tenant.TenantId) is not { PreviewOf: null, Realm: { } realm })
        {
            return (null, NoSetting());
        }

        return (new Opening(tenant.TenantId, caller.Id, realm), null);
    }

    [LoggerMessage(LogLevel.Warning, "The realm of tenant {TenantId} could not apply the outside clients setting. {Reason}")]
    private static partial void LogRealmFailed(ILogger logger, string tenantId, string reason);

    private sealed record Opening(string TenantId, string CallerId, TenantRealmAccess Realm);
}

/// <summary>The body of <c>PUT /api/tenant/outside-clients</c>.</summary>
internal sealed record TenantOutsideClientsRequest(string? OutsideClients, string?[]? AllowedClients);

/// <summary>The answer of <c>GET /api/tenant/outside-clients</c>.</summary>
internal sealed record TenantOutsideClientsResponse(string OutsideClients, IReadOnlyList<string> AllowedClients);
