using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Axis.Administration;
using Axis.Configuration.Templates;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Axis.Server.CellApi;

/// <summary>
/// The routes of the cell's provisioning API under <c>/api/cell/</c>, see <c>docs/reference/tenant-lifecycle.md#provisioning-api</c>.
/// They answer only on a platform host, see <see cref="CellHosts"/>, and a tenant host answers <c>404</c> for every path under
/// the group. Every route of the group but sign-in and sign-out needs a bearer token of the operator realm, or the
/// session of an operator who signed in to the console, with the operator role, see <see cref="CellOperator"/> and
/// <see cref="CellOperatorSession"/>: <c>401</c> without a valid token or session, <c>403</c> without the role.
/// <para>
/// <c>GET /api/cell/auth/sign-in?returnUrl</c> sends the operator to the operator realm's login page, see
/// <see cref="CellConsoleSignIn"/>, and answers <c>404</c> when the console offers no sign-in or the host is not
/// <see cref="CellOptions.ConsoleHost"/>. <c>POST /api/cell/auth/sign-out</c> deletes the session and answers <c>204</c>.
/// The two skip the operator policy, as they must work for a caller who has no session. Writes need the CSRF header.
/// </para>
/// <para>
/// <c>GET /api/cell/tenants</c> answers <c>{ "items": [...] }</c>, ordered by id, with each tenant's <c>id</c>,
/// <c>status</c>, <c>application</c> and <c>hosts</c>. <c>GET /api/cell/tenants/{id}</c> answers one such tenant. Both read the
/// platform tables over the reader role. The <c>status</c> is <c>deleting</c> while the tenant has a scheduled deletion, whatever
/// its stored status, which stays <c>suspended</c>. A preview tenant belongs to its main tenant's Studio and is not a tenant of the
/// cell, so the list leaves it out and its id is a <c>404</c>. The <c>application</c> is <see langword="null"/> until the
/// create job records it. The one tenant also answers its <c>quotas</c>: each quota key with its effective <c>value</c> and
/// whether the tenant <c>overridden</c> it, worked out from its <c>platform.tenant_quotas</c> rows and the cell's defaults.
/// </para>
/// <para>
/// <c>PUT /api/cell/tenants/{id}/quotas</c> takes <c>{ "set": { "users": 100, "storage": null }, "remove": ["applications"] }</c>.
/// A set value of <see langword="null"/> is no limit and a removed key goes back to the cell's default. A key the request
/// does not name stays as it is. It applies at once with no job and answers <c>200</c> with the tenant's quotas. An unknown key,
/// a negative value, a key both set and removed, or a request that names no key answers <c>400</c> and writes nothing. It writes
/// over the schema role, see <see cref="CellPlatformWriter"/>, and saves the rows with one platform audit record, command
/// <c>tenant quotas set</c>, naming the keys of the request, or saves neither. The route passes no actor yet, so the record's
/// <c>actor</c> column stays empty.
/// </para>
/// <para>
/// <c>POST /api/cell/tenants/{id}/suspend</c> and <c>/resume</c>, <c>DELETE /api/cell/tenants/{id}</c> and
/// <c>POST /api/cell/tenants/{id}/deletion/cancel</c> queue a job in <c>platform.jobs</c> and answer <c>202</c> with
/// <c>{ id, kind, tenantId, state, createdAt }</c> and a <c>Location</c> header of the job. The publisher's job runner changes the
/// tenant's status later. The jobs insert over the reader role. The job's <c>actor</c> is the operator's id, the subject of the
/// token or the session. An unknown tenant or a preview tenant answers <c>404</c>. Then the <c>Idempotency-Key</c> header, one
/// value of 1 to 255 characters, is needed: <c>400</c> without it, and no job is queued. For 24 hours the same key with the same
/// kind, tenant and body, hashed with SHA-256, answers the first job again, and the same key with any other request answers
/// <c>422</c>. The key is shared across the cell. A transaction lock on the key serializes parallel retries, so they queue one job.
/// <c>GET /api/cell/jobs/{id}</c> answers the job with its <c>steps</c> and the <c>error</c> of a failed job, or <c>404</c>.
/// <c>POST /api/cell/tenants/{id}/exports</c> queues an <c>export</c> job with the one step <c>export</c> in the same way.
/// </para>
/// <para>
/// <c>GET /api/cell/tenants/{id}/exports/{exportId}</c> answers the export as <c>{ id, tenantId, state, createdAt, expiresAt }</c>, where
/// <c>expiresAt</c> is <see cref="TenantExports.Kept"/> after the job finished and <see langword="null"/> until it succeeded. With
/// <c>Accept: application/zip</c> it streams the stored ZIP instead, after it wrote one platform audit record, command
/// <c>tenant export download</c>, with the operator as actor and <c>export:&lt;id&gt;</c> in <c>changed</c>. The server streams the ZIP
/// itself and never redirects to the storage. An unknown tenant, a preview tenant or an id that is no export of the tenant answers <c>404</c>,
/// an export past its expiry or whose ZIP the storage no longer holds <c>410</c>, a download before the job succeeded <c>409</c> and a
/// download on a server without the <c>Cell:ExportStorage</c> settings <c>503</c>. A refused call and a read of the JSON write no record.
/// </para>
/// <para>
/// <c>POST /api/cell/tenants</c> takes <c>{ "id", "application", "administrator" }</c> and queues a <c>create</c> job with the five
/// steps <c>tenant</c>, <c>application</c>, <c>activate</c>, <c>invite</c> and <c>resume</c>, all <c>pending</c>, and answers <c>202</c>
/// as the suspend route does. The checks run in this order: the <c>Idempotency-Key</c> header (<c>400</c>), the replay of the first job
/// or <c>422</c>, the body (<c>400</c>: the id by the rule of <c>tenant create</c>, an application that is not an installed template, an
/// administrator that is not an email), then under a cell-wide advisory lock an id that a directory row, an unfinished create job
/// or a tombstone of a deleted tenant holds (<c>409</c>) and the cell's <see cref="CellOptions.TenantCap"/> (<c>409</c>, "This cell is full."). The job's <c>input</c> holds the
/// application and the administrator and the job read never returns it.
/// </para>
/// <para>
/// <c>POST /api/cell/tenants/{id}/hosts</c> takes <c>{ "host" }</c>, a host of the tenant's own such as <c>approvals.acme.vn</c>, and
/// stores a pending request in <c>platform.tenant_host_requests</c>, with the host in lower case. It answers <c>201</c> with
/// <c>{ host, token, state, expiresAt, txt, cname }</c>: the token is 32 random bytes as base64url, <c>txt</c> is the record
/// <c>_axis-verify.&lt;host&gt;</c> that holds the token, <c>cname</c> points the host to <c>&lt;id&gt;.&lt;Cell:TenantDomain&gt;</c>, and the
/// request expires 7 days later. A repeat request from the same tenant for the same pending host answers <c>200</c> with the same
/// token. A blank host or one that is not a DNS name of at least two labels answers <c>400</c>, an unknown tenant or a preview tenant
/// <c>404</c>, and a host that <c>platform.tenant_hosts</c> holds for any tenant <c>409</c>, whose title names no tenant. A host that a tenant deleted less than <see cref="DeletedTenantHolds.HostHoldBackDays"/> days
/// ago had answers <c>409</c> too, with a title that names no tenant. A transaction
/// lock on the tenant and host serializes parallel requests. A pending request does not hold the host, so another tenant may request it
/// and the host still answers <c>404</c> until a later step verifies it. The insert goes over the reader role. The request's <c>actor</c>
/// is the operator's id. The route queues no job and takes no <c>Idempotency-Key</c>.
/// </para>
/// </summary>
internal static class CellEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private const int MaxIdempotencyKeyLength = 255;

    private static readonly TimeSpan _idempotencyWindow = TimeSpan.FromHours(24);

    // The steps of each kind, as the publisher's job runner defines them, see ProvisioningJobs.Builtin.
    private static readonly Dictionary<string, string[]> _jobSteps = new(StringComparer.Ordinal)
    {
        ["suspend"] = ["status"],
        ["resume"] = ["status"],
        ["delete"] = ["schedule"],
        ["cancel-deletion"] = ["cancel"],
        ["create"] = ["tenant", "application", "activate", "invite", "resume"],
        ["export"] = ["export"],
    };

    private const int MaxTenantIdLength = 50;

    private const int MaxHostLength = 253;

    private const int MaxHostLabelLength = 63;

    private static readonly TimeSpan _hostRequestWindow = TimeSpan.FromDays(7);

    public static void MapCellEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var cell = endpoints.MapGroup("/api/cell")
            .WithMetadata(CellOperatorRoute.Instance)
            .RequireAuthorization(CellOperator.Policy);
        cell.MapGet("/tenants", ListTenantsAsync);
        cell.MapGet("/tenants/{id}", GetTenantAsync);
        cell.MapPut("/tenants/{id}/quotas", SetQuotasAsync);
        cell.MapPost("/tenants", CreateTenantAsync);
        cell.MapPost("/tenants/{id}/suspend", (string id, HttpContext http, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            QueueJobAsync("suspend", id, http, dataSource, cancellationToken));
        cell.MapPost("/tenants/{id}/resume", (string id, HttpContext http, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            QueueJobAsync("resume", id, http, dataSource, cancellationToken));
        cell.MapDelete("/tenants/{id}", (string id, HttpContext http, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            QueueJobAsync("delete", id, http, dataSource, cancellationToken));
        cell.MapPost("/tenants/{id}/deletion/cancel", (string id, HttpContext http, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            QueueJobAsync("cancel-deletion", id, http, dataSource, cancellationToken));
        cell.MapPost("/tenants/{id}/exports", (string id, HttpContext http, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            QueueJobAsync("export", id, http, dataSource, cancellationToken));
        cell.MapPost("/tenants/{id}/hosts", RequestHostAsync);
        cell.MapGet("/tenants/{id}/exports/{exportId:guid}", GetExportAsync);
        cell.MapGet("/jobs/{id:guid}", GetJobAsync);

        // Sign-in and sign-out are the only routes that skip the operator policy. They keep the route marker, so the console
        // host serves them and tenant resolution has no second way through.
        var auth = endpoints.MapGroup("/api/cell/auth")
            .WithMetadata(CellOperatorRoute.Instance)
            .AllowAnonymous();
        auth.MapGet("/sign-in", SignInAsync).WithRateLimit(Partition.SignIn);
        // The cast makes the handler a route handler, whose result is written, and not a RequestDelegate.
        auth.MapPost("/sign-out", (Delegate)SignOutAsync);
    }

    // Only a local path is a return address, so sign-in can never send the browser to another site.
    private static async Task<IResult> SignInAsync(
        HttpContext httpContext,
        string? returnUrl,
        CellOptions cell,
        IAuthenticationSchemeProvider schemes)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            returnUrl = "/";
        }
        else if (!returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal) || returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The return address must be a local path.");
        }

        if (await schemes.GetSchemeAsync(CellConsoleSignIn.Scheme) is null || !CellOperatorSession.IsConsoleHost(httpContext, cell.ConsoleHost))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This host offers no console sign-in.");
        }

        return Results.Challenge(new AuthenticationProperties { RedirectUri = returnUrl }, [CellConsoleSignIn.Scheme]);
    }

    private static async Task<IResult> SignOutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CellOperatorSession.Scheme);
        return Results.NoContent();
    }

    private static async Task<IResult> ListTenantsAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var platform = Open(dataSource);
        var tenants = await platform.Tenants.AsNoTracking()
            .Where(tenant => tenant.PreviewOf == null)
            .Select(tenant => new { tenant.Id, Status = tenant.DeleteAfter != null ? "deleting" : tenant.Status })
            .ToListAsync(cancellationToken);
        var hosts = await ReadHostsAsync(platform, tenants.Select(tenant => tenant.Id).ToList(), cancellationToken);

        return Results.Ok(new CellTenantListResponse(
            [.. tenants
                .OrderBy(tenant => tenant.Id, StringComparer.Ordinal)
                .Select(tenant => new CellTenantResponse(tenant.Id, tenant.Status, null, HostsOf(hosts, tenant.Id)))]));
    }

    private static async Task<IResult> GetTenantAsync(
        string id,
        NpgsqlDataSource dataSource,
        QuotaDefaults defaults,
        CancellationToken cancellationToken)
    {
        await using var platform = Open(dataSource);
        var tenant = await platform.Tenants.AsNoTracking()
            .Where(row => row.Id == id && row.PreviewOf == null)
            .Select(row => new { row.Id, Status = row.DeleteAfter != null ? "deleting" : row.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (tenant is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No tenant has this id.");
        }

        var hosts = await ReadHostsAsync(platform, [tenant.Id], cancellationToken);
        var quotas = await ReadQuotasAsync(platform, tenant.Id, defaults, cancellationToken);
        return Results.Ok(new CellTenantDetailResponse(tenant.Id, tenant.Status, null, HostsOf(hosts, tenant.Id), quotas));
    }

    private static async Task<IResult> SetQuotasAsync(
        string id,
        CellQuotasRequest? request,
        CellPlatformWriter writer,
        QuotaDefaults defaults,
        CancellationToken cancellationToken)
    {
        var set = request?.Set ?? [];
        var remove = request?.Remove ?? [];
        var problem = ValidateQuotas(request, set, remove);
        if (problem is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: problem);
        }

        var named = set.Keys.Concat(remove).ToHashSet(StringComparer.Ordinal);
        await using var platform = writer.Open();
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);
        if (!await platform.Tenants.AnyAsync(row => row.Id == id && row.PreviewOf == null, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No tenant has this id.");
        }

        foreach (var (quota, value) in set)
        {
            await platform.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO platform.tenant_quotas (tenant_id, quota, value, updated_at)
                VALUES ({id}, {quota}, CAST({value} AS bigint), now())
                ON CONFLICT (tenant_id, quota) DO UPDATE SET value = EXCLUDED.value, updated_at = EXCLUDED.updated_at
                """,
                cancellationToken);
        }

        if (remove.Count > 0)
        {
            await platform.TenantQuotas
                .Where(row => row.TenantId == id && remove.Contains(row.Quota))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await PlatformAuditRecords.WriteAsync(
            platform, "tenant quotas set", id, [.. QuotaKeys.All.Where(named.Contains)], cancellationToken);
        var quotas = await ReadQuotasAsync(platform, id, defaults, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new CellQuotasResponse(quotas));
    }

    private static async Task<IResult> QueueJobAsync(
        string kind,
        string id,
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        await using var platform = Open(dataSource);
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);
        if (!await platform.Tenants.AnyAsync(row => row.Id == id && row.PreviewOf == null, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No tenant has this id.");
        }

        if (ReadIdempotencyKey(httpContext) is not { } key)
        {
            return MissingKey();
        }

        var hash = HashRequest(kind, id, await ReadBodyAsync(httpContext, cancellationToken));
        if (await ReplayAsync(platform, transaction, key, hash, cancellationToken) is { } replay)
        {
            return replay;
        }

        var job = NewJob(kind, id, httpContext, key, hash, null);
        platform.Jobs.Add(job);
        await platform.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Accepted(job);
    }

    [SuppressMessage("Performance", "CA1862:Use the 'StringComparison' method overloads", Justification = "The query must translate to lower(host) to use the unique index on lower(host).")]
    private static async Task<IResult> RequestHostAsync(
        string id,
        CellHostRequest? request,
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        CellOptions cell,
        CancellationToken cancellationToken)
    {
        var trimmed = request?.Host?.Trim();
        if (trimmed is null || !IsValidHost(trimmed.ToLowerInvariant()))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The host must be a DNS name such as approvals.example.com.");
        }

        // Hosts are ASCII, so lower() in the database matches this lower case.
        var host = trimmed.ToLowerInvariant();
        await using var platform = Open(dataSource);
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);

        // The lock keeps two parallel retries from inserting two pending requests with different tokens.
        await platform.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended('axis-host-request:' || {id} || ':' || {host}, 0))",
            cancellationToken);
        if (!await platform.Tenants.AnyAsync(row => row.Id == id && row.PreviewOf == null, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No tenant has this id.");
        }

        // The title names neither the host nor the tenant that holds it.
        if (await platform.TenantHosts.AnyAsync(row => row.Host.ToLower() == host, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This host already belongs to a tenant.");
        }

        // The title names neither the host nor the deleted tenant that held it.
        if (await DeletedTenantHolds.IsHostHeldBackAsync(platform, [host], cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This host is held back after a tenant was deleted.");
        }

        // A request whose expiry has passed is not pending, although its state says so until a later step marks it expired.
        var now = DateTimeOffset.UtcNow;
        var pending = await platform.TenantHostRequests.AsNoTracking()
            .Where(row => row.TenantId == id && row.Host == host && row.State == "pending" && row.ExpiresAt > now)
            .OrderByDescending(row => row.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (pending is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(HostResponse(pending, cell));
        }

        var created = new TenantHostRequestRow
        {
            TenantId = id,
            Host = host,
            Token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)),
            State = "pending",
            RequestedAt = now,
            ExpiresAt = now + _hostRequestWindow,
            Actor = httpContext.User.FindFirst(CellOperator.IdClaim)!.Value,
        };
        platform.TenantHostRequests.Add(created);
        await platform.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Json(HostResponse(created, cell), statusCode: StatusCodes.Status201Created);
    }

    private static CellHostResponse HostResponse(TenantHostRequestRow request, CellOptions cell) =>
        new(
            request.Host,
            request.Token,
            request.State,
            request.ExpiresAt,
            new CellDnsRecord("_axis-verify." + request.Host, request.Token),
            new CellCnameRecord(request.Host, $"{request.TenantId}.{cell.TenantDomain!.Trim()}"));

    // A host of a tenant's own must be a public DNS name that can carry a CNAME, so it has at least two labels and is no IP address.
    private static bool IsValidHost(string host)
    {
        if (host.Length is 0 or > MaxHostLength || Uri.CheckHostName(host) != UriHostNameType.Dns)
        {
            return false;
        }

        var labels = host.Split('.');
        return labels.Length >= 2 && labels.All(IsValidHostLabel);
    }

    private static bool IsValidHostLabel(string label) =>
        label.Length is >= 1 and <= MaxHostLabelLength
        && label[0] != '-'
        && label[^1] != '-'
        && label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    private static async Task<IResult> CreateTenantAsync(
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        TemplateCatalog templates,
        CellOptions cell,
        CancellationToken cancellationToken)
    {
        if (ReadIdempotencyKey(httpContext) is not { } key)
        {
            return MissingKey();
        }

        var body = await ReadBodyAsync(httpContext, cancellationToken);
        var hash = HashRequest("create", "", body);
        await using var platform = Open(dataSource);
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);

        // The replay comes before every check that a later change can flip, such as the tenant it created or a removed template.
        if (await ReplayAsync(platform, transaction, key, hash, cancellationToken) is { } replay)
        {
            return replay;
        }

        var request = ReadCreateRequest(body);
        var problem = ValidateCreate(request, templates);
        if (problem is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: problem);
        }

        var id = request!.Id!;

        // One lock for the whole cell covers the id check and the cap check, so two creates cannot both pass. It comes after the
        // key lock in every request, which is one fixed order.
        await platform.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended('axis-tenant-create', 0))",
            cancellationToken);
        var unfinished = platform.Jobs.AsNoTracking().Where(job => job.Kind == "create" && (job.State == "queued" || job.State == "running"));
        if (await platform.Tenants.AnyAsync(tenant => tenant.Id == id, cancellationToken)
            || await unfinished.AnyAsync(job => job.TenantId == id, cancellationToken)
            || await DeletedTenantHolds.IsDeletedIdAsync(platform, id, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "A tenant of the cell already has this id.");
        }

        if (cell.TenantCap is { } cap)
        {
            var count = await platform.Tenants.CountAsync(cancellationToken)
                + await unfinished.CountAsync(job => !platform.Tenants.Any(tenant => tenant.Id == job.TenantId), cancellationToken);
            if (count >= cap)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This cell is full.");
            }
        }

        var input = JsonSerializer.Serialize(new { request.Application, request.Administrator }, JsonSerializerOptions.Web);
        var job = NewJob("create", id, httpContext, key, hash, input);
        platform.Jobs.Add(job);
        await platform.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Accepted(job);
    }

    private static CellCreateTenantRequest? ReadCreateRequest(byte[] body)
    {
        try
        {
            return JsonSerializer.Deserialize<CellCreateTenantRequest>(body, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ValidateCreate(CellCreateTenantRequest? request, TemplateCatalog templates)
    {
        if (request is null)
        {
            return "The request must be a JSON object with an id, an application and an administrator.";
        }

        if (request.Id is not { } id
            || !TenantOptionsValidator.IsValidTenantId(id)
            || id.Length > MaxTenantIdLength
            || id == "master")
        {
            return $"The tenant id must have 1 to {MaxTenantIdLength} lowercase letters, digits and hyphens, and must not be 'master'.";
        }

        if (request.Application is not { } application
            || !templates.Newest.Any(template => string.Equals(template.Manifest.Id, application, StringComparison.Ordinal)))
        {
            return "The application must be the id of an installed application template.";
        }

        return request.Administrator is { } administrator
            && MailAddress.TryCreate(administrator, out var address)
            && address.Address == administrator
            ? null
            : "The administrator must be an email address.";
    }

    private static string? ReadIdempotencyKey(HttpContext httpContext)
    {
        var values = httpContext.Request.Headers[IdempotencyKeyHeader];
        return values.Count != 1 || string.IsNullOrWhiteSpace(values[0]) || values[0]!.Length > MaxIdempotencyKeyLength
            ? null
            : values[0];
    }

    private static IResult MissingKey() =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: $"The request must carry an {IdempotencyKeyHeader} header of 1 to {MaxIdempotencyKeyLength} characters.");

    private static async Task<byte[]> ReadBodyAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(body, cancellationToken);
        return body.ToArray();
    }

    // The tenant segment is empty for a request that names no tenant in its path, such as create.
    private static string HashRequest(string kind, string tenantSegment, byte[] body) =>
        Convert.ToHexStringLower(SHA256.HashData([.. Encoding.UTF8.GetBytes($"{kind}\n{tenantSegment}\n"), .. body]));

    // Answers the first job again, or 422 when the key was used with another request, or null when the key is new.
    private static async Task<IResult?> ReplayAsync(
        PlatformDbContext platform,
        IDbContextTransaction transaction,
        string key,
        string hash,
        CancellationToken cancellationToken)
    {
        // The key index cannot be unique, as a key may be used again after the window, so the lock keeps two parallel retries from both inserting.
        await platform.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended('axis-job:' || {key}, 0))",
            cancellationToken);
        var windowStart = DateTimeOffset.UtcNow - _idempotencyWindow;
        var first = await platform.Jobs.AsNoTracking()
            .Where(job => job.IdempotencyKey == key && job.CreatedAt > windowStart)
            .OrderByDescending(job => job.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (first is null)
        {
            return null;
        }

        if (first.RequestHash != hash)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: $"The {IdempotencyKeyHeader} was used before with another request.");
        }

        await transaction.CommitAsync(cancellationToken);
        return Accepted(first);
    }

    private static JobRow NewJob(string kind, string tenantId, HttpContext httpContext, string key, string hash, string? input) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            TenantId = tenantId,
            State = "queued",
            Steps = JsonSerializer.Serialize(_jobSteps[kind].Select(name => new CellJobStepResponse(name, "pending", null)), JsonSerializerOptions.Web),
            Input = input,
            Actor = httpContext.User.FindFirst(CellOperator.IdClaim)!.Value,
            IdempotencyKey = key,
            RequestHash = hash,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static IResult Accepted(JobRow job) =>
        Results.Accepted($"/api/cell/jobs/{job.Id:D}", new CellJobResponse(job.Id, job.Kind, job.TenantId, job.State, job.CreatedAt));

    private static async Task<IResult> GetExportAsync(
        string id,
        Guid exportId,
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        CellPlatformWriter writer,
        CellExportStorage storage,
        CancellationToken cancellationToken)
    {
        JobRow? job;
        await using (var platform = Open(dataSource))
        {
            if (!await platform.Tenants.AsNoTracking().AnyAsync(row => row.Id == id && row.PreviewOf == null, cancellationToken))
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No tenant has this id.");
            }

            job = await platform.Jobs.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == exportId && row.Kind == "export" && row.TenantId == id, cancellationToken);
        }

        if (job is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No export of this tenant has this id.");
        }

        var succeeded = job.State == "succeeded";
        DateTimeOffset? expiresAt = succeeded && job.FinishedAt is { } finished ? finished + TenantExports.Kept : null;
        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            return Gone();
        }

        if (!httpContext.Request.GetTypedHeaders().Accept.Any(accept => accept.MediaType.Equals("application/zip", StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Ok(new CellExportResponse(job.Id, job.TenantId, job.State, job.CreatedAt, expiresAt));
        }

        if (!succeeded)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The export is not ready.");
        }

        if (storage.Storage is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The export storage is not configured.");
        }

        var client = storage.Storage.NewClient();
        httpContext.Response.RegisterForDispose(client);
        Amazon.S3.Model.GetObjectResponse stored;
        try
        {
            stored = await client.GetObjectAsync(storage.Storage.Bucket, TenantExports.Key(id, exportId), cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound || exception.ErrorCode == "NoSuchKey")
        {
            return Gone();
        }

        httpContext.Response.RegisterForDispose(stored);

        // The record comes after the storage returned the object and before the first byte, so a download that breaks half-way is
        // still recorded, and a download that finds no ZIP is not.
        await using (var platform = writer.Open())
        {
            await PlatformAuditRecords.WriteAsync(
                platform,
                TenantExports.DownloadCommand,
                id,
                [$"export:{exportId:D}"],
                httpContext.User.FindFirst(CellOperator.IdClaim)!.Value,
                cancellationToken);
        }

        return Results.Stream(stored.ResponseStream, "application/zip", $"{id}-{exportId:D}.zip");

        static IResult Gone() =>
            Results.Problem(statusCode: StatusCodes.Status410Gone, title: "The export was deleted after 7 days.");
    }

    private static async Task<IResult> GetJobAsync(Guid id, NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var platform = Open(dataSource);
        var job = await platform.Jobs.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (job is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No job has this id.");
        }

        var steps = JsonSerializer.Deserialize<List<CellJobStepResponse>>(job.Steps, JsonSerializerOptions.Web) ?? [];
        return Results.Ok(new CellJobDetailResponse(job.Id, job.Kind, job.TenantId, job.State, job.CreatedAt, steps, job.Error));
    }

    private static string? ValidateQuotas(CellQuotasRequest? request, Dictionary<string, long?> set, List<string> remove)
    {
        if (request is null || set.Count + remove.Count == 0)
        {
            return "The request must set or remove at least one quota.";
        }

        var unknown = set.Keys.Concat(remove).Where(key => key is null || !QuotaKeys.All.Contains(key)).ToList();
        if (unknown.Count > 0)
        {
            return $"'{unknown[0]}' is not a quota. The quotas are {string.Join(", ", QuotaKeys.All)}.";
        }

        var negative = set.FirstOrDefault(entry => entry.Value < 0).Key;
        if (negative is not null)
        {
            return $"The value of '{negative}' must be zero or more, or null for no limit.";
        }

        var both = remove.FirstOrDefault(set.ContainsKey);
        return both is null ? null : $"'{both}' is both set and removed.";
    }

    private static async Task<Dictionary<string, CellQuotaResponse>> ReadQuotasAsync(
        PlatformDbContext platform,
        string tenantId,
        QuotaDefaults defaults,
        CancellationToken cancellationToken)
    {
        var rows = await platform.TenantQuotas.AsNoTracking()
            .Where(row => row.TenantId == tenantId)
            .ToDictionaryAsync(row => row.Quota, row => row.Value, StringComparer.Ordinal, cancellationToken);
        return QuotaKeys.All.ToDictionary(
            key => key,
            key => rows.TryGetValue(key, out var value) ? new CellQuotaResponse(value, true) : new CellQuotaResponse(defaults[key], false),
            StringComparer.Ordinal);
    }

    private static PlatformDbContext Open(NpgsqlDataSource dataSource) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(dataSource).Options);

    private static async Task<ILookup<string, string>> ReadHostsAsync(
        PlatformDbContext platform,
        List<string> tenantIds,
        CancellationToken cancellationToken)
    {
        var rows = await platform.TenantHosts.AsNoTracking()
            .Where(host => tenantIds.Contains(host.TenantId))
            .Select(host => new { host.TenantId, host.Host })
            .ToListAsync(cancellationToken);
        return rows.ToLookup(row => row.TenantId, row => row.Host, StringComparer.Ordinal);
    }

    private static string[] HostsOf(ILookup<string, string> hosts, string tenantId) =>
        [.. hosts[tenantId].Order(StringComparer.Ordinal)];
}

/// <summary>One tenant of the cell. <c>Application</c> is null until the create job records it.</summary>
internal sealed record CellTenantResponse(string Id, string Status, string? Application, IReadOnlyList<string> Hosts);

/// <summary>The tenants of the cell, ordered by id.</summary>
internal sealed record CellTenantListResponse(IReadOnlyList<CellTenantResponse> Items);

/// <summary>One tenant of the cell with its effective quotas, keyed by quota key.</summary>
internal sealed record CellTenantDetailResponse(
    string Id,
    string Status,
    string? Application,
    IReadOnlyList<string> Hosts,
    IReadOnlyDictionary<string, CellQuotaResponse> Quotas);

/// <summary>The body of the quotas write: the quotas to set, where <c>null</c> is no limit, and the quotas to take back to the default.</summary>
internal sealed record CellQuotasRequest(Dictionary<string, long?>? Set, List<string>? Remove);

/// <summary>One effective quota. <c>Value</c> is null for no limit. <c>Overridden</c> is false when the cell's default applies.</summary>
internal sealed record CellQuotaResponse(long? Value, bool Overridden);

/// <summary>The effective quotas of a tenant, keyed by quota key.</summary>
internal sealed record CellQuotasResponse(IReadOnlyDictionary<string, CellQuotaResponse> Quotas);

/// <summary>The body of the create route. A missing or wrongly typed member is null and answers <c>400</c>.</summary>
internal sealed record CellCreateTenantRequest(string? Id, string? Application, string? Administrator);

/// <summary>The body of the host request route. A missing or wrongly typed host is null and answers <c>400</c>.</summary>
internal sealed record CellHostRequest(string? Host);

/// <summary>A pending request for a host of a tenant's own, with the DNS records the customer must add.</summary>
internal sealed record CellHostResponse(
    string Host,
    string Token,
    string State,
    DateTimeOffset ExpiresAt,
    CellDnsRecord Txt,
    CellCnameRecord Cname);

/// <summary>A <c>TXT</c> record to add.</summary>
internal sealed record CellDnsRecord(string Name, string Value);

/// <summary>A <c>CNAME</c> record to add: the host <c>Name</c> points to <c>Target</c>.</summary>
internal sealed record CellCnameRecord(string Name, string Target);

/// <summary>A queued job of the provisioning API.</summary>
internal sealed record CellJobResponse(Guid Id, string Kind, string TenantId, string State, DateTimeOffset CreatedAt);

/// <summary>One job with its steps, in the order they run, and the <c>Error</c> of a failed job.</summary>
internal sealed record CellJobDetailResponse(
    Guid Id,
    string Kind,
    string TenantId,
    string State,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CellJobStepResponse> Steps,
    string? Error);

/// <summary>One step of a job. <c>Error</c> is the refusal of a failed step.</summary>
internal sealed record CellJobStepResponse(string Name, string State, string? Error);

/// <summary>A tenant export. <c>ExpiresAt</c> is null until the export job succeeds.</summary>
internal sealed record CellExportResponse(Guid Id, string TenantId, string State, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);
