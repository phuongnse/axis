using Axis.Configuration.Diagnostics;
using Axis.Configuration.Packages;
using Axis.Core.Tenancy;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Tenancy;

namespace Axis.Worker;

/// <summary>
/// Under the directory source it first reads the tenant directory, and retries until a read works. Then it
/// polls every tenant in ordinal order of its id and runs its due work items until none is left, then waits
/// for the poll interval when no tenant had work. A tenant whose database the server has not migrated yet is
/// skipped until it has. Under the directory source it also reads the recorded schema version of every tenant at the
/// start of each round, and claims no work of a tenant whose version is not the worker's, until the admin tool's
/// <c>migrate</c> has brought it there. When a tenant's database counts as migrated,
/// its runtime database role is checked, so that it can only read and write rows. Then the releases
/// the tenant still uses are checked against the installed extension packages. A role that can change
/// the schema, a pinned version that is not installed, or a release that no longer compiles records
/// the failure in <see cref="StartupCheckState"/> and stops the host before any work of that tenant runs.
/// </summary>
internal sealed partial class WorkerService(
    WorkerTenantCatalog tenants,
    WorkerOptions options,
    WorkItemRunner runner,
    WorkerHealth health,
    ITenantConnectionFactory connections,
    ITenantContextAccessor accessor,
    PackageCatalog packages,
    StartupCheckState checkState,
    ILogger<WorkerService> logger) : BackgroundService
{
    // Tenants whose database was migrated. A new schema arrives with a new release, which restarts
    // the worker, so a ready tenant is not checked again.
    private readonly HashSet<string> _ready = new(StringComparer.Ordinal);
    private readonly HashSet<string> _waitingLogged = new(StringComparer.Ordinal);
    private readonly HashSet<string> _schemaWaiting = new(StringComparer.Ordinal);

    // The recorded schema versions of the last good read. Null under the configuration source, and before a read worked.
    private IReadOnlyDictionary<string, string>? _schemaVersions;
    private bool _schemaReadFailureLogged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (tenants.TenantsSectionIgnored)
        {
            LogTenantsSectionIgnored();
        }

        await LoadTenantsAsync(stoppingToken);
        var tenantIds = tenants.Tenants.Select(tenant => tenant.Id).ToArray();
        while (!stoppingToken.IsCancellationRequested)
        {
            health.Polled();
            await ReadSchemaVersionsAsync(stoppingToken);
            var ranAny = false;
            foreach (var tenantId in tenantIds)
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

    private async Task<bool> RunTenantAsync(string tenantId, CancellationToken stoppingToken)
    {
        var ran = false;
        try
        {
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

            while (await runner.RunNextAsync(tenantId, options.LeaseDuration, stoppingToken))
            {
                ran = true;

                // A worker with a steady stream of items still polls. One item that hangs does not.
                health.Polled();
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
