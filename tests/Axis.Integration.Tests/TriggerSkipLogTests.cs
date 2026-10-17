using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The log of a data change trigger that skips a start names the kind of the run-time error, never its
/// message or a value of the record.
/// </summary>
public sealed class TriggerSkipLogTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_when_that_fails_at_run_time_logs_the_skip_with_the_error_kind_and_no_record_value()
    {
        var name = $"App{Guid.NewGuid():N}";
        using var folder = new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "application", "name": "{{name}}", "formatVersion": 1 }""")
            .With("entities/invoice.json", $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "entity", "name": "Invoice", "formatVersion": 1, "displayField": "title",
                  "fields": [
                    { "name": "title", "type": "text", "required": true },
                    { "name": "amount", "type": "decimal", "precision": 20, "scale": 2 },
                    { "name": "divisor", "type": "integer" }
                  ] }
                """)
            .With("processes/review.json", $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "process", "name": "Review", "formatVersion": 1, "entity": "Invoice",
                  "start": "done",
                  "steps": [ { "name": "done", "type": "end" } ] }
                """)
            .With("triggers/review-invoices.json", $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "trigger", "name": "ReviewInvoices", "formatVersion": 1, "type": "dataChange",
                  "entity": "Invoice", "on": ["create"], "when": "amount / divisor > 1", "process": "Review" }
                """);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, configuration, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        var activated = await ReleaseActivator.ActivateAsync(
            compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, CancellationToken);
        Assert.Empty(activated.Diagnostics);
        var model = compiled.Model!;
        Assert.True(model.TryGetEntity("Invoice", out var entity));

        var logs = new LogCollector();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        Assert.True(entity.TryGetField("title", out var title));
        Assert.True(entity.TryGetField("amount", out var amount));
        Assert.True(entity.TryGetField("divisor", out var divisor));
        var created = await RecordCommands.CreateAsync(
            connection,
            model,
            entity,
            Guid.NewGuid(),
            [new RecordValue(title, "Laptops"), new RecordValue(amount, "7341.29"), new RecordValue(divisor, 0L)],
            [],
            cancellationToken: CancellationToken);
        Assert.Equal(RecordWriteOutcome.Written, created.Outcome);

        await DataChangeTriggerStarts.StartAsync(
            transaction,
            "t",
            model,
            compiled.Release!.Id,
            entity,
            DataChangeKinds.Create,
            created.Record!,
            null,
            loggerFactory.CreateLogger("test"),
            CancellationToken);

        var skip = Assert.Single(logs.Entries, entry => entry.Contains("skipped the start of process Review", StringComparison.Ordinal));
        Assert.Contains("the when failed: DivisionByZero", skip, StringComparison.Ordinal);
        Assert.DoesNotContain("Division by zero", skip, StringComparison.Ordinal);
        Assert.DoesNotContain("7341", skip, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("7341", StringComparison.Ordinal));
    }
}
