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
/// and a <c>validate</c> writes none. A request of another tenant, and a <c>rollback</c>, stays queued. Every step runs as
/// the tenant's schema role, and a stop finishes the request that is running, then ends the loop. The tenants come from the
/// tenant directory or from the <c>Tenants</c> configuration section, see <see cref="IPublisherTenants"/>.
/// </summary>
internal sealed class Publisher(
    ReleaseSettings settings,
    IPublisherTenants tenants,
    TextWriter output,
    TextWriter error,
    CancellationToken stopping)
{
    // The lease is not renewed yet, and a running request whose lease has run out is not claimed again.
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private static readonly string _claimedBy = $"{Environment.MachineName}-{Environment.ProcessId}";

    private const string ClaimSql =
        """
        WITH next AS (
          SELECT r.id FROM axis.publish_requests r
          WHERE r.status = 'queued' AND r.kind IN ('publish', 'validate')
            AND NOT EXISTS (SELECT 1 FROM axis.publish_requests o
                            WHERE o.application_id = r.application_id AND o.status = 'running')
          ORDER BY r.requested_at, r.id LIMIT 1
          FOR UPDATE SKIP LOCKED)
        UPDATE axis.publish_requests p
        SET status = 'running', claimed_by = @me, lease_until = now() + @lease,
            claims = p.claims + 1, started_at = coalesce(p.started_at, now())
        FROM next WHERE p.id = next.id
        RETURNING p.id, p.application_id, p.snapshot::text, p.requested_by, p.requested_by_name, p.kind
        """;

    private const string FinishSql =
        """
        UPDATE axis.publish_requests
        SET status = @status, finished_at = now(), release_id = @release, diagnostics = @diagnostics
        WHERE id = @id
        """;

    private IReadOnlyList<PublisherTenant> _served = [];

    public async Task<int> RunAsync()
    {
        try
        {
            var problem = await tenants.CheckAsync(stopping);
            if (problem is not null)
            {
                error.WriteLine(problem);
                return 1;
            }

            _served = await tenants.ReadAsync(stopping);
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
            if (lastRead.Elapsed >= tenants.ReadInterval)
            {
                lastRead.Restart();
                try
                {
                    _served = await tenants.ReadAsync(stopping);
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
            foreach (var tenant in _served)
            {
                if (stopping.IsCancellationRequested)
                {
                    break;
                }

                claimed |= await RunNextAsync(tenant);
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

    // Claims and runs one request of the tenant. Returns whether one was claimed.
    private async Task<bool> RunNextAsync(PublisherTenant tenant)
    {
        var id = tenant.Id;
        await using var connections = tenant.Connect();
        ClaimedRequest? request;
        try
        {
            request = await ClaimAsync(connections);
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
            await RunRequestAsync(tenant, connections, request);
        }
        catch (Exception exception)
        {
            output.WriteLine($"{id} claim failed: {TenantDatabaseSteps.Safe(exception)}");
        }

        return true;
    }

    private static async Task<ClaimedRequest?> ClaimAsync(ITenantConnections connections)
    {
        var connection = await connections.SchemaConnectionAsync();
        await using var command = new NpgsqlCommand(ClaimSql, connection);
        command.Parameters.AddWithValue("me", _claimedBy);
        command.Parameters.AddWithValue("lease", LeaseDuration);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ClaimedRequest(
            reader.GetGuid(0),
            reader.GetGuid(1),
            await reader.IsDBNullAsync(2) ? null : reader.GetString(2),
            reader.GetString(3),
            await reader.IsDBNullAsync(4) ? null : reader.GetString(4),
            reader.GetString(5));
    }

    private async Task RunRequestAsync(PublisherTenant tenant, ITenantConnections connections, ClaimedRequest request)
    {
        var diagnostics = new List<Diagnostic>();
        var files = ParseSnapshot(request.Snapshot);
        var activation = new TenantReleaseActivation(settings.InlineRecomputeLimit, output, error, CancellationToken.None);
        if (request.Kind == "validate")
        {
            var valid = await activation.ValidateAsync(
                tenant.Id,
                tenant.SchemaVersion,
                connections,
                tenant.Bindings,
                files is null
                    ? null
                    : configuration => ReleaseCompiler.CompileAsync(files, configuration, settings.Packages.Versions, settings.Modules.Modules, CancellationToken.None),
                diagnostics);
            await FinishAsync(connections, request, new ReleaseRun(valid, null), diagnostics, audit: false);
            return;
        }

        ReleaseRun run;
        if (files is null)
        {
            output.WriteLine($"{tenant.Id} compile failed: The snapshot cannot be read.");
            run = new ReleaseRun(false, null);
        }
        else
        {
            run = await activation.RunAsync(
                tenant.Id,
                tenant.SchemaVersion,
                connections,
                tenant.Bindings,
                configuration => ReleaseCompiler.CompileAsync(files, configuration, settings.Packages.Versions, settings.Modules.Modules, CancellationToken.None),
                diagnostics);
        }

        await FinishAsync(connections, request, run, diagnostics, audit: true);
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
    // validate has no audit record.
    private static async Task FinishAsync(ITenantConnections connections, ClaimedRequest request, ReleaseRun run, List<Diagnostic> diagnostics, bool audit)
    {
        var connection = await connections.SchemaConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand(FinishSql, connection, transaction))
        {
            command.Parameters.AddWithValue("status", run.Succeeded ? "succeeded" : "failed");
            command.Parameters.Add(new NpgsqlParameter("release", NpgsqlDbType.Uuid) { Value = run.ReleaseId is { } release ? release : DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("diagnostics", NpgsqlDbType.Jsonb) { Value = DiagnosticsJson(diagnostics) });
            command.Parameters.AddWithValue("id", request.Id);
            await command.ExecuteNonQueryAsync();
        }

        if (audit)
        {
            // The release history assigns the number, and it is not built yet, so the number stays null.
            await AuditRecords.AppendAsync(
                transaction,
                new AuditEntry(
                    request.RequestedBy,
                    run.Succeeded ? "release.published" : "release.publishFailed",
                    request.ApplicationId,
                    null,
                    null,
                    null,
                    new JsonObject { ["requestId"] = request.Id, ["releaseId"] = run.ReleaseId, ["number"] = null },
                    request.RequestedByName));
        }

        await transaction.CommitAsync();
    }

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

    private sealed record ClaimedRequest(Guid Id, Guid ApplicationId, string? Snapshot, string RequestedBy, string? RequestedByName, string Kind);
}
