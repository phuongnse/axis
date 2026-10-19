using Axis.Configuration.Diagnostics;
using Axis.Configuration.Packages;
using Axis.Core.Tenancy;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Server.Tenancy;
using Axis.Tenancy;
using Npgsql;

namespace Axis.Server.Applications;

/// <summary>
/// Checks, for every configured tenant, that the runtime database role can only read and write rows,
/// that each release in use finds the extension package
/// versions it pins installed, and rebuilds it from its stored files. A release is in use when it
/// is active or has a running or waiting process instance. The check runs in
/// <see cref="StartingAsync"/>, after <see cref="StartupActivation"/> and before the server
/// listens. Tenants are checked in ordinal order of their id, and a diagnostic or a release that
/// no longer compiles stops the start. A tenant database with migrations pending, or one that
/// cannot be reached, is skipped, as the server starts with a database that is down.
/// </summary>
internal sealed partial class StartupReleaseCheck(
    ITenantCatalog tenants,
    PackageCatalog packages,
    ITenantContextAccessor accessor,
    IServiceScopeFactory scopes,
    ILogger<StartupReleaseCheck> logger) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        foreach (var tenantId in tenants.Tenants.Select(tenant => tenant.Id))
        {
            accessor.Current = new TenantContext(tenantId);
            try
            {
                using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId }))
                {
                    await CheckTenantAsync(tenantId, cancellationToken);
                }
            }
            finally
            {
                accessor.Current = null;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CheckTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TenantDatabase>();
        NpgsqlConnection connection;
        try
        {
            connection = await database.GetConnectionAsync(cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            // The server starts with a database that is down, as it did before, and reports it as not ready.
            LogUnreachable(exception, tenantId);
            return;
        }

        IReadOnlyList<Diagnostic>? diagnostics;
        try
        {
            await RuntimeRoleCheck.CheckAsync(connection, tenantId, cancellationToken);
            diagnostics = await PinnedReleaseCheck.CheckAsync(connection, tenantId, packages.Versions, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(exception, tenantId);
            throw;
        }

        if (diagnostics is null)
        {
            LogSkipped(tenantId);
            return;
        }

        foreach (var diagnostic in diagnostics)
        {
            LogDiagnostic(diagnostic.Code, tenantId, diagnostic.File, diagnostic.Path, diagnostic.Message);
        }

        if (diagnostics.Count > 0)
        {
            throw new InvalidOperationException(
                $"The releases in use for tenant '{tenantId}' do not match the installed extension packages: {diagnostics.Count} diagnostic(s), first {diagnostics[0].Code}: {diagnostics[0].Message}");
        }
    }

    [LoggerMessage(LogLevel.Error, "{Code} in release check for tenant {TenantId} at {File} {Path}: {Message}")]
    private partial void LogDiagnostic(string code, string tenantId, string file, string path, string message);

    [LoggerMessage(LogLevel.Information, "Skipped the release check for tenant {TenantId}: its database is not migrated.")]
    private partial void LogSkipped(string tenantId);

    [LoggerMessage(LogLevel.Warning, "Skipped the release check for tenant {TenantId}: its database is not reachable.")]
    private partial void LogUnreachable(Exception exception, string tenantId);

    [LoggerMessage(LogLevel.Error, "Checking the releases in use for tenant {TenantId} failed.")]
    private partial void LogFailed(Exception exception, string tenantId);
}
