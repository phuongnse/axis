using Axis.Data.Storage;
using Axis.Tenancy;

namespace Axis.Agent;

/// <summary>
/// The agent host's background service. With AI authoring off it only says so. With it on, it reads
/// its tenants as the worker does, see <c>docs/reference/host-settings.md#ai-settings</c>: under the <c>directory</c> source it retries the
/// first read of the tenant directory every <c>Tenancy:RefreshInterval</c> until it works, and then reads the directory again every
/// interval. A tenant whose runtime database role fails the <see cref="RuntimeRoleCheck"/> is left out until it passes. Beside the refresh it
/// runs the work loop of the <see cref="AuthoringRequestRunner"/>, which claims a request only in a state that has a registered step, see
/// <c>docs/reference/authoring-requests.md#durable-requests</c>.
/// </summary>
internal sealed partial class AgentService(
    AiSettings settings,
    TimeProvider time,
    ILogger<AgentService> logger,
    BackgroundTenantCatalog? tenants = null,
    TenantConnectionFactory? pools = null,
    AuthoringRequestStore? store = null,
    AuthoringSteps? steps = null,
    AuthoringRequestRunner? runner = null,
    AuthoringLoopOptions? loop = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (settings.DefaultBinding is null || tenants is null)
        {
            LogAuthoringOff();
            return;
        }

        await LoadTenantsAsync(tenants, stoppingToken);
        await Task.WhenAll(KeepTenantsFreshAsync(tenants, stoppingToken), WorkAsync(tenants, stoppingToken));
    }

    private async Task KeepTenantsFreshAsync(BackgroundTenantCatalog catalog, CancellationToken stoppingToken)
    {
        if (!catalog.FromDirectorySource)
        {
            return;
        }

        while (true)
        {
            await Task.Delay(catalog.RefreshInterval, time, stoppingToken);
            await RefreshAsync(catalog, stoppingToken);
        }
    }

    // With no registered step, nothing is claimed and the loop ends at once.
    private async Task WorkAsync(BackgroundTenantCatalog catalog, CancellationToken stoppingToken)
    {
        if (steps is null || runner is null || loop is null || !steps.Any)
        {
            return;
        }

        while (true)
        {
            if (!await runner.RunRoundAsync(catalog.Tenants, stoppingToken))
            {
                await Task.Delay(loop.PollInterval, time, stoppingToken);
            }
        }
    }

    // Only the first failure is logged. The directory source keeps no tenant until a read works.
    private async Task LoadTenantsAsync(BackgroundTenantCatalog catalog, CancellationToken stoppingToken)
    {
        if (catalog.TenantsSectionIgnored)
        {
            LogTenantsSectionIgnored();
        }

        var failureLogged = false;
        while (!catalog.IsLoaded)
        {
            try
            {
                await catalog.LoadAsync(stoppingToken);
            }
            catch (InvalidOperationException exception)
            {
                if (!failureLogged)
                {
                    failureLogged = true;
                    LogLoadFailed(exception, catalog.RefreshInterval);
                }

                await Task.Delay(catalog.RefreshInterval, time, stoppingToken);
            }
        }

        if (catalog.FromDirectorySource)
        {
            LogLoaded(catalog.Tenants.Count);
        }
    }

    // A tenant that changed or left loses its connection pool and its passed role check. A failed read keeps the last good one.
    private async Task RefreshAsync(BackgroundTenantCatalog catalog, CancellationToken stoppingToken)
    {
        try
        {
            var result = await catalog.RefreshAsync(PassesRoleCheckAsync, stoppingToken);
            foreach (var tenantId in result.Retired)
            {
                if (pools is not null)
                {
                    await pools.RetireAsync(tenantId);
                }

                store?.Forget(tenantId);
            }

            LogRefreshed(result.TenantCount, result.CheckedCount, result.WithheldCount);
        }
        catch (InvalidOperationException exception)
        {
            LogRefreshFailed(exception.InnerException?.GetType().Name ?? exception.GetType().Name);
        }
    }

    private async Task<bool> PassesRoleCheckAsync(TenantEntry entry, CancellationToken cancellationToken)
    {
        // The refusal names the tenant and the role. A connection failure may name the database host, so it is not logged.
        var refusal = await RuntimeRoleCheck.RefusalAsync(entry, cancellationToken);
        if (refusal is null)
        {
            return true;
        }

        LogTenantNotServed(entry.Id, refusal);
        return false;
    }

    [LoggerMessage(LogLevel.Information, "AI authoring is off, because Ai:DefaultBinding has no value.")]
    private partial void LogAuthoringOff();

    [LoggerMessage(LogLevel.Warning, "The Tenants section is ignored because Tenancy:Source is 'directory'.")]
    private partial void LogTenantsSectionIgnored();

    [LoggerMessage(LogLevel.Error, "Loading the tenant directory failed. The agent host retries every {RefreshInterval}.")]
    private partial void LogLoadFailed(Exception exception, TimeSpan refreshInterval);

    [LoggerMessage(LogLevel.Information, "Loaded {TenantCount} active tenant(s) from the tenant directory.")]
    private partial void LogLoaded(int tenantCount);

    [LoggerMessage(LogLevel.Error, "The tenant directory could not be read again over ConnectionStrings:Platform ({Cause}). The agent host keeps the last good read.")]
    private partial void LogRefreshFailed(string cause);

    [LoggerMessage(LogLevel.Error, "Tenant {TenantId} is left out until its runtime database role passes the check. {Reason}")]
    private partial void LogTenantNotServed(string tenantId, string reason);

    [LoggerMessage(LogLevel.Information, "Read the tenant directory again. {TenantCount} tenant(s) active, {CheckedCount} checked, {WithheldCount} withheld.")]
    private partial void LogRefreshed(int tenantCount, int checkedCount, int withheldCount);
}
