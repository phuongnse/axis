using Axis.Configuration.Diagnostics;
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

/// <summary>The outcome of the steps for one tenant: whether all passed, and the release once <c>compile</c> stored one.</summary>
internal readonly record struct ReleaseRun(bool Succeeded, Guid? ReleaseId);

/// <summary>
/// The version, compile, activate and seed steps for one tenant, which <c>activate</c> and the publisher share. The caller
/// says how to compile: from a folder, or from a snapshot. A tenant stops at its first failed step and keeps its previous
/// active release. A validate runs the version step and one validate step instead: the compile and every activation check
/// run in one transaction that is always rolled back, and no seed or schedule trigger step runs. Each step writes one <c>ok</c> line, or one <c>failed</c> line for each error diagnostic. A line names the
/// tenant and never a password, a host or a connection string. Compile and activate run as the tenant's schema role. The
/// schedule trigger state and the seed records run as its runtime role. Warnings go to the error writer.
/// </summary>
internal sealed class TenantReleaseActivation(
    int inlineRecomputeLimit,
    TextWriter output,
    TextWriter error,
    CancellationToken cancellationToken)
{
    /// <summary>
    /// Runs the steps for the tenant <paramref name="id"/>, whose directory entry or build records <paramref name="schemaVersion"/>. Every diagnostic of compile, activate and seed, warnings included, is added
    /// to <paramref name="diagnostics"/>. The release history records <paramref name="actor"/> for the activation.
    /// <paramref name="loadStep"/> names the second step, which loads the release: <c>compile</c>, or <c>rollback</c> for a stored release.
    /// </summary>
    public async Task<ReleaseRun> RunAsync(
        string id,
        string schemaVersion,
        ITenantConnections connections,
        ActivationBindings bindings,
        Func<ConfigurationDbContext, Task<ReleaseCompilationResult>> compile,
        List<Diagnostic> diagnostics,
        ActivationActor actor,
        string loadStep = "compile")
    {
        ConfigurationDbContext? configuration = null;
        DataDbContext? data = null;
        ReleaseCompilationResult? compiled = null;
        try
        {
            var succeeded = await Step(id, "version", () => CheckVersionAsync(schemaVersion))
                && await Step(id, loadStep, async () =>
                {
                    var connection = await connections.SchemaConnectionAsync();
                    configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options);
                    data = new DataDbContext(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(connection).Options);
                    compiled = await compile(configuration);
                    diagnostics.AddRange(compiled.Diagnostics);
                    WriteWarnings(id, compiled.Diagnostics);
                    if (compiled.Release is null || compiled.Model is null)
                    {
                        throw new DiagnosticsFailure(compiled.Diagnostics);
                    }
                })
                && await Step(id, "activate", async () =>
                {
                    var activated = await ReleaseActivator.ActivateAsync(
                        compiled!, new ActiveReleaseStore(configuration!), data!, bindings, OpenInstanceCounter.Instance, inlineRecomputeLimit, actor, cancellationToken);
                    diagnostics.AddRange(activated.Diagnostics);
                    WriteWarnings(id, activated.Diagnostics);
                    if (activated.Diagnostics.Any(IsError))
                    {
                        throw new DiagnosticsFailure(activated.Diagnostics);
                    }
                })
                && await Step(id, "seed", () => SeedAsync(connections, id, compiled!, diagnostics));
            return new ReleaseRun(succeeded, compiled?.Release?.Id);
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

    /// <summary>
    /// Runs the <c>version</c> step and then the <c>validate</c> step for the tenant <paramref name="id"/>. The validate step compiles
    /// and runs every activation check inside one transaction on the schema connection, then rolls it back, so the tenant
    /// database is unchanged. A null <paramref name="compile"/> means that the snapshot cannot be read, and fails the step.
    /// Every diagnostic, warnings included, is added to <paramref name="diagnostics"/>. Returns whether both steps passed.
    /// </summary>
    public async Task<bool> ValidateAsync(
        string id,
        string schemaVersion,
        ITenantConnections connections,
        ActivationBindings bindings,
        Func<ConfigurationDbContext, Task<ReleaseCompilationResult>>? compile,
        List<Diagnostic> diagnostics)
    {
        return await Step(id, "version", () => CheckVersionAsync(schemaVersion))
            && await Step(id, "validate", async () =>
            {
                if (compile is null)
                {
                    throw new InvalidOperationException("The snapshot cannot be read.");
                }

                var connection = await connections.SchemaConnectionAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options);
                    await using var data = new DataDbContext(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(connection).Options);
                    await configuration.Database.UseTransactionAsync(transaction);
                    await data.Database.UseTransactionAsync(transaction);
                    var compiled = await compile(configuration);
                    diagnostics.AddRange(compiled.Diagnostics);
                    WriteWarnings(id, compiled.Diagnostics);
                    if (compiled.Release is null || compiled.Model is null)
                    {
                        throw new DiagnosticsFailure(compiled.Diagnostics);
                    }

                    var activated = await ReleaseActivator.ActivateAsync(
                        compiled, new ActiveReleaseStore(configuration), data, bindings, OpenInstanceCounter.Instance, inlineRecomputeLimit, cancellationToken);
                    diagnostics.AddRange(activated.Diagnostics);
                    WriteWarnings(id, activated.Diagnostics);
                    if (activated.Diagnostics.Any(IsError))
                    {
                        throw new DiagnosticsFailure(activated.Diagnostics);
                    }
                }
                finally
                {
                    // Nothing is kept. This also frees the connection for the final status.
                    await transaction.RollbackAsync(CancellationToken.None);
                }
            });
    }

    public static bool IsError(Diagnostic diagnostic) => diagnostic.Severity != DiagnosticSeverity.Warning;

    // The hosts compare the version recorded in the directory with their build's to decide whether to serve a tenant,
    // so this does the same and reads no tenant database.
    private static Task CheckVersionAsync(string schemaVersion) =>
        schemaVersion == TenantSchemaVersion.Current
            ? Task.CompletedTask
            : throw new InvalidOperationException("The tenant database is not on this build's schema version. Run migrate first.");

    private async Task SeedAsync(ITenantConnections connections, string id, ReleaseCompilationResult compiled, List<Diagnostic> diagnostics)
    {
        var model = compiled.Model!;
        await using var runtime = await connections.OpenRuntimeConnectionAsync();
        await TriggerSchedules.ActivateAsync(runtime, id, model, compiled.Release!.Id, cancellationToken);
        if (model.Seeds.Count > 0)
        {
            var seeded = await SeedApplier.ApplyAsync(model, compiled.Release.Id, runtime, cancellationToken);
            diagnostics.AddRange(seeded.Diagnostics);
            WriteWarnings(id, seeded.Diagnostics);
            if (seeded.Diagnostics.Any(IsError))
            {
                throw new DiagnosticsFailure(seeded.Diagnostics);
            }
        }
    }

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
