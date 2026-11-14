using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Core.Tenancy;
using Axis.Data.Audit;
using Axis.Data.Storage;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Authentication;
using Npgsql;

namespace Axis.Server.CellApi;

/// <summary>
/// The routes of a tenant administrator's package runners on the tenant's own host, see
/// <c>docs/reference/user-administration.md#package-runners</c>. They check, in order, a session (<c>401</c>), the administrator role
/// (<c>403</c>) and a tenant with a directory row that is not a preview tenant and has a users client (<c>404</c>), and for <c>POST</c>
/// then the body (<c>400</c>). A create also needs a sign-in at the identity provider in the last 5 minutes (<c>403</c>).
/// <para>
/// <c>POST /api/tenant/package-runners</c> makes the runner's secret, creates the runner's confidential client in the tenant's realm and
/// then stores the runner and its audit record in one transaction. The secret is in the answer only. A failure at the identity provider is a
/// <c>502</c> with a fixed title and stores nothing. When the store fails after the client was made, the client is deleted again.
/// </para>
/// <para>
/// <c>POST /api/tenant/package-runners/{id}/revoke</c> stores the revoke time and the audit record in one transaction, and then deletes the
/// client. A failure at the identity provider is a <c>502</c> with a fixed title, the stored revoke stays and the same <c>POST</c> again
/// deletes the client. A repeat keeps the first revoke time and writes no second record.
/// </para>
/// </summary>
internal static partial class TenantPackageRunnersEndpoints
{
    private const int MaxNameLength = 100;

