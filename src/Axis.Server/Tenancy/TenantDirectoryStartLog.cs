namespace Axis.Server.Tenancy;

/// <summary>What the server learned while it loaded the tenant directory at start.</summary>
internal sealed record TenantDirectoryStart(string ReaderRole, int TenantCount, bool TenantsSectionIgnored);

/// <summary>
/// Under <c>Tenancy:Source=directory</c>, logs the platform migrations, the ignored <c>Tenants</c> section and the
/// number of tenants loaded. The load itself runs before the host is built, so this service reports it when the host
/// starts. It logs no host name and no connection value.
/// </summary>
internal sealed partial class TenantDirectoryStartLog(
    TenantDirectoryStart start,
    ILogger<TenantDirectoryStartLog> logger) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        LogMigrated(start.ReaderRole);
        if (start.TenantsSectionIgnored)
        {
            LogTenantsSectionIgnored();
        }

        LogLoaded(start.TenantCount);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(LogLevel.Information, "Applied the platform schema migrations and limited the role {ReaderRole} to reading the platform schema.")]
    private partial void LogMigrated(string readerRole);

    [LoggerMessage(LogLevel.Warning, "The Tenants section is ignored because Tenancy:Source is 'directory'.")]
    private partial void LogTenantsSectionIgnored();

    [LoggerMessage(LogLevel.Information, "Loaded {TenantCount} active tenant(s) from the tenant directory.")]
    private partial void LogLoaded(int tenantCount);
}
