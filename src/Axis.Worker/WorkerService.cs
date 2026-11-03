using Axis.Configuration.Diagnostics;
using Axis.Configuration.Packages;
using Axis.Core.Tenancy;
using Axis.Data.Files;
using Axis.Data.Records;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Npgsql;

namespace Axis.Worker;

/// <summary>
/// Under the directory source it first reads the tenant directory, and retries until a read works. Then it runs
/// <c>Worker:Concurrency</c> loops and one poll. The poll runs every <c>Worker:PollInterval</c>, and the loops share the
/// active list of <see cref="TenantRounds"/>: the tenants that have work due, which get <c>Worker:Quantum</c> work items times
/// their <c>workerWeight</c> in each turn. A loop takes the tenant whose turn it is, claims and runs one of its work items, and
/// takes the next. A tenant leaves the list when a claim finds nothing due and none of its other items runs, or when a run fails, and the next poll puts it back.
/// So a tenant with a large backlog does not hold back the others, and a tenant alone runs at full speed.
/// In a cell a call waits when its tenant has no <c>concurrentConnectorCalls</c> lease free: the claim leaves it due, the tenant's
/// turn ends, and the tenant stays in the list, idle until one of its items ends or the next poll.
/// The poll checks the tenants in ordinal order of their ids, then adds every tenant that is ready to the list in one step.
/// A tenant whose database the server has not migrated yet is skipped until it has. Under the directory source the poll
/// reads the directory again every <c>Tenancy:RefreshInterval</c>. The refresh pauses the loops, waits for the work items
/// already running, and drops the pools of the tenants that changed or left, so no pool is dropped while one of its items runs.
/// A tenant that is no longer active gets no new work item claimed, and a new tenant is polled once its runtime role passes the
/// check. The poll also reads the recorded schema version of every tenant, and claims no work of a tenant whose version is not
/// the worker's, until the admin tool's <c>migrate</c> has brought it there. When a tenant's database counts as migrated,
/// its runtime database role is checked, so that it can only read and write rows. Then the releases
/// the tenant still uses are checked against the installed extension packages. A role that can change
/// the schema, a pinned version that is not installed, or a release that no longer compiles records
/// the failure in <see cref="StartupCheckState"/> and stops the host before any work of that tenant runs.
/// Once a tenant is ready, the poll also deletes its expired sessions and sweeps its deleted and abandoned files,
/// and clears the files of its imports uploaded more than 24 hours ago, each at most every five minutes by its own clock,
/// and deletes its old in-app notifications every 24 hours. In a cell it also measures the storage of each ready tenant every
/// hour and records the sample in the platform database as the tenant's usage. That housekeeping is outside the rounds.
/// </summary>
internal sealed partial class WorkerService(
    WorkerTenantCatalog tenants,
    WorkerOptions options,
    NotificationOptions notifications,
    WorkItemRunner runner,
    WorkerHealth health,
    ITenantConnectionFactory connections,
    TenantConnectionFactory pools,
    TimeProvider time,
    ITenantContextAccessor accessor,
    TenantQuotas quotas,
    PackageCatalog packages,
    StartupCheckState checkState,
    CellOptions cell,
    ILogger<WorkerService> logger,
    TenantUsageStore? usage = null) : BackgroundService
{
    // Tenants whose database was migrated. A new schema arrives with a new release, which restarts
    // the worker, so a ready tenant is not checked again. A tenant whose database settings change, or that
    // leaves the directory, is dropped from the set and checked again when it is served again.
    private readonly HashSet<string> _ready = new(StringComparer.Ordinal);
    private readonly HashSet<string> _waitingLogged = new(StringComparer.Ordinal);
    private readonly HashSet<string> _schemaWaiting = new(StringComparer.Ordinal);

    // The recorded schema versions of the last good read. Null under the configuration source, and before a read worked.
    private IReadOnlyDictionary<string, string>? _schemaVersions;
    private bool _schemaReadFailureLogged;
    private DateTimeOffset _lastRefresh;

    // The tenants with work due, shared by the loops. In a cell a tenant's weight is its workerWeight quota, otherwise 1.
    // A weight that is missing, null or below 1 counts as 1, so no tenant with work due starves.
    private readonly TenantRounds _rounds = new(options.Quantum, tenantId => Math.Max(1, quotas.Limit(tenantId, QuotaKeys.WorkerWeight) ?? 1));

    // A loop holds one permit while it claims and runs an item. A refresh of the directory holds them all.
    private readonly SemaphoreSlim _running = new(options.Concurrency);

    // The time of the last clean-up of expired sessions of each ready tenant. It is a fixed interval, short next to the idle limit.
    private static readonly TimeSpan _sessionCleanupInterval = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, DateTimeOffset> _sessionsCleanedAt = new(StringComparer.Ordinal);

    // The time of the last sweep of deleted and abandoned files of each ready tenant. It has its own timer, so neither clean-up delays the other.
    private static readonly TimeSpan _fileSweepInterval = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, DateTimeOffset> _filesSweptAt = new(StringComparer.Ordinal);

    // The files of imports uploaded more than 24 hours ago are cleared at most every five minutes. It has its own timer, so no other clean-up delays it.
    private readonly TenantSchedule _importFilesCleared = new(TimeSpan.FromMinutes(5), time);

    // The old in-app notifications of each ready tenant are deleted once a day. It has its own timer, so no other clean-up delays it.
    private readonly TenantSchedule _notificationsCleaned = new(NotificationRetention.Interval, time);

    // The storage of each ready tenant is measured once an hour in a cell. It has its own timer, so no other clean-up delays it.
    private readonly TenantSchedule _usageSampled = new(TimeSpan.FromHours(1), time);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (tenants.TenantsSectionIgnored)
        {
            LogTenantsSectionIgnored();
        }

        await LoadTenantsAsync(stoppingToken);
        _lastRefresh = time.GetUtcNow();
        using var stopLoops = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var loops = Enumerable.Range(0, options.Concurrency)
            .Select(loop => Task.Run(() => RunLoopAsync(loop, stopLoops.Token), CancellationToken.None))
            .ToArray();
        try
        {
            await PollAsync(stoppingToken);
        }
        finally
        {
            // A StartupCheckException from the poll still stops the host, after the loops have ended.
            await stopLoops.CancelAsync();
            try
            {
                await Task.WhenAll(loops);
            }
            catch (OperationCanceledException)
            {
                // The loops ended.
            }
        }
    }

    /// <summary>
    /// Every poll interval, checks the tenants, runs their housekeeping, and puts the tenants that are ready in the active list.
    /// It is the only code that changes the ready state, so none of it is shared with the loops.
    /// </summary>
    private async Task PollAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            health.Polled();
            await ReadSchemaVersionsAsync(stoppingToken);
            await RefreshIfDueAsync(stoppingToken);

            // Every check comes first, so the first tenant does not run alone while the checks of the next one run.
            var ready = new List<string>();
            var tenantIds = tenants.Tenants.Select(tenant => tenant.Id).Order(StringComparer.Ordinal).ToArray();
            foreach (var tenantId in tenantIds)
            {
                using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId }))
                {
                    if (await CheckTenantAsync(tenantId, stoppingToken))
                    {
                        ready.Add(tenantId);
                    }
                }
            }

            foreach (var tenantId in ready)
            {
                using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId }))
                {
                    await RunHousekeepingAsync(tenantId, stoppingToken);
                }
            }

            _rounds.Join(ready);
            await Task.Delay(options.PollInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Takes the tenant whose turn it is, and claims and runs one of its work items. A tenant leaves the list when the claim
    /// finds nothing due or the run fails. The next poll puts it back, so a database that is down is not claimed in a tight loop.
    /// </summary>
    private async Task RunLoopAsync(int loop, CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_rounds.TryTake(out var tenantId, out var ticket))
                {
                    await _rounds.WaitForWorkAsync(stoppingToken);
                    continue;
                }

                await _running.WaitAsync(stoppingToken);
                try
                {
                    using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId }))
                    {
                        await RunNextAsync(loop, tenantId, ticket, stoppingToken);
                    }
                }
                finally
                {
                    _running.Release();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The worker stops.
        }
    }

    private async Task RunNextAsync(int loop, string tenantId, long ticket, CancellationToken stoppingToken)
    {
        var found = false;
        var waiting = false;
        var failed = false;
        try
        {
            // A tenant that left the directory at a refresh gets no more work.
            if (tenants.Find(tenantId) is null)
            {
                failed = true;
                return;
            }

            health.ItemStarted(loop);
            try
            {
                var next = await runner.TryRunNextAsync(tenantId, options.LeaseDuration, stoppingToken);
                found = next == NextRun.Ran;
                waiting = next == NextRun.Waiting;
            }
            finally
            {
                health.ItemFinished(loop);
            }
        }
        catch (Exception exception) when (exception is not StartupCheckException
            && (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested))
        {
            // A claimed item reappears when its lease expires.
            LogRunFailed(exception, tenantId);
            failed = true;
        }
        finally
        {
            // An empty claim only takes the tenant out of the list when none of its other items runs, because a running item may queue the next one.
            if (failed)
            {
                _rounds.Fail(tenantId);
            }
            else if (waiting)
            {
                _rounds.Wait(tenantId);
            }
            else
            {
                _rounds.Done(tenantId, ticket, found);
            }
        }
    }

    /// <summary>
    /// Under the directory source, reads the tenants and retries every poll interval until a read works. Only the
    /// first failure is logged, and readiness shows that the problem continues.
    /// </summary>
    private async Task LoadTenantsAsync(CancellationToken stoppingToken)
    {
        var failureLogged = false;
        while (!tenants.IsLoaded)
        {
            health.Polled();
            try
            {
                await tenants.LoadAsync(stoppingToken);
            }
            catch (InvalidOperationException exception)
            {
                if (!failureLogged)
                {
                    failureLogged = true;
                    LogLoadFailed(exception, options.PollInterval);
                }

                await Task.Delay(options.PollInterval, stoppingToken);
            }
        }

        if (tenants.FromDirectorySource)
        {
            LogLoaded(tenants.Tenants.Count);
        }
    }

    /// <summary>
    /// Under the directory source, reads the schema versions again. When a read fails, the worker keeps the last good read,
    /// and the log holds the failure once until a read works.
    /// </summary>
    private async Task ReadSchemaVersionsAsync(CancellationToken stoppingToken)
    {
        try
        {
            var versions = await tenants.ReadSchemaVersionsAsync(stoppingToken);
            if (versions is not null)
            {
                _schemaVersions = versions;
            }

            _schemaReadFailureLogged = false;
        }
        catch (InvalidOperationException exception)
        {
            if (!_schemaReadFailureLogged)
            {
                _schemaReadFailureLogged = true;
                LogSchemaReadFailed(exception);
            }
        }
    }

    // A tenant that has no recorded version in the read is not held back: the read lists the active tenants only.
    private bool IsOnOtherSchemaVersion(string tenantId) =>
        _schemaVersions is not null
        && _schemaVersions.TryGetValue(tenantId, out var version)
        && !string.Equals(version, TenantSchemaVersion.Current, StringComparison.Ordinal);

    /// <summary>
    /// Under the directory source, reads the directory again once the refresh interval has passed since the last read.
    /// A tenant that changed or left loses its old connection pool and its ready state and leaves the active list. The loops
    /// pause until the work items already running have ended, so no pool is dropped while one of its items runs.
    /// </summary>
    private async Task RefreshIfDueAsync(CancellationToken stoppingToken)
    {
        if (!tenants.FromDirectorySource || time.GetUtcNow() - _lastRefresh < tenants.RefreshInterval)
        {
            return;
        }

        var held = 0;
        try
        {
            for (; held < options.Concurrency; held++)
            {
                await _running.WaitAsync(stoppingToken);
            }

            var result = await tenants.RefreshAsync(PassesRoleCheckAsync, stoppingToken);
            foreach (var tenantId in result.Retired)
            {
                _rounds.Leave(tenantId);
                await pools.RetireAsync(tenantId);
                _ready.Remove(tenantId);
                _waitingLogged.Remove(tenantId);
                _schemaWaiting.Remove(tenantId);
                _sessionsCleanedAt.Remove(tenantId);
                _filesSweptAt.Remove(tenantId);
                _importFilesCleared.Forget(tenantId);
                _notificationsCleaned.Forget(tenantId);
                _usageSampled.Forget(tenantId);
                health.TenantDropped(tenantId);
            }

            LogRefreshed(result.TenantCount, result.CheckedCount, result.WithheldCount);
        }
        catch (InvalidOperationException exception)
        {
            LogRefreshFailed(exception.InnerException?.GetType().Name ?? exception.GetType().Name);
        }
        finally
        {
            _running.Release(held);
            _lastRefresh = time.GetUtcNow();
        }
    }

    private async Task<bool> PassesRoleCheckAsync(TenantEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await using var source = NpgsqlDataSource.Create(entry.ConnectionString.Reveal());
            await using var connection = await source.OpenConnectionAsync(cancellationToken);
            await RuntimeRoleCheck.CheckAsync(connection, entry.Id, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NpgsqlException)
        {
            // The role check names the tenant and the role. A connection failure may name the database host, so it is not logged.
            LogTenantNotServed(entry.Id, exception is InvalidOperationException ? exception.Message : "The tenant database could not be reached.");
            return false;
        }
    }

    /// <summary>
    /// Checks whether the tenant can run work: it is served, on the worker's schema version, its database is migrated, and its role
    /// and releases passed the check once. A failure is logged and counts as not ready until the next poll.
    /// </summary>
    private async Task<bool> CheckTenantAsync(string tenantId, CancellationToken stoppingToken)
    {
        try
        {
            // A tenant that left the directory at a refresh gets no more work.
            if (tenants.Find(tenantId) is null)
            {
                _rounds.Leave(tenantId);
                return false;
            }

            if (IsOnOtherSchemaVersion(tenantId))
            {
                if (_schemaWaiting.Add(tenantId))
                {
                    LogSchemaWaiting(tenantId);
                }

                health.SchemaWaiting(tenantId, true);
                _rounds.Leave(tenantId);
                return false;
            }

            if (_schemaWaiting.Remove(tenantId))
            {
                health.SchemaWaiting(tenantId, false);
            }

            if (!_ready.Contains(tenantId))
            {
                if (!await runner.IsMigratedAsync(tenantId, stoppingToken))
                {
                    if (_waitingLogged.Add(tenantId))
                    {
                        LogWaitingForMigrations(tenantId);
                    }

                    return false;
                }

                await CheckRuntimeRoleAsync(tenantId, stoppingToken);
                await CheckReleasesAsync(tenantId, stoppingToken);
                _ready.Add(tenantId);
                health.TenantMigrated(tenantId);
                LogReady(tenantId);
            }

            return true;
        }
        catch (Exception exception) when (exception is not StartupCheckException
            && (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested))
        {
            // The tenant is polled again after the poll interval, so a database that is down is not polled in a tight loop.
            LogRunFailed(exception, tenantId);
            return false;
        }
    }

    /// <summary>Runs the clean-ups of a ready tenant, each when its own timer says so. A failure does not keep the tenant from the active list.</summary>
    private async Task RunHousekeepingAsync(string tenantId, CancellationToken stoppingToken)
    {
        try
        {
            await RemoveExpiredSessionsAsync(tenantId, stoppingToken);
            await SweepFilesAsync(tenantId, stoppingToken);
            await ClearExpiredImportFilesAsync(tenantId, stoppingToken);
            await RemoveOldNotificationsAsync(tenantId, stoppingToken);
            await SampleUsageAsync(tenantId, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            LogHousekeepingFailed(exception, tenantId);
        }
    }

    /// <summary>
    /// Deletes the expired rows of <c>axis.sessions</c> of the tenant, at most once per five minutes.
    /// The time comes from the worker's clock. A database without the table yet is skipped. Any other database failure is
    /// logged and does not hold back the work items.
    /// </summary>
    private async Task RemoveExpiredSessionsAsync(string tenantId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (_sessionsCleanedAt.TryGetValue(tenantId, out var cleanedAt) && now - cleanedAt < _sessionCleanupInterval)
        {
            return;
        }

        // Recorded first, so a failure is not retried in every poll.
        _sessionsCleanedAt[tenantId] = now;
        var previous = accessor.Current;
        accessor.Current = new TenantContext(tenantId);
        try
        {
            await using var connection = await connections.OpenConnectionAsync(cancellationToken);
            var removed = await SessionLifetime.DeleteExpiredAsync(connection, now, cancellationToken);
            LogSessionsRemoved(removed, tenantId);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // The data migrations have not created the table yet.
        }
        catch (NpgsqlException exception)
        {
            LogSessionCleanupFailed(exception, tenantId);
        }
        finally
        {
            accessor.Current = previous;
        }
    }

    /// <summary>
    /// Purges the <c>deleted</c> files of the tenant, and its <c>pending</c> files uploaded more than 24 hours ago, with their bytes,
    /// at most once per five minutes. The time comes from the worker's clock. A database without the table yet is skipped.
    /// Any other database failure is logged and does not hold back the work items.
    /// </summary>
    private async Task SweepFilesAsync(string tenantId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (_filesSweptAt.TryGetValue(tenantId, out var sweptAt) && now - sweptAt < _fileSweepInterval)
        {
            return;
        }

        // Recorded first, so a failure is not retried in every poll.
        _filesSweptAt[tenantId] = now;
        var previous = accessor.Current;
        accessor.Current = new TenantContext(tenantId);
        try
        {
            await using var connection = await connections.OpenConnectionAsync(cancellationToken);
            var purged = await FileSweep.PurgeAsync(connection, now, cancellationToken);
            LogFilesPurged(purged, tenantId);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // The data migrations have not created the table yet.
        }
        catch (NpgsqlException exception)
        {
            LogFileSweepFailed(exception, tenantId);
        }
        finally
        {
            accessor.Current = previous;
        }
    }

    /// <summary>
    /// Clears the files of the tenant's imports uploaded more than 24 hours ago and keeps the imports, at most once per
    /// five minutes. The time comes from the worker's clock. A database without the table yet is skipped.
    /// Any other database failure is logged and does not hold back the work items.
    /// </summary>
    private async Task ClearExpiredImportFilesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (!_importFilesCleared.TryStart(tenantId, out var now))
        {
            return;
        }

        var previous = accessor.Current;
        accessor.Current = new TenantContext(tenantId);
        try
        {
            await using var connection = await connections.OpenConnectionAsync(cancellationToken);
            var cleared = await RecordImports.ClearExpiredFilesAsync(connection, now, cancellationToken);
            LogImportFilesCleared(cleared, tenantId);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // The data migrations have not created the table yet.
        }
        catch (NpgsqlException exception)
        {
            LogImportFileClearFailed(exception, tenantId);
        }
        finally
        {
            accessor.Current = previous;
        }
    }

    /// <summary>
    /// Deletes the in-app notifications of the tenant that are older than <c>Notifications:RetentionDays</c>, with their read marks,
    /// at most once per 24 hours. The time comes from the worker's clock. A database without the tables yet is skipped.
    /// Any other database failure is logged and does not hold back the work items.
    /// </summary>
    private async Task RemoveOldNotificationsAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (!_notificationsCleaned.TryStart(tenantId, out var now))
        {
            return;
        }

        var previous = accessor.Current;
        accessor.Current = new TenantContext(tenantId);
        try
        {
            await using var connection = await connections.OpenConnectionAsync(cancellationToken);
            var removed = await NotificationRetention.DeleteExpiredAsync(connection, now, notifications.RetentionDays, cancellationToken);
            LogNotificationsRemoved(removed, tenantId);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // The processes migrations have not created the tables yet.
        }
        catch (NpgsqlException exception)
        {
            LogNotificationCleanupFailed(exception, tenantId);
        }
        finally
        {
            accessor.Current = previous;
        }
    }

    /// <summary>
    /// In a cell, measures the storage of the tenant database and records the sample in the platform database, at most once per hour.
    /// The time comes from the worker's clock. A database without the file table yet counts no file bytes. A database failure is logged
    /// and does not hold back the work items. Self-hosted installations measure nothing.
    /// </summary>
    private async Task SampleUsageAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (!cell.IsCell || usage is null || !_usageSampled.TryStart(tenantId, out var now))
        {
            return;
        }

        var previous = accessor.Current;
        accessor.Current = new TenantContext(tenantId);
        try
        {
            TenantStorageSample sample;
            await using (var connection = await connections.OpenConnectionAsync(cancellationToken))
            {
                sample = await TenantStorageSample.ReadAsync(connection, cancellationToken);
            }

            await usage.RecordSampleAsync(tenantId, sample.DatabaseBytes, sample.FileBytes, now, cancellationToken);
            LogUsageSampled(sample.DatabaseBytes, sample.FileBytes, tenantId);
        }
        catch (NpgsqlException exception)
        {
            LogUsageSampleFailed(exception, tenantId);
        }
        finally
        {
            accessor.Current = previous;
        }
    }

    /// <exception cref="StartupCheckException">The runtime database role is a superuser, owns a schema or may create objects.</exception>
    private async Task CheckRuntimeRoleAsync(string tenantId, CancellationToken cancellationToken)
    {
        var previous = accessor.Current;
        accessor.Current = new TenantContext(tenantId);
        try
        {
            await using var connection = await connections.OpenConnectionAsync(cancellationToken);
            await RuntimeRoleCheck.CheckAsync(connection, tenantId, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            checkState.Failed = true;
            LogRoleCheckFailed(exception, tenantId);
            throw new StartupCheckException(exception);
        }
        finally
        {
            accessor.Current = previous;
        }
    }

    /// <exception cref="StartupCheckException">A release in use pins a package version that is not installed, or no longer compiles.</exception>
    private async Task CheckReleasesAsync(string tenantId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Diagnostic>? diagnostics;
        var previous = accessor.Current;
        accessor.Current = new TenantContext(tenantId);
        try
        {
            await using var connection = await connections.OpenConnectionAsync(cancellationToken);
            diagnostics = await PinnedReleaseCheck.CheckAsync(connection, tenantId, packages.Versions, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            // A stored release that no longer compiles against the installed packages.
            checkState.Failed = true;
            LogCheckFailed(exception, tenantId);
            throw new StartupCheckException(tenantId, exception);
        }
        finally
        {
            accessor.Current = previous;
        }

        if (diagnostics is { Count: > 0 })
        {
            checkState.Failed = true;
            foreach (var diagnostic in diagnostics)
            {
                LogDiagnostic(diagnostic.Code, tenantId, diagnostic.File, diagnostic.Path, diagnostic.Message);
            }

            throw new StartupCheckException(tenantId, diagnostics[0]);
        }
    }

    [LoggerMessage(LogLevel.Warning, "The Tenants section is ignored because Tenancy:Source is 'directory'.")]
    private partial void LogTenantsSectionIgnored();

    [LoggerMessage(LogLevel.Error, "Loading the tenant directory failed. The worker retries every {PollInterval}.")]
    private partial void LogLoadFailed(Exception exception, TimeSpan pollInterval);

    [LoggerMessage(LogLevel.Information, "Loaded {TenantCount} active tenant(s) from the tenant directory.")]
    private partial void LogLoaded(int tenantCount);

    [LoggerMessage(LogLevel.Error, "The tenant directory could not be read again over ConnectionStrings:Platform ({Cause}). The worker keeps running the last good read.")]
    private partial void LogRefreshFailed(string cause);

    [LoggerMessage(LogLevel.Error, "Tenant {TenantId} gets no work until its runtime database role passes the check. {Reason}")]
    private partial void LogTenantNotServed(string tenantId, string reason);

    [LoggerMessage(LogLevel.Information, "Read the tenant directory again. {TenantCount} tenant(s) active, {CheckedCount} checked, {WithheldCount} withheld.")]
    private partial void LogRefreshed(int tenantCount, int checkedCount, int withheldCount);

    [LoggerMessage(LogLevel.Information, "Waiting for the database of tenant {TenantId} to be migrated.")]
    private partial void LogWaitingForMigrations(string tenantId);

    [LoggerMessage(LogLevel.Warning, "Tenant {TenantId} waits: its schema version is not this worker's. Run Axis.Admin migrate.")]
    private partial void LogSchemaWaiting(string tenantId);

    [LoggerMessage(LogLevel.Error, "Reading the schema versions from the tenant directory failed. The worker keeps the last good read.")]
    private partial void LogSchemaReadFailed(Exception exception);

    [LoggerMessage(LogLevel.Information, "Tenant {TenantId} is ready for work.")]
    private partial void LogReady(string tenantId);

    [LoggerMessage(LogLevel.Error, "{Code} in release check for tenant {TenantId} at {File} {Path}: {Message}")]
    private partial void LogDiagnostic(string code, string tenantId, string file, string path, string message);

    [LoggerMessage(LogLevel.Error, "Checking the releases in use for tenant {TenantId} failed.")]
    private partial void LogCheckFailed(Exception exception, string tenantId);

    [LoggerMessage(LogLevel.Error, "The runtime database role check for tenant {TenantId} failed.")]
    private partial void LogRoleCheckFailed(Exception exception, string tenantId);

    [LoggerMessage(LogLevel.Warning, "Removing the expired sessions of tenant {TenantId} failed.")]
    private partial void LogSessionCleanupFailed(Exception exception, string tenantId);

    [LoggerMessage(LogLevel.Debug, "Removed {Count} expired session(s) of tenant {TenantId}.")]
    private partial void LogSessionsRemoved(int count, string tenantId);

    [LoggerMessage(LogLevel.Warning, "Sweeping the files of tenant {TenantId} failed.")]
    private partial void LogFileSweepFailed(Exception exception, string tenantId);

    [LoggerMessage(LogLevel.Debug, "Purged {Count} file(s) of tenant {TenantId}.")]
    private partial void LogFilesPurged(int count, string tenantId);

    [LoggerMessage(LogLevel.Warning, "Clearing the expired import files of tenant {TenantId} failed.")]
    private partial void LogImportFileClearFailed(Exception exception, string tenantId);

    [LoggerMessage(LogLevel.Debug, "Cleared {Count} expired import file(s) of tenant {TenantId}.")]
    private partial void LogImportFilesCleared(int count, string tenantId);

    [LoggerMessage(LogLevel.Warning, "Removing the old notifications of tenant {TenantId} failed.")]
    private partial void LogNotificationCleanupFailed(Exception exception, string tenantId);

    [LoggerMessage(LogLevel.Debug, "Removed {Count} old notification(s) of tenant {TenantId}.")]
    private partial void LogNotificationsRemoved(int count, string tenantId);

    [LoggerMessage(LogLevel.Warning, "Measuring the storage of tenant {TenantId} failed.")]
    private partial void LogUsageSampleFailed(Exception exception, string tenantId);

    [LoggerMessage(LogLevel.Debug, "Measured {DatabaseBytes} database byte(s) and {FileBytes} file byte(s) of tenant {TenantId}.")]
    private partial void LogUsageSampled(long databaseBytes, long fileBytes, string tenantId);

    [LoggerMessage(LogLevel.Error, "Running work items for tenant {TenantId} failed.")]
    private partial void LogRunFailed(Exception exception, string tenantId);

    [LoggerMessage(LogLevel.Error, "The housekeeping of tenant {TenantId} failed.")]
    private partial void LogHousekeepingFailed(Exception exception, string tenantId);

    /// <summary>Stops the worker. It is not caught by the per-tenant handler, which retries other failures.</summary>
    private sealed class StartupCheckException : InvalidOperationException
    {
        public StartupCheckException(string tenantId, Diagnostic first)
            : base($"The releases in use for tenant '{tenantId}' do not match the installed extension packages, first {first.Code}: {first.Message}")
        {
        }

        public StartupCheckException(Exception inner)
            : base(inner.Message, inner)
        {
        }

        public StartupCheckException(string tenantId, Exception inner)
            : base($"The releases in use for tenant '{tenantId}' no longer compile against the installed extension packages.", inner)
        {
        }
    }
}
