using Axis.Configuration.Diagnostics;
using Axis.Configuration.Packages;
using Axis.Core.Tenancy;
using Axis.Data.Files;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Tenancy;
using Npgsql;

namespace Axis.Worker;

/// <summary>
/// Under the directory source it first reads the tenant directory, and retries until a read works. Then it
/// polls every tenant in ordinal order of its id and runs its due work items until none is left, then waits
/// for the poll interval when no tenant had work. A tenant whose database the server has not migrated yet is
/// skipped until it has. Under the directory source it reads the directory again every <c>Tenancy:RefreshInterval</c>,
/// between work items. A tenant that is no longer active gets no new work item claimed, and a new tenant is polled
/// once its runtime role passes the check. It also reads the recorded schema version of every tenant at the
/// start of each round, and claims no work of a tenant whose version is not the worker's, until the admin tool's
/// <c>migrate</c> has brought it there. When a tenant's database counts as migrated,
/// its runtime database role is checked, so that it can only read and write rows. Then the releases
/// the tenant still uses are checked against the installed extension packages. A role that can change
/// the schema, a pinned version that is not installed, or a release that no longer compiles records
/// the failure in <see cref="StartupCheckState"/> and stops the host before any work of that tenant runs.
/// Once a tenant is ready, the worker also deletes its expired sessions and sweeps its deleted and abandoned files,
/// each at most every five minutes by its own clock.
/// </summary>
internal sealed partial class WorkerService(
    WorkerTenantCatalog tenants,
    WorkerOptions options,
    WorkItemRunner runner,
    WorkerHealth health,
    ITenantConnectionFactory connections,
    TenantConnectionFactory pools,
    TimeProvider time,
    ITenantContextAccessor accessor,
    PackageCatalog packages,
    StartupCheckState checkState,
    ILogger<WorkerService> logger) : BackgroundService
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

    // The time of the last clean-up of expired sessions of each ready tenant. It is a fixed interval, short next to the idle limit.
    private static readonly TimeSpan _sessionCleanupInterval = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, DateTimeOffset> _sessionsCleanedAt = new(StringComparer.Ordinal);

    // The time of the last sweep of deleted and abandoned files of each ready tenant. It has its own timer, so neither clean-up delays the other.
    private static readonly TimeSpan _fileSweepInterval = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, DateTimeOffset> _filesSweptAt = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (tenants.TenantsSectionIgnored)
        {
            LogTenantsSectionIgnored();
        }

        await LoadTenantsAsync(stoppingToken);
        _lastRefresh = time.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            health.Polled();
            await ReadSchemaVersionsAsync(stoppingToken);
            await RefreshIfDueAsync(stoppingToken);
            var ranAny = false;
            foreach (var tenantId in tenants.Tenants.Select(tenant => tenant.Id).ToArray())
            {
                using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId }))
                {
                    ranAny |= await RunTenantAsync(tenantId, stoppingToken);
                }
            }

            if (!ranAny)
            {
                await Task.Delay(options.PollInterval, stoppingToken);
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
    /// A tenant that changed or left loses its old connection pool and its ready state. It runs between work items,
    /// so no pool is dropped while one of its items runs.
    /// </summary>
    private async Task RefreshIfDueAsync(CancellationToken stoppingToken)
    {
        if (!tenants.FromDirectorySource || time.GetUtcNow() - _lastRefresh < tenants.RefreshInterval)
        {
            return;
        }

        try
        {
            var result = await tenants.RefreshAsync(PassesRoleCheckAsync, stoppingToken);
            foreach (var tenantId in result.Retired)
            {
                await pools.RetireAsync(tenantId);
                _ready.Remove(tenantId);
                _waitingLogged.Remove(tenantId);
                _schemaWaiting.Remove(tenantId);
                _sessionsCleanedAt.Remove(tenantId);
                _filesSweptAt.Remove(tenantId);
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

    private async Task<bool> RunTenantAsync(string tenantId, CancellationToken stoppingToken)
    {
        var ran = false;
        try
        {
            // A tenant that left the directory at a refresh gets no more work.
            if (tenants.Find(tenantId) is null)
            {
                return false;
            }

            if (IsOnOtherSchemaVersion(tenantId))
            {
                if (_schemaWaiting.Add(tenantId))
                {
                    LogSchemaWaiting(tenantId);
                }

                health.SchemaWaiting(tenantId, true);
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

            await RemoveExpiredSessionsAsync(tenantId, stoppingToken);
            await SweepFilesAsync(tenantId, stoppingToken);

            // A refresh between two items can end the service of the tenant. The item already claimed has finished.
            while (tenants.Find(tenantId) is not null && await runner.RunNextAsync(tenantId, options.LeaseDuration, stoppingToken))
            {
                ran = true;

                // A worker with a steady stream of items still polls and refreshes. One item that hangs does not.
                health.Polled();
                await RefreshIfDueAsync(stoppingToken);
            }
        }
        catch (Exception exception) when (exception is not StartupCheckException
            && (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested))
        {
            // A claimed item reappears when its lease expires. The tenant is polled again in the next
            // round, after the poll interval, so a database that is down is not polled in a tight loop.
            LogRunFailed(exception, tenantId);
            return false;
        }

        return ran;
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

    [LoggerMessage(LogLevel.Error, "Running work items for tenant {TenantId} failed.")]
    private partial void LogRunFailed(Exception exception, string tenantId);

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
