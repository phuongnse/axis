using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Core.Tenancy;
using Axis.Data;
using Axis.Server.Tenancy;
using Axis.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Axis.Server.Applications;

/// <summary>
/// Migrates every configured tenant database, then compiles and activates the configured
/// application folders in it. The work runs in <see cref="StartingAsync"/>, before any hosted
/// service starts, so the server is not listening yet. Any diagnostic or error stops the start:
/// tenants are processed in ordinal order of their id, and the first failure is thrown.
/// Restarting with unchanged folders stores no new release and only refreshes the activation time.
/// </summary>
internal sealed partial class StartupActivation(
    StartupActivationOptions options,
    TenantOptions tenants,
    ITenantContextAccessor accessor,
    IServiceScopeFactory scopes,
    ILogger<StartupActivation> logger) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        foreach (var tenantId in tenants.Tenants.Keys.Order(StringComparer.Ordinal))
        {
            accessor.Current = new TenantContext(tenantId);
            try
            {
                using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId }))
                {
                    await ActivateTenantAsync(tenantId, cancellationToken);
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

    private async Task ActivateTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TenantDatabase>();
        var configuration = await database.GetConfigurationAsync(cancellationToken);
        var data = await database.GetDataAsync(cancellationToken);
        await configuration.Database.MigrateAsync(cancellationToken);
        await data.Database.MigrateAsync(cancellationToken);

        foreach (var folder in options.Folders)
        {
            try
            {
                var compiled = await ReleaseCompiler.CompileAsync(folder, configuration, cancellationToken);
                LogDiagnostics(compiled.Diagnostics, folder, tenantId);
                if (compiled.Release is null || compiled.Model is null)
                {
                    throw Failure("does not compile", compiled.Diagnostics, folder, tenantId);
                }

                var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, cancellationToken);
                if (activated.Diagnostics.Count > 0)
                {
                    LogDiagnostics(activated.Diagnostics, folder, tenantId);
                    throw Failure("could not be activated", activated.Diagnostics, folder, tenantId);
                }

                LogActivated(compiled.Model.Manifest.Name, compiled.Release.Id, folder, tenantId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(exception, folder, tenantId);
                throw;
            }
        }
    }

    private void LogDiagnostics(IReadOnlyList<Diagnostic> diagnostics, string folder, string tenantId)
    {
        foreach (var diagnostic in diagnostics)
        {
            LogDiagnostic(
                diagnostic.Severity == DiagnosticSeverity.Warning ? LogLevel.Warning : LogLevel.Error,
                diagnostic.Code,
                folder,
                tenantId,
                diagnostic.File,
                diagnostic.Path,
                diagnostic.Message);
        }
    }

    private static InvalidOperationException Failure(string reason, IReadOnlyList<Diagnostic> diagnostics, string folder, string tenantId)
    {
        var first = diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ?? (diagnostics.Count > 0 ? diagnostics[0] : null);
        var detail = first is null ? "no diagnostic" : $"{diagnostics.Count} diagnostic(s), first {first.Code} in '{first.File}'";
        return new InvalidOperationException(
            $"The application folder '{folder}' {reason} for tenant '{tenantId}': {detail}.");
    }

    [LoggerMessage("{Code} in application folder {Folder} for tenant {TenantId} at {File} {Path}: {Message}")]
    private partial void LogDiagnostic(LogLevel level, string code, string folder, string tenantId, string file, string path, string message);

    [LoggerMessage(LogLevel.Information, "Activated application {Application} release {ReleaseId} from folder {Folder} for tenant {TenantId}.")]
    private partial void LogActivated(string application, Guid releaseId, string folder, string tenantId);

    [LoggerMessage(LogLevel.Error, "Activating application folder {Folder} for tenant {TenantId} failed.")]
    private partial void LogFailed(Exception exception, string folder, string tenantId);
}
