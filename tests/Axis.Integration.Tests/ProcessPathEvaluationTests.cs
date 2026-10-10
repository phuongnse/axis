using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Records;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class ProcessPathEvaluationTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    // Each test uses its own application and entity ids and names, so tests sharing the database do
    // not see each other's tables and records.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _departmentId = Guid.NewGuid();
    private readonly Guid _requestId = Guid.NewGuid();

    private const string ManagerStartsWithB = "startsWith(department.manager.name, 'B')";
    private const string SalesWithManager = "startsWith(department.name, 'S') and department.manager is not null";
    private const string Sales = "startsWith(department.name, 'S')";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_path_reads_the_stored_related_records_inside_the_open_transaction_and_a_null_reference_gives_null()
    {
        var model = await ActivateAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        var binh = await CreateAsync(connection, model, "Employee", ("name", "Binh"));
        var sales = await CreateAsync(connection, model, "Department", ("name", "Sales"), ("manager", binh));
        var routed = await CreateAsync(connection, model, "PurchaseRequest", ("title", "Laptops"), ("department", sales));
        var unrouted = await CreateAsync(connection, model, "PurchaseRequest", ("title", "Desks"), ("department", null));

        // Nothing is committed yet, so only the caller's transaction sees these records.
        Assert.Equal(true, await EvaluateAsync(connection, model, routed, ManagerStartsWithB));
        Assert.Equal(true, await EvaluateAsync(connection, model, routed, SalesWithManager));
        Assert.Null(await EvaluateAsync(connection, model, unrouted, ManagerStartsWithB));
        Assert.Null(await EvaluateAsync(connection, model, unrouted, Sales));
        Assert.Equal(false, await EvaluateAsync(connection, model, unrouted, SalesWithManager));
    }

    [Fact]
    public async Task Two_paths_through_the_same_reference_read_that_record_once()
    {
        var model = await ActivateAsync();
        var log = new LogCollector();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(log).SetMinimumLevel(LogLevel.Trace));
        await using var npgsql = new NpgsqlDataSourceBuilder(database.ConnectionString).UseLoggerFactory(loggerFactory).Build();
        await using var connection = await npgsql.OpenConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        var binh = await CreateAsync(connection, model, "Employee", ("name", "Binh"));
        var sales = await CreateAsync(connection, model, "Department", ("name", "Sales"), ("manager", binh));
        var request = await CreateAsync(connection, model, "PurchaseRequest", ("title", "Laptops"), ("department", sales));
        Assert.True(model.TryGetEntity("PurchaseRequest", out var entity));
        var record = await RecordQueries.GetAsync(connection, model, entity, request, CancellationToken);
        Assert.NotNull(record);
        var before = Commands(log);

        var result = await RecordExpressions.EvaluateAsync(connection, model, entity, record, When(model, SalesWithManager), CancellationToken);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(true, result.Value);
        // Npgsql logs one entry per executed command. Matching on the message alone keeps the test
        // independent of the log category.
        var commands = Commands(log) - before;
        Assert.True(commands == 1, $"Expected 1 command, found {commands}:\n{string.Join('\n', log.Entries)}");
    }

    private static int Commands(LogCollector log) =>
        log.Entries.Count(entry => entry.Contains("Command execution completed", StringComparison.Ordinal));

    /// <summary>Evaluates the decision condition <paramref name="condition"/>, or the start condition, against the stored request <paramref name="id"/>.</summary>
    private static async Task<object?> EvaluateAsync(NpgsqlConnection connection, ApplicationModel model, Guid id, string condition)
    {
        Assert.True(model.TryGetEntity("PurchaseRequest", out var entity));
        var record = await RecordQueries.GetAsync(connection, model, entity, id, CancellationToken);
        Assert.NotNull(record);
        var result = await RecordExpressions.EvaluateAsync(connection, model, entity, record, When(model, condition), CancellationToken);
        Assert.True(result.Succeeded, result.Error?.Message);
        return result.Value;
    }

    /// <summary>The compiled start condition or decision condition of the process with the text <paramref name="condition"/>.</summary>
    private static ExpressionModel When(ApplicationModel model, string condition)
    {
        Assert.True(model.TryGetProcess("Routing", out var process));
        return new[] { process.StartCondition!.Expression }
            .Concat(process.Steps.OfType<DecisionStepModel>().SelectMany(step => step.Branches).Select(branch => branch.When))
            .Single(expression => expression.Expression == condition);
    }

    private static async Task<Guid> CreateAsync(
        NpgsqlConnection connection, ApplicationModel model, string entityName, params (string Field, object? Value)[] values)
    {
        Assert.True(model.TryGetEntity(entityName, out var entity));
        var fields = values.Select(value =>
        {
            Assert.True(entity.TryGetField(value.Field, out var field));
            return new RecordValue(field, value.Value);
        });
        var result = await RecordCommands.CreateAsync(connection, model, entity, [.. fields], [], cancellationToken: CancellationToken);
        Assert.Equal(RecordWriteOutcome.Written, result.Outcome);
        return result.Record!.Id;
    }

    /// <summary>
    /// Compiles and activates an application where a purchase request references a department and a
    /// department references its manager, an employee, with the process <c>Routing</c> over
    /// purchase requests. Returns its model.
    /// </summary>
    private async Task<ApplicationModel> ActivateAsync()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""")
            .With("texts/en.json", $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "routing.cannotStart": "This request cannot be routed." } }
                """)
            .With("entities/employee.json", $$"""
                { "id": "{{_employeeId}}", "kind": "entity", "name": "Employee", "formatVersion": 1, "displayField": "name",
                  "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """)
            .With("entities/department.json", $$"""
                { "id": "{{_departmentId}}", "kind": "entity", "name": "Department", "formatVersion": 1, "displayField": "name",
                  "fields": [
                    { "name": "name", "type": "text", "required": true },
                    { "name": "manager", "type": "reference", "target": "Employee" }
                  ] }
                """)
            .With("entities/purchase-request.json", $$"""
                { "id": "{{_requestId}}", "kind": "entity", "name": "PurchaseRequest", "formatVersion": 1, "displayField": "title",
                  "fields": [
                    { "name": "title", "type": "text", "required": true },
                    { "name": "department", "type": "reference", "target": "Department" }
                  ] }
                """)
            .With("processes/routing.json", $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "process", "name": "Routing", "formatVersion": 1, "entity": "PurchaseRequest",
                  "startCondition": { "expression": "{{ManagerStartsWithB}}", "message": { "textKey": "routing.cannotStart" } },
                  "start": "route",
                  "steps": [
                    { "name": "route", "type": "decision", "branches": [
                        { "when": "{{SalesWithManager}}", "next": "managed" },
                        { "when": "{{Sales}}", "next": "unmanaged" }
                      ], "otherwise": "other" },
                    { "name": "managed", "type": "end" },
                    { "name": "unmanaged", "type": "end" },
                    { "name": "other", "type": "end" }
                  ] }
                """);

        await using var configuration = database.CreateConfigurationContext();
        await using var data = database.CreateContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, configuration, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        Assert.Empty((await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, CancellationToken)).Diagnostics);
        return compiled.Model!;
    }
}
