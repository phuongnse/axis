using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.S3;
using Axis.Administration;
using Axis.Configuration.Templates;
using Axis.Data.Storage;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

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
/// <c>GET /api/cell/session</c> answers <c>{ displayName }</c> of the operator, <c>401</c> without a session or token and
/// <c>403</c> without the role.
/// </para>
/// <para>
/// <c>GET /api/cell</c> answers <c>{ id, region, tenants, cap, restoreCheck }</c>: <c>tenants</c> is the count the tenant cap checks, previews
/// and unfinished creates included, see docs/reference/cells.md#cell-size, <c>cap</c> is <see cref="CellOptions.TenantCap"/> or
/// <see langword="null"/>, and <c>restoreCheck</c> is <c>{ ranAt, tenantId, passed }</c> of the newest row of <c>platform.restore_checks</c> or
/// <see langword="null"/> when no check has run. <c>GET /api/cell/jobs?tenant</c> answers one page as <c>{ items, page, pageSize, totalCount }</c>, 20 jobs by default, each as
/// the job read answers it, and never with the job's <c>actor</c> or <c>input</c>. The <c>page</c>, <c>pageSize</c>, <c>sort</c>,
/// <c>search</c> and <c>filter</c> are those of the shared <see cref="ListQuery{T}"/>, in the record API's format, and the database applies
/// them, through its SQL form. The fields are <c>createdAt</c>, <c>kind</c>, <c>tenant</c>, <c>state</c>, <c>steps</c> and <c>error</c>,
/// <c>createdAt</c> filters by its UTC day and sorts by its instant, and the search looks in the tenant and the error. An undeclared field,
/// an invalid page or an invalid page size answers <c>400</c> keyed by the parameter, after the checks of the operator policy. The jobs stay
/// newest first without <c>sort</c>, and a sorted list breaks ties newest first, see <c>docs/reference/tenant-lifecycle.md#routes</c>.
/// <c>tenant</c> keeps one tenant's jobs and an unknown tenant answers an empty list.
/// </para>
/// <para>
/// <c>GET /api/cell/tenants</c> answers one page as <c>{ items, page, pageSize, totalCount }</c>, 20 tenants by default, from the
/// <c>page</c>, <c>pageSize</c>, <c>sort</c>, <c>search</c> and <c>filter</c> of the shared <see cref="ListQuery{T}"/>, in the record API's format.
/// The fields are <c>id</c>, <c>status</c>, <c>application</c>, <c>hosts</c>, <c>instances</c>, <c>unknownCalls</c>, <c>deadLetters</c>,
/// <c>storage</c>, <c>users</c>, <c>applications</c>, <c>emailsPerDay</c> and <c>newestArchiveAt</c>, and the search looks in the id and the hosts.
/// An undeclared field, an invalid page or an invalid page size answers <c>400</c> keyed by the parameter, after the checks of the operator
/// policy. Without <c>sort</c> the tenants stay ordered by id, see <c>docs/reference/tenant-lifecycle.md#routes</c>. Each tenant has its <c>id</c>,
/// <c>status</c>, <c>application</c>, <c>hosts</c>, <c>failedWork</c>, <c>usage</c>, <c>newestArchiveAt</c> and <c>deleteAfter</c>, the scheduled purge time of a
/// <c>deleting</c> tenant and <see langword="null"/> for every other. <c>GET /api/cell/tenants/{id}</c> answers one
/// such tenant. Both read the platform tables over the reader role. The <c>failedWork</c> is <c>{ instances, unknownCalls, deadLetters }</c>, the counts of the worker's
/// latest hourly sample in <c>platform.tenant_usage_latest</c>, and <see langword="null"/> until the worker has counted the tenant. The <c>status</c> is <c>deleting</c> while the tenant has a scheduled deletion, whatever
/// its stored status, which stays <c>suspended</c>. A preview tenant belongs to its main tenant's Studio and is not a tenant of the
/// cell, so the list leaves it out and its id is a <c>404</c>. The <c>application</c> is the one the create job
/// recorded on the tenant's row: the template id, or the name of the blank application, and <see langword="null"/> for a tenant that no create job made. The one tenant also answers its <c>quotas</c>: each quota key with its effective <c>value</c> and
/// whether the tenant <c>overridden</c> it, worked out from its <c>platform.tenant_quotas</c> rows and the cell's defaults. It also
/// answers its <c>hostRequests</c>: the newest request of each host that was asked for, as <c>{ host, state, expiresAt }</c>, ordered
/// by host. A pending request past its expiry answers <c>expired</c>, and the token is never answered.
/// </para>
/// <para>
/// <c>GET /api/cell/tenants/{id}/usage-days</c> answers the tenant's day rows of the kept 13 months as one page, <c>{ items, page, pageSize, totalCount }</c>,
/// 20 days by default, from the same <c>page</c>, <c>pageSize</c>, <c>sort</c> and <c>filter</c> of the shared <see cref="ListQuery{T}"/>. The fields are
/// <c>day</c>, <c>databaseBytes</c>, <c>fileBytes</c>, <c>users</c>, <c>applications</c>, <c>emailsSent</c>, <c>connectorCalls</c> and <c>workItemsRun</c>.
/// A day holds no text, so the list has no search and a <c>search</c> answers <c>400</c>. Without <c>sort</c> the days come newest first.
/// An unknown tenant or a preview tenant answers <c>404</c> before any <c>400</c> of the query, see <c>docs/reference/tenant-lifecycle.md#routes</c>.
/// </para>
/// <para>
/// Both reads answer each tenant's <c>usage</c>: <c>storage</c>, <c>users</c>, <c>applications</c> and <c>emailsPerDay</c>, each as
/// <c>{ limit, used }</c> as <c>GET /api/tenant/usage</c> does, with the limit of the tenant's <c>quotas</c>. The <c>used</c> of
/// <c>storage</c> is the latest hourly sample, that of <c>users</c> and <c>applications</c> is the newest day row of
/// <c>platform.tenant_usage</c>, and each is <see langword="null"/> while nothing fills it. The <c>used</c> of <c>emailsPerDay</c> is
/// today's <c>emails</c> counter, 0 when none was sent. The <c>newestArchiveAt</c> is the time of the tenant's newest daily archive in
/// <c>platform.tenant_backups</c>, or <see langword="null"/> when the tenant was never backed up.
/// </para>
/// <para>
/// <c>GET /api/cell/tenants/{id}/administrators</c> answers <c>{ "items": [{ email, displayName, language }] }</c>, ordered by email: the
/// enabled realm accounts that hold the administrator role and have an email, and nothing else of them. The holders are the administrator
/// rows of the tenant's <c>axis.given_roles</c>, which the invite step and the users page write, read over the tenant's runtime role. The
/// route opens the tenant database from the tenant row and not from the tenant catalog, so it answers for a suspended or deleting tenant
/// too. The <c>language</c> is the locale the person chose in the realm, or <see langword="null"/>. An unknown tenant or a preview tenant
/// answers <c>404</c>, a tenant without a realm users client <c>409</c> and an identity provider failure <c>502</c>.
/// </para>
/// <para>
/// <c>PUT /api/cell/tenants/{id}/quotas</c> takes <c>{ "set": { "users": 100, "storage": null }, "remove": ["applications"] }</c>.
/// A set value of <see langword="null"/> is no limit and a removed key goes back to the cell's default. A key the request
/// does not name stays as it is. It applies at once with no job and answers <c>200</c> with the tenant's quotas. An unknown key,
/// a negative value, a key both set and removed, or a request that names no key answers <c>400</c> and writes nothing. It writes
/// over the schema role, see <see cref="CellPlatformWriter"/>, and saves the rows with one platform audit record, command
/// <c>tenant quotas set</c>, naming the keys of the request, or saves neither. The record's <c>actor</c> is the operator's id, the
/// subject of the bearer token or of the console session.
/// </para>
/// <para>
/// <c>POST /api/cell/tenants/{id}/suspend</c>, <c>/resume</c> and <c>/read-only</c>, <c>DELETE /api/cell/tenants/{id}</c> and
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
/// <c>POST /api/cell/jobs/{id}/run-again</c> queues a new job from a <c>failed</c> one, see <see cref="RunAgainJob"/>, and answers <c>202</c> as the
/// suspend route does. The new job has the kind, tenant and input of the failed job, keeps the steps that succeeded and starts the others
/// <c>pending</c>, with fresh claims. The failed job keeps its state and its audit record, and the new job writes a record of its own. The checks run in
/// this order: an unknown job answers <c>404</c>, then the <c>Idempotency-Key</c> header (<c>400</c>), then the replay of the first job or <c>422</c>
/// for a request of the job id and the body. Then, under a transaction lock on the tenant and kind, the route answers <c>409</c> for a job that is not
/// <c>failed</c>, for a <c>purge</c> job, which the publisher queues again on its own, and for a job that is not the tenant's newest of its kind. A <c>create</c> job
/// takes the cell-wide lock of the create route too and answers <c>409</c> for the id of a deleted tenant and, while the tenant has no directory
/// row, for a cell at its <see cref="CellOptions.TenantCap"/> (<c>409</c>, "This cell is full.").
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
/// <c>GET /api/cell/deleted-tenants/{id}</c> answers <c>{ id, deletedAt, completeAt }</c> of a purged main tenant from its tombstone in
/// <c>platform.deleted_tenants</c>, where <c>completeAt</c> is 7 days after <c>deletedAt</c>, see <c>docs/reference/tenant-lifecycle.md#deleting-a-tenant</c>.
/// It never answers the tombstone's actor or hosts. An unknown id and a preview tenant's tombstone, which no <c>purge</c> job names, answer <c>404</c>.
/// </para>
/// <para>
/// A call that an operator makes and a route refuses with a <c>4xx</c> writes the refused call warning of <see cref="RefusedRequests"/>
/// and no record, see <c>docs/reference/tenant-lifecycle.md#provisioning-api</c>.
/// </para>
/// <para>
/// <c>POST /api/cell/tenants</c> takes <c>{ "id", "application", "administrator" }</c>, where the <c>application</c> may be left out for a blank application, and queues a <c>create</c> job with the five
/// steps <c>tenant</c>, <c>application</c>, <c>activate</c>, <c>invite</c> and <c>resume</c>, all <c>pending</c>, and answers <c>202</c>
/// as the suspend route does. The checks run in this order: the <c>Idempotency-Key</c> header (<c>400</c>), the replay of the first job
/// or <c>422</c>, the body (<c>400</c>: the id by the rule of <c>tenant create</c>, an application that is not an installed template, an
/// administrator that is not an email), then under a cell-wide advisory lock an id that a directory row, an unfinished create job
/// or a tombstone of a deleted tenant holds (<c>409</c>) and the cell's <see cref="CellOptions.TenantCap"/> (<c>409</c>, "This cell is full."). The job's <c>input</c> holds the
/// application, when there is one, and the administrator, and the job read never returns it.
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
/// is the caller's id, here the operator's. The route queues no job and takes no <c>Idempotency-Key</c>. The code is <see cref="CellHostRequests"/>,
/// which the tenant administrator's route shares, see <c>docs/reference/tenant-lifecycle.md#hosts-by-a-tenant-administrator</c>.
/// </para>
/// </summary>
internal static partial class CellEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private const int MaxIdempotencyKeyLength = 255;

    private const string PurgeJobKind = "purge";

    private static readonly TimeSpan _idempotencyWindow = TimeSpan.FromHours(24);

    // The steps of each kind, as the publisher's job runner defines them, see ProvisioningJobs.Builtin.
    private static readonly Dictionary<string, string[]> _jobSteps = new(StringComparer.Ordinal)
    {
        ["suspend"] = ["status"],
        ["resume"] = ["status"],
        ["read-only"] = ["status"],
        ["delete"] = ["schedule"],
        ["cancel-deletion"] = ["cancel"],
        ["create"] = ["tenant", "application", "activate", "invite", "resume"],
        ["export"] = ["export"],
    };

    private const int MaxTenantIdLength = 50;

    // The managed PostgreSQL's point-in-time recovery retention, after which a deletion is complete,
    // see docs/reference/tenant-lifecycle.md#deleting-a-tenant.
    private static readonly TimeSpan _pointInTimeRecovery = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions _createJobInputOptions =
        new(JsonSerializerOptions.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static void MapCellEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var cell = endpoints.MapGroup("/api/cell")
            .WithMetadata(CellOperatorRoute.Instance)
            .RequireAuthorization(CellOperator.Policy);
        cell.MapGet("", GetCellAsync);
        cell.MapGet("/session", GetSession);
        cell.MapGet("/tenants", ListTenantsAsync);
        cell.MapGet("/tenants/{id}", GetTenantAsync);
        cell.MapGet("/tenants/{id}/usage-days", ListUsageDaysAsync);
        cell.MapGet("/tenants/{id}/administrators", ListAdministratorsAsync);
        cell.MapGet("/deleted-tenants/{id}", GetDeletedTenantAsync);
        cell.MapPut("/tenants/{id}/quotas", SetQuotasAsync);
        cell.MapPost("/tenants", CreateTenantAsync);
        cell.MapPost("/tenants/{id}/suspend", (string id, HttpContext http, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            QueueJobAsync("suspend", id, http, dataSource, cancellationToken));
        cell.MapPost("/tenants/{id}/resume", (string id, HttpContext http, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            QueueJobAsync("resume", id, http, dataSource, cancellationToken));
        cell.MapPost("/tenants/{id}/read-only", (string id, HttpContext http, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            QueueJobAsync("read-only", id, http, dataSource, cancellationToken));
        cell.MapDelete("/tenants/{id}", (string id, HttpContext http, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            QueueJobAsync("delete", id, http, dataSource, cancellationToken));
        cell.MapPost("/tenants/{id}/deletion/cancel", (string id, HttpContext http, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            QueueJobAsync("cancel-deletion", id, http, dataSource, cancellationToken));
        cell.MapPost("/tenants/{id}/exports", (string id, HttpContext http, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            QueueJobAsync("export", id, http, dataSource, cancellationToken));
        cell.MapPost("/tenants/{id}/hosts", (string id, CellHostRequest? request, HttpContext http, NpgsqlDataSource dataSource, CellOptions options, CancellationToken cancellationToken) =>
            CellHostRequests.RequestAsync(id, request, http.User.FindFirst(CellOperator.IdClaim)!.Value, http, dataSource, options, cancellationToken));
        cell.MapGet("/tenants/{id}/exports/{exportId:guid}", GetExportAsync);
        cell.MapGet("/jobs", ListJobsAsync);
        cell.MapGet("/jobs/{id:guid}", GetJobAsync);
        cell.MapPost("/jobs/{id:guid}/run-again", RunJobAgainAsync);

        // Sign-in and sign-out are the only routes that skip the operator policy. They keep the route marker, so the console
        // host serves them and tenant resolution has no second way through.
        var auth = endpoints.MapGroup("/api/cell/auth")
            .WithMetadata(CellOperatorRoute.Instance)
            .AllowAnonymous();
        auth.MapGet("/sign-in", SignInAsync).WithRateLimit(Partition.SignIn);
        auth.MapPost("/sign-out", SignOutAsync);
    }

    private static IResult GetSession(ClaimsPrincipal user) =>
        Results.Ok(new CellSessionResponse(user.FindFirst(CellOperator.DisplayNameClaim)!.Value));

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

    // A ValueTask result cannot bind as a RequestDelegate, so the endpoint stays a route handler whose result is written.
    private static async ValueTask<IResult> SignOutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CellOperatorSession.Scheme);
        return Results.NoContent();
    }

    private static async Task<IResult> GetCellAsync(NpgsqlDataSource dataSource, CellOptions cell, CancellationToken cancellationToken)
    {
        await using var platform = Open(dataSource);
        var tenants = await CountTowardsCapAsync(platform, cancellationToken);
        var restoreCheck = await platform.RestoreChecks.AsNoTracking()
            .OrderByDescending(check => check.RanAt).ThenByDescending(check => check.Id)
            .Select(check => new CellRestoreCheckResponse(check.RanAt, check.TenantId, check.Passed))
            .FirstOrDefaultAsync(cancellationToken);
        return Results.Ok(new CellResponse(cell.Id!, cell.Region!, tenants, cell.TenantCap, restoreCheck));
    }

    // The fields of the tenant list, see docs/reference/tenant-lifecycle.md#routes. A usage field holds its used value.
    private static readonly IReadOnlyList<ListColumn<CellTenantResponse>> _tenantColumns =
    [
        new("id", ListColumnKind.Text, tenant => tenant.Id, Searchable: true),
        new(
            "status",
            ListColumnKind.Enum,
            tenant => tenant.Status,
            Values: ["active", "readOnly", "suspended", "provisioning", "deleting"]),
        new("application", ListColumnKind.Text, tenant => tenant.Application),
        new("hosts", ListColumnKind.Text, tenant => string.Join(' ', tenant.Hosts), Searchable: true),
        new("instances", ListColumnKind.Number, tenant => tenant.FailedWork?.Instances),
        new("unknownCalls", ListColumnKind.Number, tenant => tenant.FailedWork?.UnknownCalls),
        new("deadLetters", ListColumnKind.Number, tenant => tenant.FailedWork?.DeadLetters),
        new("storage", ListColumnKind.Number, tenant => tenant.Usage.Storage.Used),
        new("users", ListColumnKind.Number, tenant => tenant.Usage.Users.Used),
        new("applications", ListColumnKind.Number, tenant => tenant.Usage.Applications.Used),
        new("emailsPerDay", ListColumnKind.Number, tenant => tenant.Usage.EmailsPerDay.Used),
        new(
            "newestArchiveAt",
            ListColumnKind.Date,
            tenant => tenant.NewestArchiveAt is { } at ? DateOnly.FromDateTime(at.UtcDateTime) : (DateOnly?)null,
            Order: tenant => tenant.NewestArchiveAt),
    ];

    // The fields of the job list, over platform.jobs, see docs/reference/tenant-lifecycle.md#routes. createdAt filters by its UTC day and
    // sorts by its instant. The Value lambdas only satisfy ListColumn, as this list never reads rows in memory.
    private static readonly IReadOnlyList<ListColumn<JobRow>> _jobColumns =
    [
        new(
            "createdAt",
            ListColumnKind.Date,
            job => DateOnly.FromDateTime(job.CreatedAt.UtcDateTime),
            Order: job => job.CreatedAt,
            Sql: "(created_at AT TIME ZONE 'UTC')::date",
            OrderSql: "created_at"),
        new(
            "kind",
            ListColumnKind.Enum,
            job => job.Kind,
            Values: ["suspend", "resume", "read-only", "create", "export", "delete", "cancel-deletion", "purge"],
            Sql: "kind"),
        new("tenant", ListColumnKind.Text, job => job.TenantId, Searchable: true, Sql: "tenant_id"),
        new("state", ListColumnKind.Enum, job => job.State, Values: ["queued", "running", "succeeded", "failed"], Sql: "state"),
        new("steps", ListColumnKind.Text, job => job.Steps, Sql: "steps::text"),
        new("error", ListColumnKind.Text, job => job.Error, Searchable: true, Sql: "error"),
    ];

    // The fields of the usage days of a tenant, see docs/reference/tenant-lifecycle.md#routes. A day holds no text, so none is searchable.
    private static readonly IReadOnlyList<ListColumn<TenantUsageDayResponse>> _usageDayColumns =
    [
        new("day", ListColumnKind.Date, day => day.Day),
        new("databaseBytes", ListColumnKind.Number, day => day.DatabaseBytes),
        new("fileBytes", ListColumnKind.Number, day => day.FileBytes),
        new("users", ListColumnKind.Number, day => day.Users),
        new("applications", ListColumnKind.Number, day => day.Applications),
        new("emailsSent", ListColumnKind.Number, day => day.EmailsSent),
        new("connectorCalls", ListColumnKind.Number, day => day.ConnectorCalls),
        new("workItemsRun", ListColumnKind.Number, day => day.WorkItemsRun),
    ];

    private static async Task<IResult> ListTenantsAsync(
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        QuotaDefaults defaults,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var query = ListQuery<CellTenantResponse>.Read(httpContext.Request, _tenantColumns, errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        await using var platform = Open(dataSource);
        var tenants = await platform.Tenants.AsNoTracking()
            .Where(tenant => tenant.PreviewOf == null)
            .Select(tenant => new { tenant.Id, Status = tenant.DeleteAfter != null ? "deleting" : tenant.Status, tenant.Application, tenant.DeleteAfter })
            .ToListAsync(cancellationToken);
        var ids = tenants.Select(tenant => tenant.Id).ToList();
        var hosts = await ReadHostsAsync(platform, ids, cancellationToken);
        var latest = await ReadLatestSamplesAsync(platform, ids, cancellationToken);
        var quotas = await ReadQuotasAsync(platform, ids, defaults, cancellationToken);
        var usage = await ReadUsageAsync(platform, ids, quotas, latest, Today(time), cancellationToken);
        var archives = await ReadNewestArchivesAsync(platform, ids, cancellationToken);

        IReadOnlyList<CellTenantResponse> rows =
            [.. tenants
                .OrderBy(tenant => tenant.Id, StringComparer.Ordinal)
                .Select(tenant => new CellTenantResponse(
                    tenant.Id,
                    tenant.Status,
                    tenant.Application,
                    HostsOf(hosts, tenant.Id),
                    latest.GetValueOrDefault(tenant.Id)?.FailedWork,
                    usage[tenant.Id],
                    archives.TryGetValue(tenant.Id, out var archive) ? archive : null,
                    tenant.DeleteAfter))];
        var page = query.Apply(rows);
        return Results.Ok(new CellTenantListResponse(page.Items, page.Page, page.PageSize, page.TotalCount));
    }

    private static async Task<IResult> GetTenantAsync(
        string id,
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        QuotaDefaults defaults,
        TenantUsageStore usageStore,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        await using var platform = Open(dataSource);
        var tenant = await platform.Tenants.AsNoTracking()
            .Where(row => row.Id == id && row.PreviewOf == null)
            .Select(row => new { row.Id, Status = row.DeleteAfter != null ? "deleting" : row.Status, row.Application, row.DeleteAfter })
            .SingleOrDefaultAsync(cancellationToken);
        if (tenant is null)
        {
            return RefusedRequests.Problem(httpContext, StatusCodes.Status404NotFound, "No tenant has this id.");
        }

        var hosts = await ReadHostsAsync(platform, [tenant.Id], cancellationToken);
        var latest = await ReadLatestSamplesAsync(platform, [tenant.Id], cancellationToken);
        var quotas = await ReadQuotasAsync(platform, [tenant.Id], defaults, cancellationToken);
        var usage = await ReadUsageAsync(platform, [tenant.Id], quotas, latest, Today(time), cancellationToken);
        var archives = await ReadNewestArchivesAsync(platform, [tenant.Id], cancellationToken);
        var days = await usageStore.ListDaysAsync(tenant.Id, Today(time).AddMonths(-TenantUsageStore.RetentionMonths), cancellationToken);
        var hostRequests = await CellHostRequests.ReadAsync(platform, tenant.Id, time.GetUtcNow(), cancellationToken);
        return Results.Ok(new CellTenantDetailResponse(
            tenant.Id,
            tenant.Status,
            tenant.Application,
            HostsOf(hosts, tenant.Id),
            latest.GetValueOrDefault(tenant.Id)?.FailedWork,
            quotas[tenant.Id],
            usage[tenant.Id],
            archives.TryGetValue(tenant.Id, out var archive) ? archive : null,
            tenant.DeleteAfter,
            UsageDaysOf(days),
            hostRequests));
    }

    // The tenant row is checked before the query, as the other tenant routes check the id before the rest of the request.
    private static async Task<IResult> ListUsageDaysAsync(
        string id,
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        TenantUsageStore usageStore,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        await using var platform = Open(dataSource);
        if (!await platform.Tenants.AsNoTracking().AnyAsync(row => row.Id == id && row.PreviewOf == null, cancellationToken))
        {
            return RefusedRequests.Problem(httpContext, StatusCodes.Status404NotFound, "No tenant has this id.");
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var query = ListQuery<TenantUsageDayResponse>.Read(httpContext.Request, _usageDayColumns, errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var days = await usageStore.ListDaysAsync(id, Today(time).AddMonths(-TenantUsageStore.RetentionMonths), cancellationToken);
        var page = query.Apply(UsageDaysOf(days));
        return Results.Ok(new CellTenantUsageDayListResponse(page.Items, page.Page, page.PageSize, page.TotalCount));
    }

    private static IReadOnlyList<TenantUsageDayResponse> UsageDaysOf(IReadOnlyList<TenantUsageRow> days) =>
        [.. days.Select(row => new TenantUsageDayResponse(
            row.Day, row.DatabaseBytes, row.FileBytes, row.Users, row.Applications, row.EmailsSent, row.ConnectorCalls, row.WorkItemsRun))];

    // The tombstone of a preview tenant stays hidden: only a main tenant has a purge job that names it. The hosts step writes the tombstone,
    // so a purge that is still running has one already. The actor and the hosts stay out of the select.
    private static async Task<IResult> GetDeletedTenantAsync(
        string id,
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        await using var platform = Open(dataSource);
        var deletedAt = await platform.DeletedTenants.AsNoTracking()
            .Where(row => row.Id == id && platform.Jobs.Any(job => job.Kind == PurgeJobKind && job.TenantId == id))
            .Select(row => (DateTimeOffset?)row.DeletedAt)
            .SingleOrDefaultAsync(cancellationToken);
        if (deletedAt is null)
        {
            return RefusedRequests.Problem(httpContext, StatusCodes.Status404NotFound, "No deleted tenant has this id.");
        }

        return Results.Ok(new CellDeletedTenantResponse(id, deletedAt.Value, deletedAt.Value + _pointInTimeRecovery));
    }

    // The tenant row, not the tenant catalog, which holds active tenants only. A suspended tenant has no pool, so the read opens one connection.
    private static async Task<IResult> ListAdministratorsAsync(
        string id,
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        TenantSecretCipher cipher,
        CellTenantDatabases databases,
        IHttpClientFactory httpClients,
        ILogger<CellAdministratorsLog> logger,
        CancellationToken cancellationToken)
    {
        TenantRow? row;
        await using (var platform = Open(dataSource))
        {
            row = await platform.Tenants.AsNoTracking().SingleOrDefaultAsync(tenant => tenant.Id == id && tenant.PreviewOf == null, cancellationToken);
        }

        if (row is null)
        {
            return RefusedRequests.Problem(httpContext, StatusCodes.Status404NotFound, "No tenant has this id.");
        }

        if (DirectoryTenantCatalog.RealmUsers(row, cipher) is not { } access)
        {
            return RefusedRequests.Problem(httpContext, StatusCodes.Status409Conflict, "This tenant has no realm to read its administrators from.");
        }

        try
        {
            IReadOnlyList<string> holders;
            await using (var connection = new NpgsqlConnection(
                DirectoryTenantCatalog.RuntimeConnectionString(row, cipher, databases.Tls, databases.Address)))
            {
                await connection.OpenAsync(cancellationToken);
                holders = await GivenRoles.ListPlatformRoleHoldersAsync(connection, UserInvitations.AdministratorKind, cancellationToken);
            }

            using var client = httpClients.CreateClient(nameof(CellEndpoints));
            var users = new RealmUsers(client, access);
            var administrators = new List<CellAdministratorResponse>();
            foreach (var holder in holders)
            {
                if (await users.ReadContactAsync(holder, cancellationToken) is { Enabled: true, Email: { } email } contact)
                {
                    administrators.Add(new CellAdministratorResponse(email, contact.DisplayName, contact.Locale));
                }
            }

            return Results.Ok(new CellAdministratorListResponse([.. administrators.OrderBy(administrator => administrator.Email, StringComparer.Ordinal)]));
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            LogAdministratorsFailed(logger, id, exception.Message);
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The identity provider could not read the administrators.");
        }
    }

    [LoggerMessage(LogLevel.Warning, "The administrators of tenant {TenantId} could not be read. {Reason}")]
    private static partial void LogAdministratorsFailed(ILogger logger, string tenantId, string reason);

    private static async Task<IResult> SetQuotasAsync(
        string id,
        CellQuotasRequest? request,
        HttpContext httpContext,
        CellPlatformWriter writer,
        QuotaDefaults defaults,
        CancellationToken cancellationToken)
    {
        var set = request?.Set ?? [];
        var remove = request?.Remove ?? [];
        var problem = ValidateQuotas(request, set, remove);
        if (problem is not null)
        {
            return RefusedRequests.Problem(httpContext, StatusCodes.Status400BadRequest, problem, "The quotas in the request are not valid.");
        }

        var named = set.Keys.Concat(remove).ToHashSet(StringComparer.Ordinal);
        await using var platform = writer.Open();
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);
        if (!await platform.Tenants.AnyAsync(row => row.Id == id && row.PreviewOf == null, cancellationToken))
        {
            return RefusedRequests.Problem(httpContext, StatusCodes.Status404NotFound, "No tenant has this id.");
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
            platform,
            "tenant quotas set",
            id,
            [.. QuotaKeys.All.Where(named.Contains)],
            httpContext.User.FindFirst(CellOperator.IdClaim)!.Value,
            cancellationToken);
        var quotas = await ReadQuotasAsync(platform, [id], defaults, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new CellQuotasResponse(quotas[id]));
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
            return RefusedRequests.Problem(httpContext, StatusCodes.Status404NotFound, "No tenant has this id.");
        }

        if (ReadIdempotencyKey(httpContext) is not { } key)
        {
            return MissingKey(httpContext);
        }

        var hash = HashRequest(kind, id, await ReadBodyAsync(httpContext, cancellationToken));
        if (await ReplayAsync(httpContext, platform, transaction, key, hash, cancellationToken) is { } replay)
        {
            return replay;
        }

        var job = NewJob(kind, id, httpContext, key, hash, null);
        platform.Jobs.Add(job);
        await platform.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Accepted(job);
    }

    // What the cap counts, see docs/reference/cells.md#cell-size: every directory row, previews included, and each unfinished create with no row yet.
    private static async Task<int> CountTowardsCapAsync(PlatformDbContext platform, CancellationToken cancellationToken)
    {
        var unfinished = platform.Jobs.AsNoTracking().Where(job => job.Kind == "create" && (job.State == "queued" || job.State == "running"));
        return await platform.Tenants.CountAsync(cancellationToken)
            + await unfinished.CountAsync(job => !platform.Tenants.Any(tenant => tenant.Id == job.TenantId), cancellationToken);
    }

    private static async Task<bool> IsFullAsync(PlatformDbContext platform, CellOptions cell, CancellationToken cancellationToken) =>
        cell.TenantCap is { } cap && await CountTowardsCapAsync(platform, cancellationToken) >= cap;

    private static IResult CellFull(HttpContext httpContext) =>
        RefusedRequests.Problem(httpContext, StatusCodes.Status409Conflict, "This cell is full.");

    private static async Task<IResult> CreateTenantAsync(
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        TemplateCatalog templates,
        CellOptions cell,
        CancellationToken cancellationToken)
    {
        if (ReadIdempotencyKey(httpContext) is not { } key)
        {
            return MissingKey(httpContext);
        }

        var body = await ReadBodyAsync(httpContext, cancellationToken);
        var hash = HashRequest("create", "", body);
        await using var platform = Open(dataSource);
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);

        // The replay comes before every check that a later change can flip, such as the tenant it created or a removed template.
        if (await ReplayAsync(httpContext, platform, transaction, key, hash, cancellationToken) is { } replay)
        {
            return replay;
        }

        var request = ReadCreateRequest(body);
        var problem = ValidateCreate(request, templates);
        if (problem is not null)
        {
            return RefusedRequests.Problem(httpContext, StatusCodes.Status400BadRequest, problem);
        }

        if (QuotaValueProblem(request!.Quotas ?? []) is { } quotaProblem)
        {
            return RefusedRequests.Problem(httpContext, StatusCodes.Status422UnprocessableEntity, quotaProblem, "A quota in the request is not valid.");
        }

        var id = request.Id!;

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
            return RefusedRequests.Problem(httpContext, StatusCodes.Status409Conflict, "A tenant of the cell already has this id.");
        }

        if (await IsFullAsync(platform, cell, cancellationToken))
        {
            return CellFull(httpContext);
        }

        var input = JsonSerializer.Serialize(
            new CellCreateJobInput(request.Application, request.Administrator, request.Quotas), _createJobInputOptions);
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
            return "The request must be a JSON object with an id, an administrator and, optionally, an application.";
        }

        if (request.Id is not { } id
            || !TenantOptionsValidator.IsValidTenantId(id)
            || id.Length > MaxTenantIdLength
            || id == "master")
        {
            return $"The tenant id must have 1 to {MaxTenantIdLength} lowercase letters, digits and hyphens, and must not be 'master'.";
        }

        if (request.Application is { } application
            && !templates.Newest.Any(template => string.Equals(template.Manifest.Id, application, StringComparison.Ordinal)))
        {
            return "The application, when given, must be the id of an installed application template.";
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

    private static IResult MissingKey(HttpContext httpContext) =>
        RefusedRequests.Problem(
            httpContext,
            StatusCodes.Status400BadRequest,
            $"The request must carry an {IdempotencyKeyHeader} header of 1 to {MaxIdempotencyKeyLength} characters.");

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
        HttpContext httpContext,
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
            return RefusedRequests.Problem(
                httpContext,
                StatusCodes.Status422UnprocessableEntity,
                $"The {IdempotencyKeyHeader} was used before with another request.");
        }

        await transaction.CommitAsync(cancellationToken);
        return Accepted(first);
    }

    private static JobRow NewJob(string kind, string tenantId, HttpContext httpContext, string key, string hash, string? input) =>
        NewJob(kind, tenantId, httpContext.User.FindFirst(CellOperator.IdClaim)!.Value, key, hash, input);

    internal static JobRow NewJob(string kind, string tenantId, string actor, string? key, string? hash, string? input) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            TenantId = tenantId,
            State = "queued",
            Steps = JsonSerializer.Serialize(_jobSteps[kind].Select(name => new CellJobStepResponse(name, "pending", null)), JsonSerializerOptions.Web),
            Input = input,
            Actor = actor,
            IdempotencyKey = key,
            RequestHash = hash,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // A new job for a failed one: the same kind, tenant and input, the steps that succeeded kept and the others pending, and fresh claims.
    // The failed row stays as it is, so it keeps its audit record, see docs/reference/tenant-lifecycle.md#jobs.
    internal static JobRow RunAgainJob(JobRow failed, string actor, string? key, string? hash)
    {
        ArgumentNullException.ThrowIfNull(failed);
        var steps = (JsonSerializer.Deserialize<List<CellJobStepResponse>>(failed.Steps, JsonSerializerOptions.Web) ?? [])
            .Select(step => step.State == "succeeded" ? step : new CellJobStepResponse(step.Name, "pending", null));
        return new JobRow
        {
            Id = Guid.CreateVersion7(),
            Kind = failed.Kind,
            TenantId = failed.TenantId,
            State = "queued",
            Steps = JsonSerializer.Serialize(steps, JsonSerializerOptions.Web),
            Input = failed.Input,
            Actor = actor,
            IdempotencyKey = key,
            RequestHash = hash,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static async Task<IResult> RunJobAgainAsync(
        Guid id,
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        CellOptions cell,
        CancellationToken cancellationToken)
    {
        await using var platform = Open(dataSource);
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);
        if (!await platform.Jobs.AsNoTracking().AnyAsync(row => row.Id == id, cancellationToken))
        {
            return NoJob(httpContext);
        }

        if (ReadIdempotencyKey(httpContext) is not { } key)
        {
            return MissingKey(httpContext);
        }

        var hash = HashRequest("run-again", id.ToString("D"), await ReadBodyAsync(httpContext, cancellationToken));
        if (await ReplayAsync(httpContext, platform, transaction, key, hash, cancellationToken) is { } replay)
        {
            return replay;
        }

        // The lock keeps two parallel requests for jobs of one tenant and kind from both passing the newest-job check, so the job is read again after it.
        var failed = await platform.Jobs.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (failed is null)
        {
            return NoJob(httpContext);
        }

        await platform.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended('axis-job-run-again:' || {failed.TenantId} || ':' || {failed.Kind}, 0))",
            cancellationToken);
        failed = await platform.Jobs.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (failed is null)
        {
            return NoJob(httpContext);
        }

        if (failed.State != "failed")
        {
            return RefusedRequests.Problem(httpContext, StatusCodes.Status409Conflict, "Only a failed job can be run again.");
        }

        if (failed.Kind == PurgeJobKind)
        {
            return RefusedRequests.Problem(httpContext, StatusCodes.Status409Conflict, "The publisher queues a failed purge job again on its own.");
        }

        // The newest job of the kind is the one the list shows first, so a job that is not the newest has a newer one.
        var newest = await platform.Jobs.AsNoTracking()
            .Where(job => job.TenantId == failed.TenantId && job.Kind == failed.Kind)
            .OrderByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.Id)
            .FirstAsync(cancellationToken);
        if (newest.Id != failed.Id)
        {
            return RefusedRequests.Problem(httpContext, StatusCodes.Status409Conflict, "The tenant has a newer job of this kind.");
        }

        if (failed.Kind == "create")
        {
            // The checks of the create route, as a create whose tenant step never ran has not met them yet.
            await platform.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended('axis-tenant-create', 0))",
                cancellationToken);
            if (await DeletedTenantHolds.IsDeletedIdAsync(platform, failed.TenantId, cancellationToken))
            {
                return RefusedRequests.Problem(httpContext, StatusCodes.Status409Conflict, "A tenant of the cell already has this id.");
            }

            if (!await platform.Tenants.AnyAsync(tenant => tenant.Id == failed.TenantId, cancellationToken)
                && await IsFullAsync(platform, cell, cancellationToken))
            {
                return CellFull(httpContext);
            }
        }

        var job = RunAgainJob(failed, httpContext.User.FindFirst(CellOperator.IdClaim)!.Value, key, hash);
        platform.Jobs.Add(job);
        await platform.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Accepted(job);
    }

    private static IResult NoJob(HttpContext httpContext) =>
        RefusedRequests.Problem(httpContext, StatusCodes.Status404NotFound, "No job has this id.");

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
        await using (var platform = Open(dataSource))
        {
            if (!await platform.Tenants.AsNoTracking().AnyAsync(row => row.Id == id && row.PreviewOf == null, cancellationToken))
            {
                return RefusedRequests.Problem(httpContext, StatusCodes.Status404NotFound, "No tenant has this id.");
            }
        }

        return await CellExportReads.AnswerAsync(
            id, exportId, httpContext.User.FindFirst(CellOperator.IdClaim)!.Value, httpContext, dataSource, writer, storage, cancellationToken);
    }

    private static async Task<IResult> GetJobAsync(Guid id, HttpContext httpContext, NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var platform = Open(dataSource);
        var job = await platform.Jobs.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (job is null)
        {
            return NoJob(httpContext);
        }

        return Results.Ok(JobDetail(job));
    }

    // Job ids are time-ordered, so the id settles jobs created in the same instant.
    private static async Task<IResult> ListJobsAsync(string? tenant, HttpContext httpContext, NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var query = ListQuery<JobRow>.Read(httpContext.Request, _jobColumns, errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var list = query.Sql();
        var where = $"WHERE (@tenant::text IS NULL OR tenant_id = @tenant) AND {list.Condition}";
        var order = list.Order is null ? "created_at DESC, id DESC" : $"{list.Order}, created_at DESC, id DESC";

        void AddFilter(NpgsqlCommand command)
        {
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Text)
            {
                Value = string.IsNullOrEmpty(tenant) ? DBNull.Value : tenant,
            });
            list.AddTo(command);
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        long totalCount;
        await using (var count = new NpgsqlCommand($"SELECT count(*) FROM {PlatformDbContext.Schema}.jobs {where}", connection))
        {
            AddFilter(count);
            totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

        await using var command = new NpgsqlCommand(
            $"SELECT id, kind, tenant_id, state, created_at, steps::text, error FROM {PlatformDbContext.Schema}.jobs {where} ORDER BY {order} LIMIT @limit OFFSET @offset",
            connection);
        AddFilter(command);
        command.Parameters.AddWithValue("limit", query.PageSize);
        command.Parameters.AddWithValue("offset", (long)(query.Page - 1) * query.PageSize);

        var items = new List<CellJobDetailResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(JobDetail(new JobRow
            {
                Id = reader.GetGuid(0),
                Kind = reader.GetString(1),
                TenantId = reader.GetString(2),
                State = reader.GetString(3),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(4),
                Steps = reader.GetString(5),
                Error = reader.IsDBNull(6) ? null : reader.GetString(6),
            }));
        }

        return Results.Ok(new CellJobListResponse(items, query.Page, query.PageSize, totalCount));
    }

    private static CellJobDetailResponse JobDetail(JobRow job)
    {
        var steps = JsonSerializer.Deserialize<List<CellJobStepResponse>>(job.Steps, JsonSerializerOptions.Web) ?? [];
        return new CellJobDetailResponse(job.Id, job.Kind, job.TenantId, job.State, job.CreatedAt, steps, job.Error);
    }

    private static string? ValidateQuotas(CellQuotasRequest? request, Dictionary<string, long?> set, List<string> remove)
    {
        if (request is null || set.Count + remove.Count == 0)
        {
            return "The request must set or remove at least one quota.";
        }

        if (QuotaValueProblem(set, remove) is { } problem)
        {
            return problem;
        }

        var both = remove.FirstOrDefault(set.ContainsKey);
        return both is null ? null : $"'{both}' is both set and removed.";
    }

    // The checks of one quota value map, shared by the quotas route and the create route. Removed keys are checked as well.
    private static string? QuotaValueProblem(Dictionary<string, long?> values, IEnumerable<string>? removed = null)
    {
        var unknown = values.Keys.Concat(removed ?? []).Where(key => key is null || !QuotaKeys.All.Contains(key)).ToList();
        if (unknown.Count > 0)
        {
            return $"'{unknown[0]}' is not a quota. The quotas are {string.Join(", ", QuotaKeys.All)}.";
        }

        var negative = values.FirstOrDefault(entry => entry.Value < 0).Key;
        return negative is null ? null : $"The value of '{negative}' must be zero or more, or null for no limit.";
    }

    private static async Task<Dictionary<string, Dictionary<string, CellQuotaResponse>>> ReadQuotasAsync(
        PlatformDbContext platform,
        List<string> tenantIds,
        QuotaDefaults defaults,
        CancellationToken cancellationToken)
    {
        var rows = (await platform.TenantQuotas.AsNoTracking()
                .Where(row => tenantIds.Contains(row.TenantId))
                .Select(row => new { row.TenantId, row.Quota, row.Value })
                .ToListAsync(cancellationToken))
            .ToLookup(row => row.TenantId, StringComparer.Ordinal);
        return tenantIds.ToDictionary(
            tenantId => tenantId,
            tenantId =>
            {
                var overrides = rows[tenantId].ToDictionary(row => row.Quota, row => row.Value, StringComparer.Ordinal);
                return QuotaKeys.All.ToDictionary(
                    key => key,
                    key => overrides.TryGetValue(key, out var value) ? new CellQuotaResponse(value, true) : new CellQuotaResponse(defaults[key], false),
                    StringComparer.Ordinal);
            },
            StringComparer.Ordinal);
    }

    internal static PlatformDbContext Open(NpgsqlDataSource dataSource) =>
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

    // A tenant without a row has no entry. A row the worker wrote before it counted failed work has a null FailedWork.
    private static async Task<Dictionary<string, LatestSample>> ReadLatestSamplesAsync(
        PlatformDbContext platform,
        List<string> tenantIds,
        CancellationToken cancellationToken)
    {
        var rows = await platform.TenantUsageLatest.AsNoTracking()
            .Where(row => tenantIds.Contains(row.TenantId))
            .Select(row => new { row.TenantId, row.DatabaseBytes, row.FileBytes, row.FailedInstances, row.UnknownCalls, row.OpenDeadLetters })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(
            row => row.TenantId,
            row => new LatestSample(
                row.DatabaseBytes + row.FileBytes,
                row is { FailedInstances: { } instances, UnknownCalls: { } calls, OpenDeadLetters: { } deadLetters }
                    ? new CellFailedWorkResponse(instances, calls, deadLetters)
                    : null),
            StringComparer.Ordinal);
    }

    // The limits are those of the tenants' quotas. A count the worker has not filled stays null, and no email sent today is 0.
    private static async Task<Dictionary<string, CellTenantUsageResponse>> ReadUsageAsync(
        PlatformDbContext platform,
        List<string> tenantIds,
        Dictionary<string, Dictionary<string, CellQuotaResponse>> quotas,
        Dictionary<string, LatestSample> latest,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var days = (await platform.TenantUsage.AsNoTracking()
                .Where(row => tenantIds.Contains(row.TenantId))
                .GroupBy(row => row.TenantId)
                .Select(group => group.OrderByDescending(row => row.Day)
                    .Select(row => new { row.TenantId, row.Users, row.Applications })
                    .First())
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.TenantId, StringComparer.Ordinal);
        var emails = await platform.TenantDailyCounters.AsNoTracking()
            .Where(row => tenantIds.Contains(row.TenantId) && row.Day == today && row.Counter == TenantDailyCounterStore.Emails)
            .ToDictionaryAsync(row => row.TenantId, row => row.Value, StringComparer.Ordinal, cancellationToken);
        return tenantIds.ToDictionary(
            tenantId => tenantId,
            tenantId =>
            {
                var limits = quotas[tenantId];
                days.TryGetValue(tenantId, out var day);
                return new CellTenantUsageResponse(
                    new TenantQuotaUseResponse(limits[QuotaKeys.Storage].Value, latest.GetValueOrDefault(tenantId)?.Storage),
                    new TenantQuotaUseResponse(limits[QuotaKeys.Users].Value, day?.Users),
                    new TenantQuotaUseResponse(limits[QuotaKeys.Applications].Value, day?.Applications),
                    new TenantQuotaUseResponse(limits[QuotaKeys.EmailsPerDay].Value, emails.GetValueOrDefault(tenantId)));
            },
            StringComparer.Ordinal);
    }

    private static Task<Dictionary<string, DateTimeOffset>> ReadNewestArchivesAsync(
        PlatformDbContext platform,
        List<string> tenantIds,
        CancellationToken cancellationToken) =>
        platform.TenantBackups.AsNoTracking()
            .Where(row => tenantIds.Contains(row.TenantId))
            .ToDictionaryAsync(row => row.TenantId, row => row.LastSuccessAt, StringComparer.Ordinal, cancellationToken);

    private static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    private static string[] HostsOf(ILookup<string, string> hosts, string tenantId) =>
        [.. hosts[tenantId].Order(StringComparer.Ordinal)];
}

