using System.Diagnostics;
using System.Globalization;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.Profiles;
using Axis.Data.Records;
using Axis.Data.Seeding;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Processes.Storage;
using Axis.Tenancy.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Scenarios.Runner;

/// <summary>
/// Runs one scenario of an application held in a folder or in files in memory, against its own
/// temporary tenant database. The database is created, migrated,
/// given the compiled and activated application and its seed data, and dropped when the scenario
/// ends, whether it passed or failed. The scenario runs as the tenant <see cref="TenantId"/> with
/// placeholder deployment bindings, never a real URL or secret. The tenant has a random data key and lookup key that live
/// only in memory for the run, so sensitive values are encrypted as on a real tenant. It reads the manifests of the
/// installed extension packages it is given, and runs none of their code. Each step runs as its user,
/// with the roles the scenario gives that user. The first failing step ends the scenario. After each
/// action the runner runs the due process steps, the connector and package calls and the event
/// deliveries in-process, so the process reaches its next wait. Schedule triggers stay queued after an
/// action. An <c>advanceTime</c> step moves the scenario's <c>now</c> forward and runs every due work
/// item, schedule triggers and fan-outs included. The scenario's stubs answer the calls. The scenario's
/// <c>now</c> reaches every connection as the PostgreSQL setting <c>axis.now</c>, which the
/// transaction time and the SQL of <c>now()</c> read. The work queue compares due times with it too.
/// Times that PostgreSQL writes itself, such as <c>created_at</c>, and lease times keep the real clock.
/// Each scenario user has a profile with the address <c>{user}@scenario.invalid</c>, so a <c>notify</c> step
/// stages an email for the user. Emails stay in the outbox and are never sent.
/// </summary>
public static class ScenarioRunner
{
    /// <summary>The tenant id every scenario runs under.</summary>
    public const string TenantId = "scenario";

    /// <summary>The domain of the address of every scenario user's profile, <c>{user}@scenario.invalid</c>. It is a reserved name that never resolves.</summary>
    public const string EmailDomain = "scenario.invalid";

    /// <summary>The most work items that may run after one action.</summary>
    public const int WorkItemLimit = 1000;

