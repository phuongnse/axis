using Axis.Core.Tenancy;
using Axis.Tenancy;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Axis.Server.Health;

/// <summary>
/// Reports whether every tenant database answers. The tenants are checked in parallel, and a database
/// that has not answered within <see cref="TenantTimeout"/> counts as not answering. A tenant that does
/// not answer makes the result Degraded, not Unhealthy, so one tenant cannot take the others offline. Each
/// one is named in a warning log, and never in the result.
/// </summary>
internal sealed partial class TenantDatabasesHealthCheck(
    TenantOptions tenants,
    ITenantContextAccessor accessor,
    ITenantConnectionFactory connections,
    ILogger<TenantDatabasesHealthCheck> logger) : IHealthCheck
{
    internal static readonly TimeSpan TenantTimeout = TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var answers = await Task.WhenAll(tenants.Tenants.Keys.Select(id => AnswersAsync(id, cancellationToken)));
        return answers.All(answered => answered) ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded();
    }

    // A function of its own, so the tenant context set here stays in this tenant's flow.
    private async Task<bool> AnswersAsync(string tenantId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TenantTimeout);
        accessor.Current = new TenantContext(tenantId);
        try
        {
            await using var connection = await OpenAsync(timeout.Token);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(timeout.Token);
            return true;
        }
        catch (Exception exception) when (
            exception is NpgsqlException or TimeoutException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnanswered(exception, tenantId);
            return false;
        }
        finally
        {
            accessor.Current = null;
        }
    }

    // Npgsql does not stop a connection attempt on cancellation while the server is silent, so the wait
    // ends here. A connection that opens after the timeout is disposed.
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var opening = connections.OpenConnectionAsync(cancellationToken).AsTask();
        try
        {
            return await opening.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _ = opening.ContinueWith(
                task => task.Result.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
            throw;
        }
    }

    [LoggerMessage(LogLevel.Warning, "The database of tenant {TenantId} does not answer.")]
    private partial void LogUnanswered(Exception exception, string tenantId);
}