/// <summary>The signed-in operator: the name the console shows.</summary>
internal sealed record CellSessionResponse(string DisplayName);

/// <summary>The newest stored result of the weekly restore check: when the run started, its source tenant and whether it passed.</summary>
internal sealed record CellRestoreCheckResponse(DateTimeOffset RanAt, string TenantId, bool Passed);

/// <summary>
/// The cell: its id and region, the count the tenant cap checks, its tenant cap or null and its newest restore check or null.
/// </summary>
internal sealed record CellResponse(string Id, string Region, int Tenants, int? Cap, CellRestoreCheckResponse? RestoreCheck);

/// <summary>The counts of a tenant's failed work: numbers only, with no ids, inputs or errors.</summary>
internal sealed record CellFailedWorkResponse(long Instances, long UnknownCalls, long DeadLetters);

/// <summary>The newest hourly sample of a tenant: its storage in bytes, and its failed work once the worker has counted it.</summary>
internal sealed record LatestSample(long Storage, CellFailedWorkResponse? FailedWork);

/// <summary>
/// A tenant's use against its limits: sizes and counts only. Each <c>Used</c> is null while nothing fills it, see
/// <c>docs/reference/tenant-lifecycle.md#routes</c>.
/// </summary>
internal sealed record CellTenantUsageResponse(
    TenantQuotaUseResponse Storage,
    TenantQuotaUseResponse Users,
    TenantQuotaUseResponse Applications,
    TenantQuotaUseResponse EmailsPerDay);

