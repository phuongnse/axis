using System.Diagnostics;
using Axis.Configuration.Diagnostics;
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
/// no deployment bindings and no extension packages. Each step runs as its user, with the roles the
/// scenario gives that user. The first failing step ends the scenario.
/// </summary>
public static class ScenarioRunner
{
    /// <summary>The tenant id every scenario runs under.</summary>
    public const string TenantId = "scenario";

    /// <summary>
    /// Runs <paramref name="scenario"/> of the application in <paramref name="folder"/> on the
    /// server of <paramref name="serverConnectionString"/>, whose login may create databases.
    /// A cancelled run still drops the database, then throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<ScenarioResult> RunAsync(
        string folder,
        ScenarioResource scenario,
        string serverConnectionString,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
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
            var connectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = database, Pooling = false }.ConnectionString;
            failure = await RunInDatabaseAsync(folder, scenario, connectionString, loggerFactory, cancellationToken);
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
        string connectionString,
        ILoggerFactory loggerFactory,
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

        var compiled = await ReleaseCompiler.CompileAsync(folder, configuration, [], cancellationToken);
        if (compiled.Release is null || compiled.Model is null)
        {
            return Setup("could not be compiled", compiled.Diagnostics);
        }

        var bindings = new ActivationBindings(TenantId, new Dictionary<string, ActivationBinding>(), AllowHttp: false);
        var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, bindings, OpenInstanceCounter.Instance, cancellationToken);
        if (activated.Diagnostics.Count > 0)
        {
            return Setup("could not be activated", activated.Diagnostics);
        }

        var model = compiled.Model;
        if (model.Seeds.Count > 0)
        {
            var seeded = await SeedApplier.ApplyAsync(model, connection, cancellationToken);
            if (seeded.Diagnostics.Count > 0)
            {
                return Setup("could not be seeded", seeded.Diagnostics);
            }
        }

        var steps = new ScenarioSteps(connection, model, compiled.Release.Id, scenario, loggerFactory);
        for (var index = 0; index < scenario.Steps.Count; index++)
        {
            var step = scenario.Steps[index];
            try
            {
                await steps.RunAsync(step, cancellationToken);
            }
            catch (StepFailedException exception)
            {
                return $"Step {index + 1} ({step.Type}): {exception.Message}";
            }
        }

        return null;
    }

    private static string Setup(string reason, IReadOnlyList<Diagnostic> diagnostics)
    {
        var first = diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ?? diagnostics[0];
        return $"The application {reason}: {first.Code} {first.Message}";
    }
}