    /// <summary>
    /// Runs <paramref name="scenario"/> of the application in <paramref name="folder"/> on the
    /// server of <paramref name="serverConnectionString"/>, whose login may create databases.
    /// A cancelled run still drops the database, also when the cancel came while it was being created, then throws <see cref="OperationCanceledException"/>.
    /// An action that makes more than <paramref name="workItemLimit"/> work items run fails the scenario.
    /// <paramref name="packages"/> are the installed extension packages whose manifests the application
    /// is compiled against, or null for none. <paramref name="modules"/> are the installed modules it is
    /// compiled against, or null for none.
    /// </summary>
    public static Task<ScenarioResult> RunAsync(
        string folder,
        ScenarioResource scenario,
        string serverConnectionString,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        int workItemLimit = WorkItemLimit,
        PackageCatalog? packages = null,
        ModuleCatalog? modules = null)
    {
        packages ??= PackageCatalog.Empty;
        modules ??= ModuleCatalog.Empty;
        return RunAsync(
            (context, token) => ReleaseCompiler.CompileAsync(folder, context, packages.Versions, modules.Modules, token),
            scenario,
            serverConnectionString,
            loggerFactory,
            workItemLimit,
            packages,
            null,
            cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="scenario"/> of the application whose resource files are
    /// <paramref name="files"/>, held in memory, such as a snapshot. Otherwise it behaves as the
    /// overload that reads a folder.
    /// </summary>
    public static Task<ScenarioResult> RunAsync(
        IReadOnlyList<ResourceContent> files,
        ScenarioResource scenario,
        string serverConnectionString,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        int workItemLimit = WorkItemLimit,
        PackageCatalog? packages = null,
        ModuleCatalog? modules = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        packages ??= PackageCatalog.Empty;
        modules ??= ModuleCatalog.Empty;
        return RunAsync(
            (context, token) => ReleaseCompiler.CompileAsync(files, context, packages.Versions, modules.Modules, token),
            scenario,
            serverConnectionString,
            loggerFactory,
            workItemLimit,
            packages,
            null,
            cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="scenario"/> of the application in <paramref name="folder"/> as the public overload does. After the last
    /// step, and before the database is dropped, <paramref name="beforeDrop"/> gets the connection string of the scenario's database,
    /// so a test can read what the scenario stored. An exception of the callback fails the scenario.
    /// </summary>
    internal static Task<ScenarioResult> RunAsync(
        string folder,
        ScenarioResource scenario,
        string serverConnectionString,
        ILoggerFactory loggerFactory,
        Func<string, CancellationToken, Task> beforeDrop,
        CancellationToken cancellationToken,
        PackageCatalog? packages = null)
    {
        packages ??= PackageCatalog.Empty;
        return RunAsync(
            (context, token) => ReleaseCompiler.CompileAsync(folder, context, packages.Versions, ModuleCatalog.Empty.Modules, token),
            scenario,
            serverConnectionString,
            loggerFactory,
            WorkItemLimit,
            packages,
            beforeDrop,
            cancellationToken);
    }

    private static async Task<ScenarioResult> RunAsync(
        Func<ConfigurationDbContext, CancellationToken, Task<ReleaseCompilationResult>> compile,
        ScenarioResource scenario,
        string serverConnectionString,
        ILoggerFactory loggerFactory,
        int workItemLimit,
        PackageCatalog packages,
        Func<string, CancellationToken, Task>? beforeDrop,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var database = $"axis_scenario_{Guid.NewGuid():N}";
        ScenarioResult Result(ScenarioFailure? failure) => new(scenario.Name, scenario.File, database, stopwatch.Elapsed, failure?.Step, failure?.StepType, failure?.Message);

        try
        {
            await ExecuteAsync(serverConnectionString, $"""CREATE DATABASE "{database}" """, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The cancel may have come after the server committed the database.
            try
            {
                await DropAsync(serverConnectionString, database);
            }
            catch (Exception)
            {
                // The cancel is what the caller must see.
            }

            throw;
        }
        catch (Exception exception)
        {
            return Result(new(null, null, $"The temporary database could not be created: {exception.Message}"));
        }

        ScenarioFailure? failure;
        ScenarioFailure? dropFailure = null;
        try
        {
            // The connection does not pool, so no idle session keeps the database from being dropped.
            var builder = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = database, Pooling = false };
            DateTimeOffset? now = scenario.Now is { } text ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture).ToUniversalTime() : null;

            // The setting reaches every connection of the scenario, including the work item runner's.
            var connectionString = now is { } time ? WithNow(builder.ConnectionString, time) : builder.ConnectionString;
            failure = await RunInDatabaseAsync(compile, scenario, builder.ConnectionString, connectionString, now, loggerFactory, workItemLimit, packages, cancellationToken);
            if (beforeDrop is not null)
            {
                await beforeDrop(builder.ConnectionString, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failure = new(null, null, $"{exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            try
            {
                await DropAsync(serverConnectionString, database);
            }
            catch (Exception exception)
            {
                dropFailure = new(null, null, $"The temporary database could not be dropped: {exception.Message}");
            }
        }

        return Result(failure ?? dropFailure);
    }

    private static string WithNow(string connectionString, DateTimeOffset now)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var option = "-c axis.now=" + FormatNow(now);
        builder.Options = string.IsNullOrEmpty(builder.Options) ? option : builder.Options + " " + option;
        return builder.ConnectionString;
    }

    private static string FormatNow(DateTimeOffset now) =>
        now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    // A role that only has CREATEDB cannot use WITH (FORCE) while a superuser's process, such as an autovacuum worker, is in the database.
    // A plain drop cancels autovacuum and waits for sessions that are closing, so it is tried first and retried. The last try forces it.
    private static async Task DropAsync(string serverConnectionString, string database)
    {
        const int attempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var force = attempt == attempts ? " WITH (FORCE)" : "";
                await ExecuteAsync(serverConnectionString, $"""DROP DATABASE IF EXISTS "{database}"{force}""", CancellationToken.None);
                return;
            }
            catch (PostgresException) when (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), CancellationToken.None);
            }
        }
    }

    // CREATE DATABASE and DROP DATABASE cannot run in a transaction block, so each is its own command.
    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record ScenarioFailure(int? Step, string? StepType, string Message);

    private static async Task<ScenarioFailure?> RunInDatabaseAsync(
        Func<ConfigurationDbContext, CancellationToken, Task<ReleaseCompilationResult>> compile,
        ScenarioResource scenario,
        string baseConnectionString,
        string connectionString,
        DateTimeOffset? now,
        ILoggerFactory loggerFactory,
        int workItemLimit,
        PackageCatalog packages,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options);
        await using var data = new DataDbContext(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(connection).Options);
        await using var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connection).Options);
        await configuration.Database.MigrateAsync(cancellationToken);
        await data.Database.MigrateAsync(cancellationToken);
        await processes.Database.MigrateAsync(cancellationToken);

        var compiled = await compile(configuration, cancellationToken);
        if (compiled.Release is null || compiled.Model is null)
        {
            return Setup("could not be compiled", compiled.Diagnostics);
        }

        // The keys are random and live in memory for this run, so sensitive values are encrypted as on a real tenant.
        var sensitive = new SensitiveValues(new TenantKeys(new ScenarioKeys()));
        var cipher = new RecordCipher(sensitive, TenantId);

        var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ScenarioBindings.ForActivation(compiled.Model) with { Sensitive = sensitive }, OpenInstanceCounter.Instance, cancellationToken);
        if (activated.Diagnostics.Count > 0)
        {
            return Setup("could not be activated", activated.Diagnostics);
        }

        var model = compiled.Model;
        await TriggerSchedules.ActivateAsync(connection, TenantId, model, compiled.Release.Id, cancellationToken);
        if (model.Seeds.Count > 0)
        {
            var seeded = await SeedApplier.ApplyAsync(model, compiled.Release.Id, connection, cipher, cancellationToken);
            if (seeded.Diagnostics.Count > 0)
            {
                return Setup("could not be seeded", seeded.Diagnostics);
            }
        }

        // Every scenario user has a profile, so an email to the user is staged. No email is ever sent.
        foreach (var user in scenario.Users.Keys)
        {
            await UserProfiles.UpsertAsync(connection, user, user, $"{user}@{EmailDomain}", null, cancellationToken);
        }

        var steps = new ScenarioSteps(connection, model, compiled.Release.Id, scenario, cipher, loggerFactory);
        using var work = new ScenarioWork(connectionString, loggerFactory, workItemLimit, packages, new ScenarioStubs(scenario.Stubs), sensitive);
        for (var index = 0; index < scenario.Steps.Count; index++)
        {
            var step = scenario.Steps[index];
            try
            {
                if (step.Type == ScenarioStepDefinition.AdvanceTime)
                {
                    if (now is not { } current || step.By is null || !IsoDuration.TryParse(step.By, out var by))
                    {
                        throw new StepFailedException("the step needs the scenario's 'now' and a positive 'by'");
                    }

                    now = current + by;
                    await using (var command = new NpgsqlCommand("SELECT set_config('axis.now', @now, false)", connection))
                    {
                        command.Parameters.AddWithValue("now", FormatNow(now.Value));
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await work.AdvanceAsync(WithNow(baseConnectionString, now.Value), cancellationToken);
                    continue;
                }

                await steps.RunAsync(step, cancellationToken);
                if (IsAction(step))
                {
                    await work.RunDueAsync(cancellationToken);
                }
            }
            catch (StepFailedException exception)
            {
                return new(index, step.Type, exception.Message);
            }
        }

        return null;
    }

    private static bool IsAction(ScenarioStepDefinition step) =>
        step.Type is ScenarioStepDefinition.CreateRecord
            or ScenarioStepDefinition.UpdateRecord
            or ScenarioStepDefinition.DeleteRecord
            or ScenarioStepDefinition.StartProcess
            or ScenarioStepDefinition.CompleteTask;

    private static ScenarioFailure Setup(string reason, IReadOnlyList<Diagnostic> diagnostics)
    {
        var first = diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ?? diagnostics[0];
        return new(null, null, $"The application {reason}: {first.Code} {first.Message}");
    }
}