    public static void MapTenantPackageRunnersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenant/package-runners", ListAsync);
        endpoints.MapPost("/api/tenant/package-runners", CreateAsync);
        endpoints.MapPost("/api/tenant/package-runners/{id}/revoke", RevokeAsync);
    }

    private static async Task<IResult> ListAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is null)
        {
            return refusal!;
        }

        var connection = await httpContext.RequestServices.GetRequiredService<TenantDatabase>().GetConnectionAsync(cancellationToken);
        var runners = await PackageRunners.ListActiveAsync(connection, cancellationToken);
        return Results.Ok(new PackageRunnersResponse(
            [
                .. runners.Select(runner => new PackageRunnerItem(
                    runner.Id,
                    runner.Name,
                    runner.ClientId,
                    [.. runner.Packages.Select(package => new PackageRunnerPackageItem(package.Id, package.Version))],
                    runner.LastCallAt,
                    runner.CreatedAt)),
            ]));
    }

    private static async Task<IResult> CreateAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is not { } open)
        {
            return refusal!;
        }

        PackageRunnerRequest? body = null;
        if (httpContext.Request.HasJsonContentType())
        {
            try
            {
                body = await httpContext.Request.ReadFromJsonAsync<PackageRunnerRequest>(cancellationToken);
            }
            catch (JsonException)
            {
            }
        }

        var name = body?.Name?.Trim();
        if (name is not { Length: >= 1 and <= MaxNameLength })
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The runner needs a name of 1 to 100 characters.");
        }

        var services = httpContext.RequestServices;
        if (!Session.MayEndSessions(open.Principal, services.GetRequiredService<TimeProvider>().GetUtcNow()))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Log in again at the identity provider to create a package runner.");
        }

        var logger = services.GetRequiredService<ILogger<UserLog>>();
        var id = Guid.CreateVersion7();
        var clientId = RealmPackageRunnerClient.ClientId(id);
        var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TenantPackageRunnersEndpoints));
        var realm = new RealmSync(client, open.Realm.BaseAddress);
        try
        {
            await realm.CreatePackageRunnerClientAsync(open.Realm.Name, open.Realm.ActivationClientSecret.Reveal(), clientId, secret, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            LogCreateFailed(logger, open.TenantId, exception.Message);
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The identity provider could not create the runner.");
        }

        try
        {
            var connection = await services.GetRequiredService<TenantDatabase>().GetConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await PackageRunners.InsertAsync(transaction, id, name, clientId, cancellationToken);
            await AuditRecords.AppendAsync(
                transaction,
                new AuditEntry(open.CallerId, AuditActions.PackageRunnerCreated, null, null, null, null, Details(id, clientId)),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // A client that no runner row points to would be a working credential nobody can see, so it goes again.
            try
            {
                await realm.DeletePackageRunnerClientAsync(open.Realm.Name, open.Realm.ActivationClientSecret.Reveal(), clientId, CancellationToken.None);
            }
            catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
            {
                LogCleanUpFailed(logger, open.TenantId, exception.Message);
            }

            throw;
        }

        return Results.Created($"/api/tenant/package-runners/{id}", new PackageRunnerCreatedResponse(id, clientId, secret));
    }

    private static async Task<IResult> RevokeAsync(string id, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is not { } open)
        {
            return refusal!;
        }

        var services = httpContext.RequestServices;
        var connection = await services.GetRequiredService<TenantDatabase>().GetConnectionAsync(cancellationToken);
        if (!Guid.TryParse(id, out var runnerId) || await PackageRunners.FindAsync(connection, runnerId, cancellationToken) is not { } runner)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This tenant has no package runner with this id.");
        }

        // The revoke and its record commit before the client goes, so a failed delete never leaves a revoke without a record.
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            if (await PackageRunners.RevokeAsync(transaction, runnerId, cancellationToken))
            {
                await AuditRecords.AppendAsync(
                    transaction,
                    new AuditEntry(open.CallerId, AuditActions.PackageRunnerRevoked, null, null, null, null, Details(runnerId, runner.ClientId)),
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        // The stored revoke stays when the delete fails, and the same POST again finishes the job.
        try
        {
            using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TenantPackageRunnersEndpoints));
            await new RealmSync(client, open.Realm.BaseAddress)
                .DeletePackageRunnerClientAsync(open.Realm.Name, open.Realm.ActivationClientSecret.Reveal(), runner.ClientId, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            LogRevokeFailed(services.GetRequiredService<ILogger<UserLog>>(), open.TenantId, exception.Message);
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The identity provider could not delete the runner's client.");
        }

        return Results.NoContent();
    }

    private static JsonObject Details(Guid id, string clientId) => new() { ["id"] = id.ToString(), ["clientId"] = clientId };

    private static IResult NoRunners() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This tenant has no package runners.");

    // A signed-in user from the session only, because a service account's bearer token has no right here (401), the administrator
    // role (403) and a tenant with a directory row that is not a preview tenant and has a users client (404).
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
            || services.GetRequiredService<ITenantCatalog>().Find(tenant.TenantId) is not { PreviewOf: null, Realm: { } realm, RealmUsers: not null })
        {
            return (null, NoRunners());
        }

        return (new Opening(tenant.TenantId, caller.Id, authenticated.Principal, realm), null);
    }

    [LoggerMessage(LogLevel.Warning, "The realm of tenant {TenantId} could not create a package runner's client. {Reason}")]
    private static partial void LogCreateFailed(ILogger logger, string tenantId, string reason);

    [LoggerMessage(LogLevel.Warning, "The realm of tenant {TenantId} could not delete the client of a package runner that was not stored. {Reason}")]
    private static partial void LogCleanUpFailed(ILogger logger, string tenantId, string reason);

    [LoggerMessage(LogLevel.Warning, "The realm of tenant {TenantId} could not delete a revoked package runner's client. {Reason}")]
    private static partial void LogRevokeFailed(ILogger logger, string tenantId, string reason);

    private sealed record Opening(string TenantId, string CallerId, ClaimsPrincipal Principal, TenantRealmAccess Realm);
}

/// <summary>The body of <c>POST /api/tenant/package-runners</c>.</summary>
internal sealed record PackageRunnerRequest(string? Name);

/// <summary>The answer of <c>GET /api/tenant/package-runners</c>.</summary>
internal sealed record PackageRunnersResponse(IReadOnlyList<PackageRunnerItem> Items);

/// <summary>A runner that is not revoked. <c>LastCallAt</c> is <c>null</c> before the runner's first take of a call.</summary>
internal sealed record PackageRunnerItem(
    Guid Id,
    string Name,
    string ClientId,
    IReadOnlyList<PackageRunnerPackageItem> Packages,
    DateTimeOffset? LastCallAt,
    DateTimeOffset CreatedAt);

/// <summary>A package a runner registered.</summary>
internal sealed record PackageRunnerPackageItem(string Id, string Version);

/// <summary>The answer of <c>POST /api/tenant/package-runners</c>. It is the only answer that holds the secret.</summary>
internal sealed record PackageRunnerCreatedResponse(Guid Id, string ClientId, string ClientSecret);
