using Axis.Data.Storage;
using Axis.Processes.Storage;
using Axis.Server.Authentication;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Npgsql;

namespace Axis.Server.Tenancy;

/// <param name="PlatformConnectionString">The reader role's connection string for the platform database.</param>
/// <param name="Interval">How often the directory is read again.</param>
internal sealed record TenantDirectoryRefreshOptions(string PlatformConnectionString, TimeSpan Interval);

/// <summary>
/// Under <c>Tenancy:Source=directory</c>, reads the tenant directory again every <c>Tenancy:RefreshInterval</c>. A tenant
/// is served only while it is active, is on this server's schema version and has passed the runtime role check on its current database settings. When a
/// read fails, the server keeps serving the last good read, logs one error per run of failed reads and reports Degraded readiness
/// until a read works. The log holds no host name and no connection value.
/// </summary>
internal sealed partial class TenantDirectoryRefresh(
    TenantDirectoryCatalog directory,
    TenantDirectoryRefreshOptions options,
    TenantSecretCipher cipher,
    TenantConnectionFactory connections,
    TenantDirectoryStart start,
    TenantSignInSchemes signIn,
    TimeProvider timeProvider,
    TenantDirectoryRefreshStatus status,
    ILogger<TenantDirectoryRefresh> logger) : BackgroundService
{
    private readonly SemaphoreSlim _gate = new(1);

    // The tenants the last read held back for their schema version, so the warning comes once per tenant and not at every read.
    // The start already logged the first ones.
    private HashSet<string> _schemaWithheld = [.. start.SchemaWithheld];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Interval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RefreshAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Reads the directory once and swaps it in. A tenant that is new, active again or on other database settings than it
    /// was served on is served only after the runtime role check passes on the new settings. Then its old pool is dropped.
    /// </summary>
    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            DirectoryTenantCatalog loaded;
            try
            {
                loaded = await DirectoryTenantCatalog.LoadAsync(options.PlatformConnectionString, cipher, cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                // One error per run of failed reads. The status tells the next read whether a run is open.
                if (!status.Failing)
                {
                    LogRefreshFailed(exception.InnerException?.GetType().Name ?? exception.GetType().Name);
                }

                status.Set(true);
                return;
            }

            // A tenant on another schema version is not served. One that becomes current counts as new below and is checked.
            var otherSchema = loaded.OnOtherSchemaVersion(TenantSchemaVersion.Current);
            foreach (var id in otherSchema.Where(id => !_schemaWithheld.Contains(id)))
            {
                LogSchemaWithheld(id);
            }

            _schemaWithheld = [.. otherSchema];
            loaded = loaded.Withhold(otherSchema);

            var pending = loaded.DatabaseChanges(directory.Current);
            var withheld = new List<string>();
            foreach (var id in pending)
            {
                if (!await PassesRoleCheckAsync(loaded.Find(id)!, cancellationToken))
                {
                    withheld.Add(id);
                }
            }

            // The start check stops the server on incomplete sign-in settings. Here the tenant is withheld, so one
            // tenant cannot stop the refresh of the others.
            var signInSettings = new Dictionary<string, TenantOidcSettings>(StringComparer.Ordinal);
            foreach (var tenant in loaded.Tenants.Where(tenant => !withheld.Contains(tenant.Id, StringComparer.Ordinal)))
            {
                var problems = new List<string>();
                if (TenantOidcSettings.TryCreate(tenant, problems) is { } settings)
                {
                    signInSettings[tenant.Id] = settings;
                }
                else
                {
                    withheld.Add(tenant.Id);
                    LogSignInIncomplete(tenant.Id, string.Join(" ", problems));
                }
            }

            var next = loaded.Withhold(withheld);
            var changed = next.DatabaseChanges(directory.Current);
            var dropped = directory.Current.Tenants.Select(tenant => tenant.Id).Except(next.Tenants.Select(tenant => tenant.Id), StringComparer.Ordinal);
            signIn.Apply(signInSettings, next.OidcChanges(directory.Current));
            directory.Replace(next);
            if (status.Failing)
            {
                LogRefreshRecovered();
            }

            status.Set(false);

            // A tenant that is no longer served, or serves on new settings, must not keep its old pool.
            foreach (var id in changed.Concat(dropped).Distinct(StringComparer.Ordinal))
            {
                await connections.RetireAsync(id);
            }

            LogRefreshed(next.Tenants.Count, pending.Count, withheld.Count);
        }
        finally
        {
            _gate.Release();
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
            LogRoleCheckFailed(entry.Id, exception is InvalidOperationException ? exception.Message : "The tenant database could not be reached.");
            return false;
        }
    }

    [LoggerMessage(LogLevel.Error, "The tenant directory could not be read again over ConnectionStrings:Platform ({Cause}). The server keeps serving the last good read.")]
    private partial void LogRefreshFailed(string cause);

    [LoggerMessage(LogLevel.Information, "The tenant directory was read again after failed reads. The server serves the new read.")]
    private partial void LogRefreshRecovered();

    [LoggerMessage(LogLevel.Error, "Tenant {TenantId} is not served until its runtime database role passes the check. {Reason}")]
    private partial void LogRoleCheckFailed(string tenantId, string reason);

    [LoggerMessage(LogLevel.Warning, "Tenant {TenantId} is not served: its schema version is not this server's. Run Axis.Admin migrate.")]
    private partial void LogSchemaWithheld(string tenantId);

    [LoggerMessage(LogLevel.Error, "Tenant {TenantId} is not served until its sign-in settings are complete. {Reason}")]
    private partial void LogSignInIncomplete(string tenantId, string reason);

    [LoggerMessage(LogLevel.Information, "Read the tenant directory again. {TenantCount} tenant(s) served, {CheckedCount} checked, {WithheldCount} withheld.")]
    private partial void LogRefreshed(int tenantCount, int checkedCount, int withheldCount);
}
