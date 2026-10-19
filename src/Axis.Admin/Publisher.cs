using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Data;
using Axis.Data.Audit;
using Axis.Processes.Storage;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Admin;

/// <summary>
/// <c>publisher</c>: runs the <c>publish</c> and <c>validate</c> requests that Studio stores in the tenant databases, until it
/// is stopped. Each poll round it claims the oldest queued request of every active tenant that is on this build's schema
/// version. A <c>publish</c> runs the steps of <c>activate</c>, compiling from the stored snapshot and never from a folder. A
/// <c>validate</c> runs the compile and every activation check in a transaction that it rolls back. A request ends
/// <c>succeeded</c> or <c>failed</c>, with its diagnostics. A <c>publish</c> writes its audit record in the same transaction,
/// and a <c>validate</c> writes none. A request of another tenant, and a <c>rollback</c>, stays queued. While a request runs,
/// the publisher renews its lease. A running request whose lease has run out is claimed again, and the third such claim to
/// run out ends it <c>failed</c> with <c>AXC0215</c>. The <c>claims</c> count fences the final update, so a publisher that
/// lost its claim changes nothing. Every step runs as the tenant's schema role, and a stop finishes the request that is
/// running, then ends the loop.
/// </summary>
internal sealed class Publisher(
    ActivateSettings settings,
    TimeSpan refreshInterval,
    TimeSpan leaseDuration,
    TextWriter output,
    TextWriter error,
    CancellationToken stopping)
{
    internal static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(5);

    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private static readonly string _claimedBy = $"{Environment.MachineName}-{Environment.ProcessId}";

    private const int MaxClaims = 3;

    // Claims of one tenant database take this lock, so two publishers cannot both see no running request of an application.
    private const long ClaimLockKey = 0x4158_4953_5055_424C; // "AXISPUBL"

    private const string ExhaustedSql =
        """
        UPDATE axis.publish_requests
        SET status = 'failed', finished_at = clock_timestamp(), diagnostics = @diagnostics
        WHERE kind IN ('publish', 'validate') AND status = 'running' AND lease_until < clock_timestamp() AND claims >= @max
        RETURNING id, application_id, requested_by, requested_by_name, kind
        """;

    // clock_timestamp() and not now(): now() is fixed when the transaction starts, before the wait for the lock.
    private const string ClaimSql =
        """
        WITH next AS (
          SELECT r.id FROM axis.publish_requests r
          WHERE r.kind IN ('publish', 'validate')
            AND (r.status = 'queued'
                 OR (r.status = 'running' AND r.lease_until < clock_timestamp() AND r.claims < @max))
            AND NOT EXISTS (SELECT 1 FROM axis.publish_requests o
                            WHERE o.application_id = r.application_id AND o.status = 'running' AND o.id <> r.id)
          ORDER BY r.requested_at, r.id LIMIT 1
          FOR UPDATE SKIP LOCKED)
        UPDATE axis.publish_requests p
        SET status = 'running', claimed_by = @me, lease_until = clock_timestamp() + @lease,
            claims = p.claims + 1, started_at = coalesce(p.started_at, clock_timestamp())
        FROM next WHERE p.id = next.id
        RETURNING p.id, p.application_id, p.snapshot::text, p.requested_by, p.requested_by_name, p.kind, p.claims
        """;

    private const string RenewSql =
        """
        UPDATE axis.publish_requests
        SET lease_until = clock_timestamp() + @lease
        WHERE id = @id AND status = 'running' AND claims = @claims
        """;

    // The claims count is the fencing token: it goes up on every claim, so a publisher that lost its claim changes no row.
    private const string FinishSql =
        """
        UPDATE axis.publish_requests
        SET status = @status, finished_at = now(), release_id = @release, diagnostics = @diagnostics
        WHERE id = @id AND status = 'running' AND claims = @claims
        """;

    private IReadOnlyList<TenantRow> _served = [];
    private Dictionary<string, ActivationBindings> _bindings = [];

    public async Task<int> RunAsync()
    {
        try
        {
            var problem = await AdminCommands.PlatformTablesProblemAsync(settings.Platform.PlatformConnectionString, stopping);
            if (problem is not null)
            {
                error.WriteLine(problem);
                return 1;
            }

            await ReadDirectoryAsync();
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception exception)
        {
            error.WriteLine($"The tenant directory could not be read: {TenantDatabaseSteps.Safe(exception)}");
            return 1;
        }

        var lastRead = Stopwatch.StartNew();
        while (!stopping.IsCancellationRequested)
        {
            if (lastRead.Elapsed >= refreshInterval)
            {
                lastRead.Restart();
                try
                {
                    await ReadDirectoryAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // The previous list stays, so a short outage of the platform database stops no tenant.
                    error.WriteLine($"The tenant directory could not be read: {TenantDatabaseSteps.Safe(exception)}");
                }
            }

            var claimed = false;
            foreach (var row in _served)
            {
                if (stopping.IsCancellationRequested)
                {
                    break;
                }

                claimed |= await RunNextAsync(row);
            }

            if (!claimed)
            {
                try
                {
                    await Task.Delay(PollInterval, stopping);
                }
                catch (OperationCanceledException)
                {
                    // A stop ends the loop.
                }
            }
        }

        return 0;
    }

    private async Task ReadDirectoryAsync()
    {
        await using var platform = new PlatformDbContext(
            new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(settings.Platform.PlatformConnectionString).Options);
        var rows = await platform.Tenants.AsNoTracking().ToListAsync(stopping);
        var served = rows
            .Where(row => row.Status == "active" && row.SchemaVersion == TenantSchemaVersion.Current)
            .OrderBy(row => row.Id, StringComparer.Ordinal)
            .ToList();
        _bindings = await AdminCommands.ReadBindingsAsync(platform, [.. served.Select(row => row.Id)], stopping);
        _served = served;
    }

    // Claims and runs one request of the tenant. Returns whether one was claimed.
    private async Task<bool> RunNextAsync(TenantRow row)
    {
        var id = row.Id;
        await using var steps = new TenantDatabaseSteps(settings.Platform.Cipher, row, row.DatabaseHost, row.DatabasePort, CancellationToken.None);
        ClaimedRequest? request;
        try
        {
            request = await ClaimAsync(steps, id);
        }
        catch (Exception exception)
        {
            output.WriteLine($"{id} claim failed: {TenantDatabaseSteps.Safe(exception)}");
            return false;
        }

        if (request is null)
        {
            return false;
        }

        output.WriteLine($"{id} claim ok");
        try
        {
            await RunRequestAsync(row, steps, request);
        }
        catch (Exception exception)
        {
            output.WriteLine($"{id} claim failed: {TenantDatabaseSteps.Safe(exception)}");
        }

        return true;
    }

    private async Task<ClaimedRequest?> ClaimAsync(TenantDatabaseSteps steps, string tenant)
    {
        var connection = await steps.SchemaConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("key", ClaimLockKey);
            await lockCommand.ExecuteNonQueryAsync();
        }

        await FailExhaustedAsync(connection, transaction, tenant);

        ClaimedRequest? claimed = null;
        await using (var command = new NpgsqlCommand(ClaimSql, connection, transaction))
        {
            command.Parameters.AddWithValue("me", _claimedBy);
            command.Parameters.AddWithValue("lease", leaseDuration);
            command.Parameters.AddWithValue("max", MaxClaims);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                claimed = new ClaimedRequest(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    await reader.IsDBNullAsync(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    await reader.IsDBNullAsync(4) ? null : reader.GetString(4),
                    reader.GetString(5),
                    reader.GetInt32(6));
            }
        }

        await transaction.CommitAsync();
        return claimed;
    }

    // A running request that was claimed three times and whose lease ran out again ends failed. A publish gets its audit record.
    private async Task FailExhaustedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string tenant)
    {
        const string message = "The publisher stopped three times while running the request.";
        var exhausted = new List<(Guid Id, Guid ApplicationId, string RequestedBy, string? RequestedByName, string Kind)>();
        await using (var command = new NpgsqlCommand(ExhaustedSql, connection, transaction))
        {
            command.Parameters.Add(new NpgsqlParameter("diagnostics", NpgsqlDbType.Jsonb)
            {
                Value = DiagnosticsJson([new Diagnostic(DiagnosticCodes.PublishClaimsExhausted, message, "", "")]),
            });
            command.Parameters.AddWithValue("max", MaxClaims);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                exhausted.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), await reader.IsDBNullAsync(3) ? null : reader.GetString(3), reader.GetString(4)));
            }
        }

        foreach (var request in exhausted)
        {
            if (request.Kind == "publish")
            {
                await AppendAuditAsync(transaction, request.Id, request.ApplicationId, request.RequestedBy, request.RequestedByName, false, null);
            }

            output.WriteLine($"{tenant} claim failed: {DiagnosticCodes.PublishClaimsExhausted} {message}");
        }
    }

    private async Task RunRequestAsync(TenantRow row, TenantDatabaseSteps steps, ClaimedRequest request)
    {
        using var renewing = new CancellationTokenSource();
        var renewal = RenewAsync(steps, request, renewing.Token);
        try
        {
            await RunRequestCoreAsync(row, steps, request, renewing, renewal);
        }
        finally
        {
            await StopRenewalAsync(renewing, renewal);
        }
    }

    private async Task RunRequestCoreAsync(
        TenantRow row,
        TenantDatabaseSteps steps,
        ClaimedRequest request,
        CancellationTokenSource renewing,
        Task renewal)
    {
        var diagnostics = new List<Diagnostic>();
        var files = ParseSnapshot(request.Snapshot);
        var activation = new TenantReleaseActivation(settings.InlineRecomputeLimit, output, error, CancellationToken.None);
        if (request.Kind == "validate")
        {
            var valid = await activation.ValidateAsync(
                row,
                steps,
                _bindings[row.Id],
                files is null
                    ? null
                    : configuration => ReleaseCompiler.CompileAsync(files, configuration, settings.Packages.Versions, settings.Modules.Modules, CancellationToken.None),
                diagnostics);
            await StopRenewalAsync(renewing, renewal);
            await FinishAsync(row, steps, request, new ReleaseRun(valid, null), diagnostics, audit: false);
            return;
        }

        ReleaseRun run;
        if (files is null)
        {
            output.WriteLine($"{row.Id} compile failed: The snapshot cannot be read.");
            run = new ReleaseRun(false, null);
        }
        else
        {
            run = await activation.RunAsync(
                row,
                steps,
                _bindings[row.Id],
                configuration => ReleaseCompiler.CompileAsync(files, configuration, settings.Packages.Versions, settings.Modules.Modules, CancellationToken.None),
                diagnostics);
        }

        // The renewal stops before the final update, so it cannot extend a lease that the update has just ended.
        await StopRenewalAsync(renewing, renewal);
        await FinishAsync(row, steps, request, run, diagnostics, audit: true);
    }

    private static async Task StopRenewalAsync(CancellationTokenSource renewing, Task renewal)
    {
        await renewing.CancelAsync();
        try
        {
            await renewal;
        }
        catch (OperationCanceledException)
        {
            // The renewal ends with the request.
        }
    }

    // Renews the lease every third of its length on a connection of its own, because activation uses the main one. It stops
    // when no row changes: the request ended, or another publisher claimed it again.
    private async Task RenewAsync(TenantDatabaseSteps steps, ClaimedRequest request, CancellationToken token)
    {
        NpgsqlConnection? connection = null;
        try
        {
            using var timer = new PeriodicTimer(leaseDuration / 3);
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    connection ??= await steps.OpenSchemaConnectionAsync();
                    await using var command = new NpgsqlCommand(RenewSql, connection);
                    command.Parameters.AddWithValue("lease", leaseDuration);
                    command.Parameters.AddWithValue("id", request.Id);
                    command.Parameters.AddWithValue("claims", request.Claims);
                    if (await command.ExecuteNonQueryAsync(token) == 0)
                    {
                        return;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The next tick tries again, on a new connection when this one broke.
                    if (connection is not null)
                    {
                        await connection.DisposeAsync();
                        connection = null;
                    }
                }
            }
        }
        finally
        {
            if (connection is not null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    // The snapshot is an array of { file, version, content }, where content is the file's text as a JSON string. Null means
    // that it cannot be read: it is not such an array, or two entries name one file.
    private static List<ResourceContent>? ParseSnapshot(string? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshot);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var files = new List<ResourceContent>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("file", out var file) || file.ValueKind != JsonValueKind.String
                    || !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String
                    || !names.Add(file.GetString()!))
                {
                    return null;
                }

                files.Add(new ResourceContent(file.GetString()!, content.GetString()!));
            }

            return files;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The final status, the diagnostics and the audit record commit together, so a request never ends without its record. A
    // validate has no audit record. A publisher that lost its claim changes nothing and writes no record.
    private async Task FinishAsync(TenantRow row, TenantDatabaseSteps steps, ClaimedRequest request, ReleaseRun run, List<Diagnostic> diagnostics, bool audit)
    {
        var connection = await steps.SchemaConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        int changed;
        await using (var command = new NpgsqlCommand(FinishSql, connection, transaction))
        {
            command.Parameters.AddWithValue("status", run.Succeeded ? "succeeded" : "failed");
            command.Parameters.Add(new NpgsqlParameter("release", NpgsqlDbType.Uuid) { Value = run.ReleaseId is { } release ? release : DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("diagnostics", NpgsqlDbType.Jsonb) { Value = DiagnosticsJson(diagnostics) });
            command.Parameters.AddWithValue("id", request.Id);
            command.Parameters.AddWithValue("claims", request.Claims);
            changed = await command.ExecuteNonQueryAsync();
        }

        if (changed == 0)
        {
            await transaction.RollbackAsync();
            output.WriteLine($"{row.Id} claim failed: Another publisher claimed the request again.");
            return;
        }

        if (audit)
        {
            await AppendAuditAsync(transaction, request.Id, request.ApplicationId, request.RequestedBy, request.RequestedByName, run.Succeeded, run.ReleaseId);
        }

        await transaction.CommitAsync();
    }

    // The release history assigns the number, and it is not built yet, so the number stays null.
    private static Task AppendAuditAsync(
        NpgsqlTransaction transaction,
        Guid requestId,
        Guid applicationId,
        string requestedBy,
        string? requestedByName,
        bool succeeded,
        Guid? releaseId) =>
        AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                requestedBy,
                succeeded ? "release.published" : "release.publishFailed",
                applicationId,
                null,
                null,
                null,
                new JsonObject { ["requestId"] = requestId, ["releaseId"] = releaseId, ["number"] = null },
                requestedByName));

    // Each entry has the shape of a Studio diagnostic.
    private static string DiagnosticsJson(List<Diagnostic> diagnostics) =>
        new JsonArray([.. diagnostics.Select(diagnostic => (JsonNode)new JsonObject
        {
            ["code"] = diagnostic.Code,
            ["severity"] = diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning",
            ["message"] = diagnostic.Message,
            ["file"] = diagnostic.File,
            ["path"] = diagnostic.Path,
            ["resourceId"] = diagnostic.ResourceId?.ToString("D"),
        })]).ToJsonString();

    private sealed record ClaimedRequest(Guid Id, Guid ApplicationId, string? Snapshot, string RequestedBy, string? RequestedByName, string Kind, int Claims);
}
