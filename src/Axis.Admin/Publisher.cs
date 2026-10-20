using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Admin;

/// <summary>
/// <c>publisher</c>: runs the <c>publish</c>, <c>validate</c>, <c>rollback</c> and <c>test</c> requests that Studio stores in the tenant
/// databases, until it is stopped. Each poll round it claims the oldest queued request of every active tenant that is on this build's schema
/// version. A <c>publish</c> runs the steps of <c>activate</c>, compiling from the stored snapshot and never from a folder. A
/// <c>validate</c> runs the compile and every activation check in a transaction that it rolls back. A <c>rollback</c> runs the
/// same steps with the stored release in place of the compile: it rebuilds the release's model from its stored files, refuses
/// a release that pins a package version that is not installed with <c>AXC0146</c>, and then activates it with every
/// activation check, so a migration that the release lacks is <c>AXC0174</c>. It never reads a folder and stores no new
/// release. A request ends <c>succeeded</c> or <c>failed</c>, with its diagnostics. A <c>publish</c> and a <c>rollback</c> write
/// their audit record in the same transaction, and a <c>validate</c> writes none. A request of another tenant stays queued.
/// A <c>publish</c> request with target <c>preview</c> runs the same steps on the tenant's preview tenant, with that tenant's
/// database, schema role and bindings, and writes its history row there. The request itself stays in the tenant's database,
/// where its final status and diagnostics go, and it writes no audit record. Without a preview tenant it fails with
/// <c>AXC0216</c>. While the preview tenant exists but is not served, the request stays queued. A <c>validate</c> or
/// <c>rollback</c> with target <c>preview</c> is never claimed.
/// A <c>test</c> compiles the snapshot and runs its test scenarios, each in its own temporary database that the scenario role
/// of <c>Scenarios:ServerConnectionString</c> creates, and stores each scenario's outcome on the request. It touches no
/// tenant database except to store the request's status and results, and writes no audit record and no release id. Without the
/// setting it fails with <c>AXC0217</c>, and an unknown scenario name fails it with <c>AXC0218</c>.
/// The first three kinds share one queue for each application, oldest first. The <c>test</c> requests form a queue of their
/// own for each application, and run in a second loop of this process, so a long test holds up no publish and a publish holds
/// up no test. While a request runs,
/// the publisher renews its lease. A running request whose lease has run out is claimed again, and the third such claim to
/// run out ends it <c>failed</c> with <c>AXC0215</c>. The <c>claims</c> count fences the final update, so a publisher that
/// lost its claim changes nothing. Every step runs as the tenant's schema role, and a stop finishes the request that is
/// running, then ends the loop. The tenants come from the tenant directory or from the <c>Tenants</c> configuration section,
/// see <see cref="IPublisherTenants"/>.
/// </summary>
internal sealed class Publisher(
    ReleaseSettings settings,
    string? scenarioServer,
    IPublisherTenants tenants,
    TimeSpan leaseDuration,
    TextWriter output,
    TextWriter error,
    CancellationToken stopping)
{
    // Both loops write to these.
    private readonly TextWriter _output = TextWriter.Synchronized(output);
    private readonly TextWriter _error = TextWriter.Synchronized(error);

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
        WHERE kind IN ('publish', 'validate', 'rollback', 'test') AND status = 'running' AND lease_until < clock_timestamp() AND claims >= @max
        RETURNING id, application_id, requested_by, requested_by_name, kind, release_id, target
        """;

    // clock_timestamp() and not now(): now() is fixed when the transaction starts, before the wait for the lock.
    private const string ClaimSql =
        """
        WITH next AS (
          SELECT r.id FROM axis.publish_requests r
          WHERE r.kind IN ('publish', 'validate', 'rollback', 'test') AND (r.kind = 'test') = @tests
            AND (r.status = 'queued'
                 OR (r.status = 'running' AND r.lease_until < clock_timestamp() AND r.claims < @max))
            AND (r.target = 'tenant' OR (r.target = 'preview' AND r.kind = 'publish' AND @preview))
            AND NOT EXISTS (SELECT 1 FROM axis.publish_requests o
                            WHERE o.application_id = r.application_id AND o.status = 'running' AND o.id <> r.id
                              AND (o.kind = 'test') = (r.kind = 'test'))
          ORDER BY r.requested_at, r.id LIMIT 1
          FOR UPDATE SKIP LOCKED)
        UPDATE axis.publish_requests p
        SET status = 'running', claimed_by = @me, lease_until = clock_timestamp() + @lease,
            claims = p.claims + 1, started_at = coalesce(p.started_at, clock_timestamp())
        FROM next WHERE p.id = next.id
        RETURNING p.id, p.application_id, p.snapshot::text, p.requested_by, p.requested_by_name, p.kind, p.claims, p.label, p.note, p.release_id, p.target, p.scenario_names::text
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
        SET status = @status, finished_at = now(), release_id = @release, diagnostics = @diagnostics, scenario_results = @scenarios
        WHERE id = @id AND status = 'running' AND claims = @claims
        """;

    private volatile IReadOnlyList<PublisherTenant> _served = [];

    public async Task<int> RunAsync()
    {
        try
        {
            var problem = await tenants.CheckAsync(stopping);
            if (problem is not null)
            {
                _error.WriteLine(problem);
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
            _error.WriteLine($"The tenant directory could not be read: {TenantDatabaseSteps.Safe(exception)}");
            return 1;
        }

        // Two loops, so a long test holds up no publish. Only the first reads the tenants again.
        await Task.WhenAll(LoopAsync(tests: false), LoopAsync(tests: true));
        return 0;
    }

    private async Task LoopAsync(bool tests)
    {
        var lastRead = Stopwatch.StartNew();
        while (!stopping.IsCancellationRequested)
        {
            if (!tests && lastRead.Elapsed >= tenants.ReadInterval)
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
                    _error.WriteLine($"The tenant directory could not be read: {TenantDatabaseSteps.Safe(exception)}");
                }
            }

            var claimed = false;
            foreach (var tenant in _served)
            {
                if (stopping.IsCancellationRequested)
                {
                    break;
                }

                claimed |= await RunNextAsync(tenant, tests);
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
    }

    // Claims and runs one request of the tenant. Returns whether one was claimed.
    private async Task<bool> RunNextAsync(PublisherTenant tenant, bool tests)
    {
        var id = tenant.Id;
        await using var connections = tenant.Connect();
        ClaimedRequest? request;
        try
        {
            request = await ClaimAsync(connections, tenant, tests);
        }
        catch (Exception exception)
        {
            _output.WriteLine($"{id} claim failed: {TenantDatabaseSteps.Safe(exception)}");
            return false;
        }

        if (request is null)
        {
            return false;
        }

        _output.WriteLine($"{id} claim ok");
        try
        {
            await RunRequestAsync(tenant, connections, request);
        }
        catch (Exception exception)
        {
            _output.WriteLine($"{id} claim failed: {TenantDatabaseSteps.Safe(exception)}");
        }

        return true;
    }

    private async Task<ClaimedRequest?> ClaimAsync(ITenantConnections connections, PublisherTenant tenant, bool tests)
    {
        var connection = await connections.SchemaConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("key", ClaimLockKey);
            await lockCommand.ExecuteNonQueryAsync();
        }

        await FailExhaustedAsync(connection, transaction, tenant.Id);

        ClaimedRequest? claimed = null;
        await using (var command = new NpgsqlCommand(ClaimSql, connection, transaction))
        {
            command.Parameters.AddWithValue("me", _claimedBy);
            command.Parameters.AddWithValue("lease", leaseDuration);
            command.Parameters.AddWithValue("max", MaxClaims);
            command.Parameters.AddWithValue("tests", tests);
            // A preview request waits while its preview tenant exists but is not served. Without a preview tenant it is claimed, to fail.
            command.Parameters.AddWithValue("preview", !tenant.HasPreview || tenant.Preview is not null);
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
                    reader.GetInt32(6),
                    await reader.IsDBNullAsync(7) ? null : reader.GetString(7),
                    await reader.IsDBNullAsync(8) ? null : reader.GetString(8),
                    await reader.IsDBNullAsync(9) ? null : reader.GetGuid(9),
                    reader.GetString(10),
                    await reader.IsDBNullAsync(11) ? null : reader.GetString(11));
            }
        }

        await transaction.CommitAsync();
        return claimed;
    }

    // A running request that was claimed three times and whose lease ran out again ends failed. A publish and a rollback on the tenant get their audit record.
    private async Task FailExhaustedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string tenant)
    {
        const string message = "The publisher stopped three times while running the request.";
        var exhausted = new List<(Guid Id, Guid ApplicationId, string RequestedBy, string? RequestedByName, string Kind, Guid? ReleaseId, string Target)>();
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
                exhausted.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), await reader.IsDBNullAsync(3) ? null : reader.GetString(3), reader.GetString(4), await reader.IsDBNullAsync(5) ? null : reader.GetGuid(5), reader.GetString(6)));
            }
        }

        foreach (var request in exhausted)
        {
            if (request.Kind is "publish" or "rollback" && request.Target == "tenant")
            {
                await AppendAuditAsync(transaction, request.Kind, request.Id, request.ApplicationId, request.RequestedBy, request.RequestedByName, false, request.ReleaseId);
            }

            _output.WriteLine($"{tenant} claim failed: {DiagnosticCodes.PublishClaimsExhausted} {message}");
        }
    }

    private async Task RunRequestAsync(PublisherTenant tenant, ITenantConnections connections, ClaimedRequest request)
    {
        using var renewing = new CancellationTokenSource();
        var renewal = RenewAsync(connections, request, renewing.Token);
        try
        {
            await RunRequestCoreAsync(tenant, connections, request, renewing, renewal);
        }
        finally
        {
            await StopRenewalAsync(renewing, renewal);
        }
    }

    private async Task RunRequestCoreAsync(
        PublisherTenant tenant,
        ITenantConnections connections,
        ClaimedRequest request,
        CancellationTokenSource renewing,
        Task renewal)
    {
        var diagnostics = new List<Diagnostic>();
        var files = ParseSnapshot(request.Snapshot);
        var activation = new TenantReleaseActivation(settings.InlineRecomputeLimit, _output, _error, CancellationToken.None);
        var preview = request.Target == "preview";
        if (preview && tenant.Preview is null)
        {
            const string missing = "The tenant has no preview tenant.";
            diagnostics.Add(new Diagnostic(DiagnosticCodes.PreviewTenantMissing, missing, "", ""));
            _output.WriteLine($"{tenant.Id} preview failed: {DiagnosticCodes.PreviewTenantMissing} {missing}");
            await StopRenewalAsync(renewing, renewal);
            await FinishAsync(tenant.Id, connections, request, new ReleaseRun(false, null), diagnostics, audit: false);
            return;
        }

        if (request.Kind == "test")
        {
            var tested = await activation.TestAsync(
                tenant.Id,
                tenant.SchemaVersion,
                scenarioServer,
                files,
                request.ScenarioNames,
                settings.Packages,
                settings.Modules,
                diagnostics);
            await StopRenewalAsync(renewing, renewal);
            await FinishAsync(tenant.Id, connections, request, new ReleaseRun(tested.Succeeded, null), diagnostics, audit: false, tested.Scenarios.ToJsonString());
            return;
        }

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
            await StopRenewalAsync(renewing, renewal);
            await FinishAsync(tenant.Id, connections, request, new ReleaseRun(valid, null), diagnostics, audit: false);
            return;
        }

        if (request.Kind == "rollback")
        {
            var rolledBack = await activation.RunAsync(
                tenant.Id,
                tenant.SchemaVersion,
                connections,
                tenant.Bindings,
                configuration => LoadStoredAsync(configuration, request, tenant.Id),
                diagnostics,
                ActivationActor.User(request.RequestedBy, request.RequestedByName, null, null, request.Id),
                loadStep: "rollback");
            await StopRenewalAsync(renewing, renewal);
            await FinishAsync(tenant.Id, connections, request, rolledBack, diagnostics, audit: true);
            return;
        }

        // A preview publish runs on the preview tenant's database. The lease, the final status and the diagnostics stay on the
        // tenant's own connections, where the request lives.
        var target = preview ? tenant.Preview! : tenant;
        await using var previewConnections = preview ? target.Connect() : null;
        ReleaseRun run;
        if (files is null)
        {
            _output.WriteLine($"{target.Id} compile failed: The snapshot cannot be read.");
            run = new ReleaseRun(false, null);
        }
        else
        {
            run = await activation.RunAsync(
                target.Id,
                target.SchemaVersion,
                previewConnections ?? connections,
                target.Bindings,
                configuration => ReleaseCompiler.CompileAsync(files, configuration, settings.Packages.Versions, settings.Modules.Modules, CancellationToken.None),
                diagnostics,
                ActivationActor.User(request.RequestedBy, request.RequestedByName, request.Label, request.Note, request.Id));
        }

        // The renewal stops before the final update, so it cannot extend a lease that the update has just ended.
        await StopRenewalAsync(renewing, renewal);
        await FinishAsync(tenant.Id, connections, request, run, diagnostics, audit: !preview);
    }

    // The release of a rollback, rebuilt from its stored files as the server does when it serves a release. A release that
    // is missing, or that belongs to another application, is a failure of the step, and so is a failure to rebuild it.
    private async Task<ReleaseCompilationResult> LoadStoredAsync(ConfigurationDbContext configuration, ClaimedRequest request, string tenantId)
    {
        var release = request.ReleaseId is { } releaseId
            ? await new ActiveReleaseStore(configuration).GetReleaseAsync(releaseId)
            : null;
        if (release is null || release.ApplicationId != request.ApplicationId)
        {
            throw new InvalidOperationException("The release to roll back to is not stored for this application.");
        }

        var missing = PinnedReleaseCheck.MissingPins(release, tenantId, settings.Packages.Versions);
        return missing.Count > 0
            ? new ReleaseCompilationResult(null, null, missing)
            : new ReleaseCompilationResult(release, ReleaseCompiler.BuildModel(release, settings.Packages.Versions), []);
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
    private async Task RenewAsync(ITenantConnections connections, ClaimedRequest request, CancellationToken token)
    {
        NpgsqlConnection? connection = null;
        try
        {
            using var timer = new PeriodicTimer(leaseDuration / 3);
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    connection ??= await connections.OpenSchemaConnectionAsync();
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
    private async Task FinishAsync(string tenantId, ITenantConnections connections, ClaimedRequest request, ReleaseRun run, List<Diagnostic> diagnostics, bool audit, string? scenarioResults = null)
    {
        var connection = await connections.SchemaConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        int changed;
        await using (var command = new NpgsqlCommand(FinishSql, connection, transaction))
        {
            command.Parameters.AddWithValue("status", run.Succeeded ? "succeeded" : "failed");
            command.Parameters.Add(new NpgsqlParameter("release", NpgsqlDbType.Uuid) { Value = ReleaseOf(request, run) is { } release ? release : DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("diagnostics", NpgsqlDbType.Jsonb) { Value = DiagnosticsJson(diagnostics) });
            command.Parameters.Add(new NpgsqlParameter("scenarios", NpgsqlDbType.Jsonb) { Value = (object?)scenarioResults ?? DBNull.Value });
            command.Parameters.AddWithValue("id", request.Id);
            command.Parameters.AddWithValue("claims", request.Claims);
            changed = await command.ExecuteNonQueryAsync();
        }

        if (changed == 0)
        {
            await transaction.RollbackAsync();
            _output.WriteLine($"{tenantId} claim failed: Another publisher claimed the request again.");
            return;
        }

        if (audit)
        {
            await AppendAuditAsync(transaction, request.Kind, request.Id, request.ApplicationId, request.RequestedBy, request.RequestedByName, run.Succeeded, ReleaseOf(request, run));
        }

        await transaction.CommitAsync();
    }

    // A rollback names its release from the start, so a failed one still has it. A publish has it once it is compiled.
    private static Guid? ReleaseOf(ClaimedRequest request, ReleaseRun run) =>
        request.Kind == "rollback" ? request.ReleaseId : run.ReleaseId;

    // The number is the release's number in the release history. It is null when the request failed before a release was
    // known, or when the release was never activated.
    private static async Task AppendAuditAsync(
        NpgsqlTransaction transaction,
        string kind,
        Guid requestId,
        Guid applicationId,
        string requestedBy,
        string? requestedByName,
        bool succeeded,
        Guid? releaseId)
    {
        int? number = null;
        if (releaseId is { } id)
        {
            await using var command = new NpgsqlCommand(
                "SELECT number FROM axis.release_activations WHERE application_id = @application AND release_id = @release LIMIT 1",
                transaction.Connection,
                transaction);
            command.Parameters.AddWithValue("application", applicationId);
            command.Parameters.AddWithValue("release", id);
            number = await command.ExecuteScalarAsync() as int?;
        }

        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                requestedBy,
                kind == "rollback"
                    ? succeeded ? "release.rolledBack" : "release.rollbackFailed"
                    : succeeded ? "release.published" : "release.publishFailed",
                applicationId,
                null,
                null,
                null,
                new JsonObject { ["requestId"] = requestId, ["releaseId"] = releaseId, ["number"] = number },
                requestedByName));
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

    private sealed record ClaimedRequest(Guid Id, Guid ApplicationId, string? Snapshot, string RequestedBy, string? RequestedByName, string Kind, int Claims, string? Label, string? Note, Guid? ReleaseId, string Target, string? ScenarioNames);
}