/// <summary>
/// One tenant of the cell. <c>Application</c> is the one the create job recorded, and null for a tenant that no create job made. <c>FailedWork</c> is null until the
/// worker has counted the tenant. <c>NewestArchiveAt</c> is null for a tenant that was never backed up. <c>DeleteAfter</c> is the scheduled purge time of a
/// <c>deleting</c> tenant and null for every other.
/// </summary>
internal sealed record CellTenantResponse(
    string Id,
    string Status,
    string? Application,
    IReadOnlyList<string> Hosts,
    CellFailedWorkResponse? FailedWork,
    CellTenantUsageResponse Usage,
    DateTimeOffset? NewestArchiveAt,
    DateTimeOffset? DeleteAfter);

/// <summary>A purged main tenant: when the purge removed its hosts and when its deletion is complete, 7 days later.</summary>
internal sealed record CellDeletedTenantResponse(string Id, DateTimeOffset DeletedAt, DateTimeOffset CompleteAt);

/// <summary>One page of the tenants of the cell, with the number of tenants that matched before the page was cut.</summary>
internal sealed record CellTenantListResponse(IReadOnlyList<CellTenantResponse> Items, int Page, int PageSize, long TotalCount);

/// <summary>One page of the day rows of a tenant's usage, with the number of days that matched before the page was cut.</summary>
internal sealed record CellTenantUsageDayListResponse(IReadOnlyList<TenantUsageDayResponse> Items, int Page, int PageSize, long TotalCount);

