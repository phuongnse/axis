using System.Diagnostics;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Core.Telemetry;
using Axis.Core.Tenancy;
using Axis.Data;
using Axis.Data.Records;
using Axis.Data.Seeding;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Server.Health;
using Axis.Server.Tenancy;
using Axis.Tenancy;
using Axis.Tenancy.Realms;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Server.Applications;

/// <summary>
/// Migrates every configured tenant database and grants its runtime database role its rights (before
/// and after the migrations) and checks over its own connection that the role can only read and write rows,
/// then compiles the configured application folders
/// against the installed extension packages, activates them in it, brings the tenant's realm in step with each one (for a tenant
/// that has realm details, before its schedule triggers and seed), writes the state of their schedule triggers, writes their seed records and builds the indexes that activation left out of large tables. The work runs in <see cref="StartingAsync"/>, before any hosted
/// service starts, so the server is not listening yet. The migrations, release storage, entity
/// provisioning and the active release use a connection from the tenant's
/// <c>SchemaConnectionString</c>, which closes when the tenant is done. The runtime database role is the
/// user of the tenant's <c>ConnectionString</c>. The schedule trigger state
/// and the seed records use the tenant's <c>ConnectionString</c>. Any exception, error diagnostic or failed realm update stops the start:
/// tenants are processed in ordinal order of their id, and the first failure is thrown. The activation of a preview
/// tenant may end with warnings, such as a missing deployment binding, which are logged and do not stop the start.
/// Readiness reports Unhealthy until the last tenant is done, and stays so when a tenant fails.
/// Restarting with unchanged folders, installed packages and installed modules stores no new release and only
/// refreshes the activation time.
/// </summary>
internal sealed partial class StartupActivation(
    StartupActivationOptions options,
    ITenantCatalog tenants,
    TenantSchemaConnections schemaConnections,
    PackageCatalog packages,
    ModuleCatalog modules,
    ITenantContextAccessor accessor,
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    IHttpClientFactory http,
    SensitiveValues sensitive,
    StartupActivationStatus status,
    ILogger<StartupActivation> logger) : IHostedLifecycleService
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
                    await ActivateTenantAsync(tenantId, cancellationToken);
                }
            }
            finally
            {
                accessor.Current = null;
            }
        }

        status.MarkFinished();
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ActivateTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        await using var schemaSource = NpgsqlDataSource.Create(schemaConnections.For(tenantId));
        await using var schema = new TenantDatabase(new DataSourceConnections(schemaSource));
        await using var scope = scopes.CreateAsyncScope();
        var runtime = scope.ServiceProvider.GetRequiredService<TenantDatabase>();
        var configuration = await schema.GetConfigurationAsync(cancellationToken);
        var data = await schema.GetDataAsync(cancellationToken);
        var processes = await schema.GetProcessesAsync(cancellationToken);
        var runtimeRole = new NpgsqlConnectionStringBuilder(tenants.Find(tenantId)!.ConnectionString.Reveal()).Username;
        if (string.IsNullOrWhiteSpace(runtimeRole))
        {
            throw new InvalidOperationException(
                $"The ConnectionString of tenant '{tenantId}' names no user, so the runtime database role is unknown.");
        }

        var schemaConnection = await schema.GetConnectionAsync(cancellationToken);

        // Before the migrations, so the default privileges apply to the tables they create. After them, so
        // the tables that exist get their grants, and a database with older, wider grants is repaired.
        await GrantRuntimeRoleAsync(schemaConnection, runtimeRole, tenantId, cancellationToken);
        await configuration.Database.MigrateAsync(cancellationToken);
        await data.Database.MigrateAsync(cancellationToken);
        await processes.Database.MigrateAsync(cancellationToken);
        await GrantRuntimeRoleAsync(schemaConnection, runtimeRole, tenantId, cancellationToken);
        await RuntimeRoleCheck.CheckAsync(await runtime.GetConnectionAsync(cancellationToken), tenantId, cancellationToken);
        var tenant = tenants.Find(tenantId)!;
        var bindings = TenantActivationBindings.For(tenant, environment.IsDevelopment());

        foreach (var folder in options.Folders)
        {
            using var activity = AxisTelemetry.Source.StartActivity(AxisTelemetry.Activation, ActivityKind.Internal);
            activity?.SetTag(AxisTelemetry.TenantTag, tenantId);
            try
            {
                var compiled = await ReleaseCompiler.CompileAsync(folder, configuration, packages.Versions, modules.Modules, cancellationToken);
                activity?.SetTag(AxisTelemetry.ApplicationTag, compiled.Model?.Manifest.Name);
                LogDiagnostics(compiled.Diagnostics, folder, tenantId);
                if (compiled.Release is null || compiled.Model is null)
                {
                    throw Failure("does not compile", compiled.Diagnostics, folder, tenantId);
                }

                var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, bindings with { Sensitive = sensitive }, OpenInstanceCounter.Instance, options.InlineRecomputeLimit, cancellationToken);
                LogDiagnostics(activated.Diagnostics, folder, tenantId);
                if (activated.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    throw Failure("could not be activated", activated.Diagnostics, folder, tenantId);
                }

                if (activated.Realm is { } plan)
                {
                    using var client = http.CreateClient(nameof(StartupActivation));
                    await ActivationRealmSync.SyncAsync(client, tenant.Realm, tenant.Bindings, plan, cancellationToken);
                }

                await TriggerSchedules.ActivateAsync(
                    await runtime.GetConnectionAsync(cancellationToken), tenantId, compiled.Model, compiled.Release.Id, cancellationToken);
                LogActivated(compiled.Model.Manifest.Name, compiled.Release.Id, folder, tenantId);
                if (compiled.Model.Seeds.Count > 0)
                {
                    var seeded = await SeedApplier.ApplyAsync(compiled.Model, compiled.Release.Id, await runtime.GetConnectionAsync(cancellationToken), new RecordCipher(sensitive, tenantId), cancellationToken);
                    if (seeded.Diagnostics.Count > 0)
                    {
                        LogDiagnostics(seeded.Diagnostics, folder, tenantId);
                        throw Failure("could not be seeded", seeded.Diagnostics, folder, tenantId);
                    }

                    LogSeeded(seeded.Inserted, seeded.Updated, folder, tenantId);
                }

                var built = await EntityIndexBuilder.BuildAsync(compiled.Model, schemaConnection, cancellationToken);
                if (built.Count > 0)
                {
                    LogIndexesBuilt(built.Count, folder, tenantId);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AxisTelemetry.RecordFailure(activity, exception);
                LogFailed(exception, folder, tenantId);
                throw;
            }
        }
    }

    private static async Task GrantRuntimeRoleAsync(
        NpgsqlConnection schemaConnection, string runtimeRole, string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await RuntimeRoleGrants.ApplyAsync(schemaConnection, runtimeRole, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Tenant '{tenantId}': {exception.Message} Its ConnectionString and SchemaConnectionString must name different users.", exception);
        }
    }

    /// <summary>Opens connections from one data source, whatever the current tenant is.</summary>
    private sealed class DataSourceConnections(NpgsqlDataSource source) : ITenantConnectionFactory
    {
        public ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
            source.OpenConnectionAsync(cancellationToken);
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

    [LoggerMessage(LogLevel.Information, "Inserted {Inserted} seed record(s) and updated {Updated} from folder {Folder} for tenant {TenantId}.")]
    private partial void LogSeeded(int inserted, int updated, string folder, string tenantId);

    [LoggerMessage(LogLevel.Information, "Built {Count} deferred index(es) from folder {Folder} for tenant {TenantId}.")]
    private partial void LogIndexesBuilt(int count, string folder, string tenantId);

    [LoggerMessage(LogLevel.Error, "Activating application folder {Folder} for tenant {TenantId} failed.")]
    private partial void LogFailed(Exception exception, string folder, string tenantId);
}
