using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.Seeding;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Admin;

/// <summary>
/// <c>activate</c>: compiles an application folder and activates its release for each tenant, on its own. For each tenant
/// it checks the schema version, compiles, activates and seeds. A tenant stops at its first failed step and keeps its
/// previous active release, and the other tenants go on. Each step writes one <c>ok</c> line, or one <c>failed</c> line for
/// each error diagnostic. A line names the tenant and never a password, a host or a connection string. Compile and
/// activate run as the tenant's schema role, over the host and port that the directory records for it. The schedule
/// trigger state and the seed records run as its runtime role. Warnings go to the error writer.
/// </summary>
internal sealed class TenantActivate(
    PlatformSettings settings,
    IReadOnlyList<TenantRow> tenants,
    IReadOnlyDictionary<string, ActivationBindings> bindings,
    string folder,
    PackageCatalog packages,
    ModuleCatalog modules,
    int inlineRecomputeLimit,
    TextWriter output,
    TextWriter error,
    CancellationToken cancellationToken)
{
    public async Task<int> RunAsync()
    {
        var failed = 0;
        foreach (var row in tenants)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return 1;
            }

            if (!await ActivateTenantAsync(row))
            {
                failed++;
            }
        }

        return failed == 0 && !cancellationToken.IsCancellationRequested ? 0 : 1;
    }

    private async Task<bool> ActivateTenantAsync(TenantRow row)
    {
        var id = row.Id;
        await using var steps = new TenantDatabaseSteps(settings.Cipher, row, row.DatabaseHost, row.DatabasePort, cancellationToken);
        ConfigurationDbContext? configuration = null;
        DataDbContext? data = null;
        ReleaseCompilationResult? compiled = null;
        try
        {
            return await Step(id, "version", () => CheckVersionAsync(row))
                && await Step(id, "compile", async () =>
                {
                    var connection = await steps.SchemaConnectionAsync();
                    configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options);
                    data = new DataDbContext(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(connection).Options);
                    compiled = await ReleaseCompiler.CompileAsync(folder, configuration, packages.Versions, modules.Modules, cancellationToken);
                    WriteWarnings(id, compiled.Diagnostics);
                    if (compiled.Release is null || compiled.Model is null)
                    {
                        throw new DiagnosticsFailure(compiled.Diagnostics);
                    }
                })
                && await Step(id, "activate", async () =>
                {
                    var activated = await ReleaseActivator.ActivateAsync(
                        compiled!, new ActiveReleaseStore(configuration!), data!, bindings[id], OpenInstanceCounter.Instance, inlineRecomputeLimit, cancellationToken);
                    WriteWarnings(id, activated.Diagnostics);
                    if (activated.Diagnostics.Any(IsError))
                    {
                        throw new DiagnosticsFailure(activated.Diagnostics);
                    }
                })
                && await Step(id, "seed", () => SeedAsync(steps, id, compiled!));
        }
        finally
        {
            if (configuration is not null)
            {
                await configuration.DisposeAsync();
            }

            if (data is not null)
            {
                await data.DisposeAsync();
            }
        }
    }

    // The hosts compare the version recorded in the directory with their build's to decide whether to serve a tenant,
    // so this does the same and reads no tenant database.
    private static Task CheckVersionAsync(TenantRow row) =>
        row.SchemaVersion == TenantSchemaVersion.Current
            ? Task.CompletedTask
            : throw new InvalidOperationException("The tenant database is not on this build's schema version. Run migrate first.");

    private async Task SeedAsync(TenantDatabaseSteps steps, string id, ReleaseCompilationResult compiled)
    {
        var model = compiled.Model!;
        await using var runtime = await steps.OpenRuntimeConnectionAsync();
        await TriggerSchedules.ActivateAsync(runtime, id, model, compiled.Release!.Id, cancellationToken);
        if (model.Seeds.Count > 0)
        {
            var seeded = await SeedApplier.ApplyAsync(model, compiled.Release.Id, runtime, cancellationToken);
            WriteWarnings(id, seeded.Diagnostics);
            if (seeded.Diagnostics.Any(IsError))
            {
                throw new DiagnosticsFailure(seeded.Diagnostics);
            }
        }
    }

    private static bool IsError(Diagnostic diagnostic) => diagnostic.Severity != DiagnosticSeverity.Warning;

    private void WriteWarnings(string id, IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics.Where(diagnostic => !IsError(diagnostic)))
        {
            error.WriteLine($"{id} warning {Describe(diagnostic)}");
        }
    }

    private static string Describe(Diagnostic diagnostic) =>
        $"{diagnostic.Code} {diagnostic.File} {diagnostic.Path}: {diagnostic.Message}";

    // Runs one step. Any failure, also a stop by Ctrl+C, writes a failed line and ends that tenant. A failure with diagnostics
    // writes one line for each error.
    private async Task<bool> Step(string id, string name, Func<Task> work)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await work();
        }
        catch (DiagnosticsFailure failure)
        {
            foreach (var diagnostic in failure.Diagnostics.Where(IsError))
            {
                output.WriteLine($"{id} {name} failed: {Describe(diagnostic)}");
            }

            return false;
        }
        catch (Exception exception)
        {
            output.WriteLine($"{id} {name} failed: {TenantDatabaseSteps.Safe(exception)}");
            return false;
        }

        output.WriteLine($"{id} {name} ok");
        return true;
    }

    private sealed class DiagnosticsFailure(IReadOnlyList<Diagnostic> diagnostics) : Exception
    {
        public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    }
}
