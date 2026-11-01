using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Realms;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.Records;
using Axis.Data.Seeding;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Scenarios.Runner;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axis.Admin;

/// <summary>The outcome of the steps for one tenant: whether all passed, and the release once <c>compile</c> stored one.</summary>
internal readonly record struct ReleaseRun(bool Succeeded, Guid? ReleaseId);

/// <summary>The outcome of a test: whether the steps passed, and one JSON object for each scenario that ran, in the order they ran.</summary>
internal readonly record struct ScenarioTestRun(bool Succeeded, JsonArray Scenarios);

/// <summary>
/// The version, compile, activate, seed and indexes steps for one tenant, which <c>activate</c> and the publisher share. The caller
/// says how to compile: from a folder, or from a snapshot. A tenant stops at its first failed step and keeps its previous
/// active release. A validate runs the version step and one validate step instead: the compile and every activation check
/// run in one transaction that is always rolled back, and no seed or schedule trigger step runs. A test runs the version step
/// and one test step: it compiles the snapshot, then runs the chosen scenarios one at a time, each in its own temporary
/// database on the scenario server, and touches no tenant database. Each step writes one <c>ok</c> line, or one <c>failed</c> line for each error diagnostic. A line names the
/// tenant and never a password, a host or a connection string. Compile, activate and indexes run as the tenant's schema role. The
/// schedule trigger state and the seed records run as its runtime role. The indexes step builds the non-unique indexes that
/// activation left out of a large table, and runs last, so a slow build holds back nothing else. Warnings go to the error writer.
/// When the caller gives a realm sync, the activate step applies the realm plan to the tenant's realm after the activation
/// committed. A realm failure fails that step, so the new release stays active and the seed and indexes steps wait for a rerun.
/// A validate and a test never update the realm.
/// </summary>
internal sealed class TenantReleaseActivation(
    int inlineRecomputeLimit,
    TimeSpan activationTimeout,
    SensitiveValues sensitive,
    TextWriter output,
    TextWriter error,
    CancellationToken cancellationToken)
{
    /// <summary>
    /// Runs the steps for the tenant <paramref name="id"/>, whose directory entry or build records <paramref name="schemaVersion"/>. Every diagnostic of compile, activate and seed, warnings included, is added
    /// to <paramref name="diagnostics"/>. The release history records <paramref name="actor"/> for the activation.
    /// <paramref name="loadStep"/> names the second step, which loads the release: <c>compile</c>, or <c>rollback</c> for a stored release.
    /// <paramref name="syncRealm"/> applies the realm plan of the activation to the tenant's realm, or is <c>null</c> when the tenant has no realm to update.
    /// </summary>
    public async Task<ReleaseRun> RunAsync(
        string id,
        string schemaVersion,
        ITenantConnections connections,
        ActivationBindings bindings,
        Func<ConfigurationDbContext, Task<ReleaseCompilationResult>> compile,
        List<Diagnostic> diagnostics,
        ActivationActor actor,
        string loadStep = "compile",
        Func<RealmPlan, Task>? syncRealm = null,
        Func<Task<bool>>? contracts = null)
    {
        ConfigurationDbContext? configuration = null;
        DataDbContext? data = null;
        ReleaseCompilationResult? compiled = null;
        try
        {
            var succeeded = await Step(id, "version", () => CheckVersionAsync(schemaVersion))
                && (contracts is null || await contracts())
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
                        compiled!, new ActiveReleaseStore(configuration!), data!, bindings with { Sensitive = sensitive }, OpenInstanceCounter.Instance, inlineRecomputeLimit, activationTimeout, actor, cancellationToken);
                    diagnostics.AddRange(activated.Diagnostics);
                    WriteWarnings(id, activated.Diagnostics);
                    if (activated.Diagnostics.Any(IsError))
                    {
                        throw new DiagnosticsFailure(activated.Diagnostics);
                    }

                    if (syncRealm is not null && activated.Realm is { } plan)
                    {
                        await syncRealm(plan);
                    }
                })
                && await Step(id, "seed", () => SeedAsync(connections, id, compiled!, diagnostics))
                && await Step(id, "indexes", () => BuildIndexesAsync(connections, compiled!));
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
    /// The realm sync for <see cref="RunAsync"/>: it applies a realm plan to the realm of <paramref name="realm"/> with the tenant's
    /// <paramref name="bindings"/>, then writes <paramref name="smtpServer"/> as the realm's mail server. It is <c>null</c> when the tenant has no realm details,
    /// such as a preview tenant, so the step skips the sync.
    /// </summary>
    public static Func<RealmPlan, Task>? RealmSync(
        HttpClient http,
        TenantRealmAccess? realm,
        IReadOnlyDictionary<string, TenantBinding> bindings,
        IReadOnlyDictionary<string, string> smtpServer,
        CancellationToken cancellationToken) =>
        realm is null
            ? null
            : async plan =>
            {
                await ActivationRealmSync.SyncAsync(http, realm, bindings, plan, cancellationToken);
                await new RealmSync(http, realm.BaseAddress).SyncMailServerAsync(realm.Name, realm.ActivationClientSecret.Reveal(), smtpServer, cancellationToken);
            };

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
        List<Diagnostic> diagnostics,
        Func<Task<bool>>? contracts = null)
    {
        return await Step(id, "version", () => CheckVersionAsync(schemaVersion))
            && (contracts is null || await contracts())
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
                        compiled, new ActiveReleaseStore(configuration), data, bindings with { Sensitive = sensitive }, OpenInstanceCounter.Instance, inlineRecomputeLimit, activationTimeout, ActivationActor.Startup, cancellationToken);
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

    /// <summary>
    /// Runs the <c>version</c> step and then the <c>test</c> step for the tenant <paramref name="id"/>. The test step fails
    /// with <c>AXC0217</c> when <paramref name="scenarioServer"/> is null, and when the snapshot cannot be read, which is a null
    /// <paramref name="files"/>, or does not compile. It fails with <c>AXC0218</c> for each name of
    /// <paramref name="scenarioNames"/>, a JSON array of strings or null for every scenario, that the application does not have. A name of a module's
    /// scenario is <c>&lt;module id&gt;/&lt;name&gt;</c>, and the module's scenarios run after the application's own.
    /// In each of these cases no scenario runs. Otherwise it runs the chosen scenarios in file order, and fails when one
    /// failed. Every compile diagnostic, warnings included, is added to <paramref name="diagnostics"/>.
    /// The scenarios may run for at most <paramref name="limit"/>, counted from the first of them. A run that
    /// reaches it fails with <c>AXC0219</c>, and one that <paramref name="stopping"/> ends fails with <c>AXC0220</c>. In both
    /// cases the running scenario, or the one about to start, is stored as failed and its database is dropped, and the scenarios
    /// that follow do not run.
    /// </summary>
    public async Task<ScenarioTestRun> TestAsync(
        string id,
        string schemaVersion,
        string? scenarioServer,
        IReadOnlyList<ResourceContent>? files,
        string? scenarioNames,
        PackageCatalog packages,
        ModuleCatalog modules,
        List<Diagnostic> diagnostics,
        TimeSpan limit,
        CancellationToken stopping)
    {
        var results = new JsonArray();
        var succeeded = await Step(id, "version", () => CheckVersionAsync(schemaVersion))
            && await Step(id, "test", async () =>
            {
                if (scenarioServer is null)
                {
                    const string message = "The setting Scenarios:ServerConnectionString is not set, so test scenarios cannot run.";
                    var missing = new Diagnostic(DiagnosticCodes.ScenarioServerMissing, message, "", "");
                    diagnostics.Add(missing);
                    throw new DiagnosticsFailure([missing]);
                }

                if (files is null)
                {
                    throw new InvalidOperationException("The snapshot cannot be read.");
                }

                var compiled = ApplicationCompiler.CompileFiles(files, packages.Versions, modules.Modules);
                diagnostics.AddRange(compiled.Diagnostics);
                WriteWarnings(id, compiled.Diagnostics);
                if (compiled.HasErrors || compiled.Model is null)
                {
                    throw new DiagnosticsFailure(compiled.Diagnostics);
                }

                var scenarios = ApplicationLoader.Load(files).Scenarios.OrderBy(scenario => scenario.File, StringComparer.Ordinal).Concat(compiled.ModuleScenarios).ToList();
                if (scenarioNames is not null)
                {
                    var names = JsonSerializer.Deserialize<List<string>>(scenarioNames) ?? [];
                    var known = scenarios.Select(scenario => scenario.Name).ToHashSet(StringComparer.Ordinal);
                    var unknown = names.Distinct(StringComparer.Ordinal)
                        .Where(name => !known.Contains(name))
                        .Select(name => new Diagnostic(DiagnosticCodes.UnknownScenario, $"The scenario '{name}' is not in the application.", "", ""))
                        .ToList();
                    if (unknown.Count > 0)
                    {
                        diagnostics.AddRange(unknown);
                        throw new DiagnosticsFailure(unknown);
                    }

                    scenarios = scenarios.Where(scenario => names.Contains(scenario.Name, StringComparer.Ordinal)).ToList();
                }

                var failed = await RunScenariosAsync(id, files, scenarios, scenarioServer, packages, modules, diagnostics, results, limit, "test request", stopping);
                if (failed > 0)
                {
                    throw new ScenariosFailure($"{failed} of {scenarios.Count} scenarios failed.");
                }
            });
        return new ScenarioTestRun(succeeded, results);
    }

    /// <summary>
    /// Runs the <c>contracts</c> step for the tenant <paramref name="id"/>: the contract scenarios of the pinned modules, each in its
    /// own temporary database on <paramref name="scenarioServer"/>, before the compile. It compiles <paramref name="files"/> in memory
    /// to find them. When the snapshot cannot be read, which is a null <paramref name="files"/>, or does not compile, and when
    /// there is no contract scenario, it runs nothing, writes no line and returns <c>true</c>, so the next step reports the
    /// compile problem. Otherwise it fails with <c>AXC0217</c> when <paramref name="scenarioServer"/> is null, and with one
    /// <c>AXC0342</c> for each scenario that failed, after every scenario ran. Each outcome is added to <paramref name="results"/>.
    /// The scenarios may run for at most <paramref name="limit"/>, and then fail with <c>AXC0219</c>. A stop never cuts them short.
    /// Returns whether the step passed.
    /// </summary>
    public async Task<bool> ContractsAsync(
        string id,
        string? scenarioServer,
        IReadOnlyList<ResourceContent>? files,
        PackageCatalog packages,
        ModuleCatalog modules,
        List<Diagnostic> diagnostics,
        JsonArray results,
        TimeSpan limit)
    {
        if (files is null)
        {
            return true;
        }

        var compiled = ApplicationCompiler.CompileFiles(files, packages.Versions, modules.Modules);
        if (compiled.HasErrors || compiled.Model is null)
        {
            return true;
        }

        var scenarios = compiled.ModuleScenarios.Where(scenario => scenario.Contract is not null).ToList();
        if (scenarios.Count == 0)
        {
            return true;
        }

        return await Step(id, "contracts", async () =>
        {
            if (scenarioServer is null)
            {
                const string message = "The setting Scenarios:ServerConnectionString is not set, so contract scenarios cannot run.";
                var missing = new Diagnostic(DiagnosticCodes.ScenarioServerMissing, message, "", "");
                diagnostics.Add(missing);
                throw new DiagnosticsFailure([missing]);
            }

            var first = results.Count;
            await RunScenariosAsync(id, files, scenarios, scenarioServer, packages, modules, diagnostics, results, limit, "contract scenarios", CancellationToken.None);
            var failures = new List<Diagnostic>();
            for (var index = 0; index < scenarios.Count; index++)
            {
                if (results[first + index] is not JsonObject outcome || outcome["outcome"]?.GetValue<string>() != "failed")
                {
                    continue;
                }

                // A name is <module id>/<name>.
                var name = scenarios[index].Name;
                var slash = name.IndexOf('/');
                var (module, plain) = slash < 0 ? ("", name) : (name[..slash], name[(slash + 1)..]);
                var step = outcome["step"]?.GetValue<int>();
                failures.Add(new Diagnostic(
                    DiagnosticCodes.ContractScenarioFailed,
                    $"The contract scenario '{plain}' of the module '{module}' failed: {outcome["message"]?.GetValue<string>()}",
                    outcome["file"]?.GetValue<string>() ?? "",
                    step is { } i ? $"/steps/{i}" : "",
                    scenarios[index].Id));
            }

            if (failures.Count > 0)
            {
                diagnostics.AddRange(failures);
                throw new DiagnosticsFailure(failures);
            }
        });
    }

    // Runs the scenarios one at a time, in their own databases, and adds each outcome to results and one line to the output.
    // Returns the number that failed. A run that passes the limit, or that stopping ends, adds its diagnostic and throws.
    private async Task<int> RunScenariosAsync(
        string id,
        IReadOnlyList<ResourceContent> files,
        IReadOnlyList<ScenarioResource> scenarios,
        string server,
        PackageCatalog packages,
        ModuleCatalog modules,
        List<Diagnostic> diagnostics,
        JsonArray results,
        TimeSpan limit,
        string what,
        CancellationToken stopping)
    {
        // The limit counts from here, so a claim that runs again gets the whole time again.
        using var limited = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        limited.CancelAfter(limit);

        // A stop is what ended the run when both happened, because the operator caused it and the test did not.
        Exception Cut(ScenarioResource? cut, TimeSpan elapsed)
        {
            var stoppedByOperator = stopping.IsCancellationRequested;
            var (code, message) = stoppedByOperator
                ? (DiagnosticCodes.ScenarioStopped, $"The publisher stopped while the {what} ran.")
                : (DiagnosticCodes.ScenarioTimeLimit, $"The {what} ran past its time limit of {limit.TotalMinutes:0.##} minutes.");
            if (cut is not null)
            {
                results.Add(ScenarioJson(cut.Id, new ScenarioResult(cut.Name, cut.File, "", elapsed, null, null, message)));
                output.WriteLine($"{id} scenario {cut.Name} failed: {message}");
            }

            var diagnostic = new Diagnostic(code, message, "", "");
            diagnostics.Add(diagnostic);
            return new DiagnosticsFailure([diagnostic]);
        }

        var failed = 0;
        foreach (var scenario in scenarios)
        {
            var started = Stopwatch.StartNew();
            ScenarioResult? result = null;
            if (!limited.IsCancellationRequested)
            {
                try
                {
                    result = await ScenarioRunner.RunAsync(
                        files, scenario, server, NullLoggerFactory.Instance, limited.Token, packages: packages, modules: modules);
                }
                catch (OperationCanceledException) when (limited.IsCancellationRequested)
                {
                    // The runner has dropped its database.
                }
            }

            if (result is null)
            {
                throw Cut(scenario, started.Elapsed);
            }

            results.Add(ScenarioJson(scenario.Id, result));
            if (result.Failure is { } failure)
            {
                failed++;
                output.WriteLine($"{id} scenario {result.Name} failed: {failure}");
                if (limited.IsCancellationRequested)
                {
                    // The cancel surfaced as a failure of the scenario, not as a cancel.
                    throw Cut(null, started.Elapsed);
                }
            }
            else
            {
                output.WriteLine($"{id} scenario {result.Name} passed");
            }
        }

        return failed;
    }

    // The shape of a scenario result in the authoring API. The path is that of the failing step in the scenario file.
    private static JsonObject ScenarioJson(Guid resourceId, ScenarioResult result) =>
        new()
        {
            ["name"] = result.Name,
            ["resourceId"] = resourceId.ToString("D"),
            ["file"] = result.File,
            ["outcome"] = result.Passed ? "passed" : "failed",
            ["durationMs"] = (long)result.Duration.TotalMilliseconds,
            ["step"] = result.Step,
            ["path"] = result.Step is { } index ? $"/steps/{index}" : null,
            ["message"] = result.Message,
        };

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
            var seeded = await SeedApplier.ApplyAsync(model, compiled.Release.Id, runtime, new RecordCipher(sensitive, id), cancellationToken);
            diagnostics.AddRange(seeded.Diagnostics);
            WriteWarnings(id, seeded.Diagnostics);
            if (seeded.Diagnostics.Any(IsError))
            {
                throw new DiagnosticsFailure(seeded.Diagnostics);
            }
        }
    }

    // Builds the non-unique indexes that activation left out of a large table, as the schema role over its connection. Activation
    // has committed by now, so the connection has no transaction, which CREATE INDEX CONCURRENTLY needs.
    private async Task BuildIndexesAsync(ITenantConnections connections, ReleaseCompilationResult compiled) =>
        _ = await EntityIndexBuilder.BuildAsync(compiled.Model!, await connections.SchemaConnectionAsync(), cancellationToken);

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
        catch (ScenariosFailure failure)
        {
            output.WriteLine($"{id} {name} failed: {failure.Message}");
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

    // A scenario that failed is not a diagnostic: its outcome is in the results, and this only ends the step.
    private sealed class ScenariosFailure(string message) : Exception(message);

    private sealed class DiagnosticsFailure(IReadOnlyList<Diagnostic> diagnostics) : Exception
    {
        public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    }
}
