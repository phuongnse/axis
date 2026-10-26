using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class ProcessSetConditionTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    // Each test uses its own application and entity ids and names, so tests sharing the database do
    // not see each other's tables and records.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";
    private readonly Guid _softwareId = Guid.NewGuid();
    private readonly Guid _workstationId = Guid.NewGuid();

    private const string HasOffice = "any(software, name == 'Office')";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_decision_condition_over_a_set_reads_the_stored_set_of_the_subject()
    {
        var model = await ActivateAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        var office = await CreateAsync(connection, model, "Software", null, ("name", "Office"));
        var paint = await CreateAsync(connection, model, "Software", null, ("name", "Paint"));
        var withOffice = await CreateAsync(connection, model, "Workstation", new[] { paint, office }, ("name", "Desk 1"));
        var withPaint = await CreateAsync(connection, model, "Workstation", new[] { paint }, ("name", "Desk 2"));
        var empty = await CreateAsync(connection, model, "Workstation", null, ("name", "Desk 3"));

        Assert.Equal(true, await EvaluateAsync(connection, model, withOffice));
        Assert.Equal(false, await EvaluateAsync(connection, model, withPaint));
        Assert.Equal(false, await EvaluateAsync(connection, model, empty));
    }

    /// <summary>Evaluates the decision condition against the stored workstation <paramref name="id"/>.</summary>
    private static async Task<object?> EvaluateAsync(NpgsqlConnection connection, ApplicationModel model, Guid id)
    {
        Assert.True(model.TryGetEntity("Workstation", out var entity));
        Assert.True(model.TryGetProcess("Routing", out var process));
        var record = await RecordQueries.GetAsync(connection, model, entity, id, CancellationToken);
        Assert.NotNull(record);
        var when = process.Steps.OfType<DecisionStepModel>().SelectMany(step => step.Branches).Single().When;
        var result = await RecordExpressions.EvaluateAsync(connection, model, entity, record, when, DateTimeOffset.UtcNow, CancellationToken);
        Assert.True(result.Succeeded, result.Error?.Message);
        return result.Value;
    }

    private static async Task<Guid> CreateAsync(
        NpgsqlConnection connection, ApplicationModel model, string entityName, Guid[]? software, params (string Field, object? Value)[] values)
    {
        Assert.True(model.TryGetEntity(entityName, out var entity));
        var fields = values.Select(value =>
        {
            Assert.True(entity.TryGetField(value.Field, out var field));
            return new RecordValue(field, value.Value);
        });
        RecordLinks[] links = [];
        if (software is not null)
        {
            Assert.True(entity.TryGetField("software", out var set));
            links = [new RecordLinks(set, software)];
        }

        var result = await RecordCommands.CreateAsync(connection, model, entity, Guid.NewGuid(), [.. fields], [], links, cancellationToken: CancellationToken);
        Assert.Equal(RecordWriteOutcome.Written, result.Outcome);
        return result.Record!.Id;
    }

    /// <summary>
    /// Compiles and activates an application where a workstation holds a set of software and the
    /// process <c>Routing</c> over workstations has a decision on the set. Returns its model.
    /// </summary>
    private async Task<ApplicationModel> ActivateAsync()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""")
            .With("entities/software.json", $$"""
                { "id": "{{_softwareId}}", "kind": "entity", "name": "Software", "formatVersion": 1, "displayField": "name",
                  "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """)
            .With("entities/workstation.json", $$"""
                { "id": "{{_workstationId}}", "kind": "entity", "name": "Workstation", "formatVersion": 1, "displayField": "name",
                  "fields": [
                    { "name": "name", "type": "text", "required": true },
                    { "name": "software", "type": "references", "target": "Software" }
                  ] }
                """)
            .With("processes/routing.json", $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "process", "name": "Routing", "formatVersion": 1, "entity": "Workstation",
                  "start": "route",
                  "steps": [
                    { "name": "route", "type": "decision", "branches": [
                        { "when": "{{HasOffice}}", "next": "office" }
                      ], "otherwise": "other" },
                    { "name": "office", "type": "end" },
                    { "name": "other", "type": "end" }
                  ] }
                """);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, configuration, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        Assert.Empty((await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, CancellationToken)).Diagnostics);
        return compiled.Model!;
    }
}
