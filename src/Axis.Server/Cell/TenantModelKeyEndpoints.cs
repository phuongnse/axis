using System.Text.Json;
using Axis.Administration;
using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Users;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Server.CellApi;

/// <summary>
/// The routes of a tenant administrator's own model key on the tenant's own host, see
/// <c>docs/reference/user-administration.md#the-tenants-model-key</c>. They check, in order, a session (<c>401</c>), the
/// administrator role (<c>403</c>), <c>Ai:AllowTenantKeys</c> (<c>403</c>) and a tenant with a directory row that is not a preview
/// tenant (<c>404</c>).
/// <para>
/// <c>PUT</c> stores the trimmed key as the encrypted tenant secret <c>model_key</c> and <c>DELETE</c> removes it, both with a
/// platform audit record in the same transaction that names <c>modelKey</c> and never holds the key. <c>GET</c> answers whether
/// a key is set and its last four characters, never the whole key.
/// </para>
/// </summary>
internal static class TenantModelKeyEndpoints
{
    internal const string SetCommand = "tenant model-key set";

    internal const string RemoveCommand = "tenant model-key remove";

    /// <summary>How many characters of the key <c>GET</c> answers. A key this short or shorter answers none.</summary>
    private const int EndingLength = 4;

    public static void MapTenantModelKeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenant/model-key", ReadAsync);
        endpoints.MapPut("/api/tenant/model-key", SetAsync);
        endpoints.MapDelete("/api/tenant/model-key", RemoveAsync);
    }

    private static async Task<IResult> ReadAsync(
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        TenantSecretCipher cipher,
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
            .Select(row => new { row.ModelKey })
            .SingleOrDefaultAsync(cancellationToken);
        if (stored is null)
        {
            return NoModelKey();
        }

        if (stored.ModelKey is not { Length: > 0 } encrypted)
        {
            return Results.Ok(new TenantModelKeyResponse(false, null));
        }

        var plain = cipher.Decrypt(open.TenantId, TenantSecretFields.ModelKey, encrypted).Reveal();
        return Results.Ok(new TenantModelKeyResponse(true, plain.Length > EndingLength ? plain[^EndingLength..] : null));
    }

    private static async Task<IResult> SetAsync(
        HttpContext httpContext,
        CellPlatformWriter writer,
        TenantSecretCipher cipher,
        CancellationToken cancellationToken)
    {
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is not { } open)
        {
            return refusal!;
        }

        TenantModelKeyRequest? body = null;
        if (httpContext.Request.HasJsonContentType())
        {
            try
            {
                body = await httpContext.Request.ReadFromJsonAsync<TenantModelKeyRequest>(cancellationToken);
            }
            catch (JsonException)
            {
            }
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Key))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The request needs a model key.");
        }

        await using var platform = writer.Open();
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);
        var row = await FindAsync(platform, open.TenantId, cancellationToken);
        if (row is null)
        {
            return NoModelKey();
        }

        row.ModelKey = cipher.Encrypt(open.TenantId, TenantSecretFields.ModelKey, body.Key.Trim());
        row.Version++;
        await PlatformAuditRecords.WriteAsync(platform, SetCommand, open.TenantId, ["modelKey"], open.CallerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveAsync(
        HttpContext httpContext,
        CellPlatformWriter writer,
        CancellationToken cancellationToken)
    {
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is not { } open)
        {
            return refusal!;
        }

        await using var platform = writer.Open();
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);
        var row = await FindAsync(platform, open.TenantId, cancellationToken);
        if (row is null)
        {
            return NoModelKey();
        }

        // Removing a key that is not there changes nothing, so it writes no record.
        if (row.ModelKey is null)
        {
            return Results.NoContent();
        }

        row.ModelKey = null;
        row.Version++;
        await PlatformAuditRecords.WriteAsync(platform, RemoveCommand, open.TenantId, ["modelKey"], open.CallerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static Task<TenantRow?> FindAsync(PlatformDbContext platform, string tenantId, CancellationToken cancellationToken) =>
        platform.Tenants.SingleOrDefaultAsync(row => row.Id == tenantId && row.PreviewOf == null, cancellationToken);

    private static IResult NoModelKey() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This tenant has no model key.");

    // A signed-in user from the session only, because a service account's bearer token has no right here (401), the administrator
    // role (403), the setting (403) and a tenant with a directory row that is not a preview tenant (404).
    private static async Task<(Opening? Opening, IResult? Refusal)> OpenAsync(HttpContext httpContext)
    {
        var services = httpContext.RequestServices;
        var authenticated = await httpContext.AuthenticateAsync(Session.Scheme);
        if (!authenticated.Succeeded || Session.Read(authenticated.Principal) is not { } caller)
        {
            return (null, Grants.SignInRequired());
        }

        if (!services.GetRequiredService<UsersSettings>().Allows(caller.Roles)
            || !services.GetRequiredService<TenantModelKeySettings>().Allowed)
        {
            return (null, Grants.Forbidden());
        }

        if (services.GetRequiredService<ITenantContextAccessor>().Current is not { } tenant
            || services.GetRequiredService<ITenantCatalog>().Find(tenant.TenantId) is not { PreviewOf: null })
        {
            return (null, NoModelKey());
        }

        return (new Opening(tenant.TenantId, caller.Id), null);
    }

    private sealed record Opening(string TenantId, string CallerId);
}

/// <summary>The body of <c>PUT /api/tenant/model-key</c>.</summary>
internal sealed record TenantModelKeyRequest(string? Key);

/// <summary>The answer of <c>GET /api/tenant/model-key</c>. <paramref name="Ending"/> is the last four characters, or <c>null</c>.</summary>
internal sealed record TenantModelKeyResponse(bool Set, string? Ending);
