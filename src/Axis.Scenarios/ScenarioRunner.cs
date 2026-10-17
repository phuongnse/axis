using System.Diagnostics;
using System.Globalization;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.Seeding;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Processes.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Scenarios;

/// <summary>
/// Runs one scenario against its own temporary tenant database. The database is created, migrated,
/// given the compiled and activated application and its seed data, and dropped when the scenario
/// ends, whether it passed or failed. The scenario runs as the tenant <see cref="TenantId"/> with
/// placeholder deployment bindings, never a real URL or secret. It reads the manifests of the
/// installed extension packages it is given, and runs none of their code. Each step runs as its user,
/// with the roles the scenario gives that user. The first failing step ends the scenario. After each
/// action the runner runs the due process steps, the connector and package calls and the event
/// deliveries in-process, so the process reaches its next wait. Schedule triggers stay queued after an
/// action. An <c>advanceTime</c> step moves the scenario's <c>now</c> forward and runs every due work
/// item, schedule triggers and fan-outs included. The scenario's stubs answer the calls. The scenario's
/// <c>now</c> reaches every connection as the PostgreSQL setting <c>axis.now</c>, which the
/// transaction time and the SQL of <c>now()</c> read. The work queue compares due times with it too.
/// Times that PostgreSQL writes itself, such as <c>created_at</c>, and lease times keep the real clock.
/// </summary>
public static class ScenarioRunner
{
    /// <summary>The tenant id every scenario runs under.</summary>
    public const string TenantId = "scenario";

    /// <summary>The most work items that may run after one action.</summary>
    public const int WorkItemLimit = 1000;

    /// <summary>
    /// Runs <paramref name="scenario"/> of the application in <paramref name="folder"/> on the
    /// server of <paramref name="serverConnectionString"/>, whose login may create databases.
    /// A cancelled run still drops the database, then throws <see cref="OperationCanceledException"/>.
    /// An action that makes more than <paramref name="workItemLimit"/> work items run fails the scenario.
    /// <paramref name="packages"/> are the installed extension packages whose manifests the application
    /// is compiled against, or null for none.
    /// </summary>
    public static async Task<ScenarioResult> RunAsync(
        string folder,
        ScenarioResource scenario,
        string serverConnectionString,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        int workItemLimit = WorkItemLimit,
        PackageCatalog? packages = null)
    {
        packages ??= PackageCatalog.Empty;
        var stopwatch = Stopwatch.StartNew();
        var database = $"axis_scenario_{Guid.NewGuid():N}";
        ScenarioResult Result(string? failure) => new(scenario.Name, scenario.File, database, stopwatch.Elapsed, failure);

        try
        {
            await ExecuteAsync(serverConnectionString, $"""CREATE DATABASE "{database}" """, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result($"The temporary database could not be created: {exception.Message}");
        }

        string? failure;
        string? dropFailure = null;
        try
        {
            // The connection does not pool, so no idle session keeps the database from being dropped.
            var builder = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = database, Pooling = false };
            DateTimeOffset? now = scenario.Now is { } text ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture).ToUniversalTime() : null;

            // The setting reaches every connection of the scenario, including the work item runner's.
            var connectionString = now is { } time ? WithNow(builder.ConnectionString, time) : builder.ConnectionString;
            failure = await RunInDatabaseAsync(folder, scenario, builder.ConnectionString, connectionString, now, loggerFactory, workItemLimit, packages, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failure = $"{exception.GetType().Name}: {exception.Message}";
        }
        finally
        {
            try
            {
                await ExecuteAsync(serverConnectionString, $"""DROP DATABASE IF EXISTS "{database}" WITH (FORCE)""", CancellationToken.None);
            }
            catch (Exception exception)
            {
                dropFailure = $"The temporary database could not be dropped: {exception.Message}";
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

    // CREATE DATABASE and DROP DATABASE cannot run in a transaction block, so each is its own command.
    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string?> RunInDatabaseAsync(
        string folder,
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

        var compiled = await ReleaseCompiler.CompileAsync(folder, configuration, packages.Versions, cancellationToken);
        if (compiled.Release is null || compiled.Model is null)
        {
            return Setup("could not be compiled", compiled.Diagnostics);
        }

        var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ScenarioBindings.ForActivation(compiled.Model), OpenInstanceCounter.Instance, cancellationToken);
        if (activated.Diagnostics.Count > 0)
        {
            return Setup("could not be activated", activated.Diagnostics);
        }

        var model = compiled.Model;
        await TriggerSchedules.ActivateAsync(connection, TenantId, model, compiled.Release.Id, cancellationToken);
        if (model.Seeds.Count > 0)
        {
            var seeded = await SeedApplier.ApplyAsync(model, compiled.Release.Id, connection, cancellationToken);
            if (seeded.Diagnostics.Count > 0)
            {
                return Setup("could not be seeded", seeded.Diagnostics);
            }
        }

        var steps = new ScenarioSteps(connection, model, compiled.Release.Id, scenario, loggerFactory);
        using var work = new ScenarioWork(connectionString, loggerFactory, workItemLimit, packages, new ScenarioStubs(scenario.Stubs));
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
                return $"Step {index + 1} ({step.Type}): {exception.Message}";
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

    private static string Setup(string reason, IReadOnlyList<Diagnostic> diagnostics)
    {
        var first = diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ?? diagnostics[0];
        return $"The application {reason}: {first.Code} {first.Message}";
    }
}