/// <summary>
/// One tenant of the cell with its effective quotas, keyed by quota key, its day rows of the last 13 months, newest first, and the newest
/// request of each host that was asked for.
/// </summary>
internal sealed record CellTenantDetailResponse(
    string Id,
    string Status,
    string? Application,
    IReadOnlyList<string> Hosts,
    CellFailedWorkResponse? FailedWork,
    IReadOnlyDictionary<string, CellQuotaResponse> Quotas,
    CellTenantUsageResponse Usage,
    DateTimeOffset? NewestArchiveAt,
    DateTimeOffset? DeleteAfter,
    IReadOnlyList<TenantUsageDayResponse> Days,
    IReadOnlyList<CellHostRequestStateResponse> HostRequests);

/// <summary>The newest request for one host of a tenant's own: its state and when it expires, with no token.</summary>
internal sealed record CellHostRequestStateResponse(string Host, string State, DateTimeOffset ExpiresAt);

/// <summary>The category of the logs of the administrators route.</summary>
internal sealed class CellAdministratorsLog;

/// <summary>One enabled administrator of a tenant. <c>Language</c> is the locale the person chose in the realm, or null when none.</summary>
internal sealed record CellAdministratorResponse(string Email, string DisplayName, string? Language);

/// <summary>The enabled administrators of a tenant, ordered by email.</summary>
internal sealed record CellAdministratorListResponse(IReadOnlyList<CellAdministratorResponse> Items);

/// <summary>The body of the quotas write: the quotas to set, where <c>null</c> is no limit, and the quotas to take back to the default.</summary>
internal sealed record CellQuotasRequest(Dictionary<string, long?>? Set, List<string>? Remove);

/// <summary>One effective quota. <c>Value</c> is null for no limit. <c>Overridden</c> is false when the cell's default applies.</summary>
internal sealed record CellQuotaResponse(long? Value, bool Overridden);

/// <summary>The effective quotas of a tenant, keyed by quota key.</summary>
internal sealed record CellQuotasResponse(IReadOnlyDictionary<string, CellQuotaResponse> Quotas);

/// <summary>
/// The body of the create route. A missing or wrongly typed member is null and answers <c>400</c>. <c>Quotas</c> maps a quota key to
/// its value, where <c>null</c> is no limit, and is null when the tenant uses the cell's defaults.
/// </summary>
internal sealed record CellCreateTenantRequest(string? Id, string? Application, string? Administrator, Dictionary<string, long?>? Quotas);

/// <summary>The input the create route stores on the create job. <c>Quotas</c> is left out when the request had none.</summary>
internal sealed record CellCreateJobInput(string? Application, string? Administrator, Dictionary<string, long?>? Quotas);

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

/// <summary>One page of the jobs of the cell, with the number of jobs that matched before it was cut.</summary>
internal sealed record CellJobListResponse(IReadOnlyList<CellJobDetailResponse> Items, int Page, int PageSize, long TotalCount);

/// <summary>One step of a job. <c>Error</c> is the refusal of a failed step.</summary>
internal sealed record CellJobStepResponse(string Name, string State, string? Error);

/// <summary>A tenant export. <c>ExpiresAt</c> is null until the export job succeeds.</summary>
internal sealed record CellExportResponse(Guid Id, string TenantId, string State, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);
