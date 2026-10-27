using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using static Axis.Integration.Tests.PerformanceServerFixture;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A CSV file of 10,000 rows imports into an entity with a reference column, a sequence and a validation, and
/// its dry run and its commit each finish within a time limit. The commit writes all rows in one transaction, so
/// they share one <c>xmin</c>, the id PostgreSQL gives the transaction that wrote a row. The class runs alone, so
/// no other test competes for CPU with the timed steps.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class ImportPerformanceTests(ImportPerformanceTests.ImportFixture fixture) : IClassFixture<ImportPerformanceTests.ImportFixture>
{
    private const int RowCount = 10_000;
    private const int WarmUpRowCount = 20;
    private const int SupplierCount = 100;
    private const string Items = "/api/apps/ImportPerformanceApp/entities/Item";

    // The dry run of 10,000 rows took about 0.5 s locally and the commit about 19 s, because the commit creates the
    // rows one at a time through the record command, with its sequence number, validation and display value lookup.
    // Each limit is about five times the local time, so shared CI runners do not make the test flaky, and it still
    // catches a slowdown of five times or more. Each step runs once, as a repeated commit costs too much, and a run
    // of seconds absorbs the short pauses of a runner.
    private static readonly TimeSpan _checkTime = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan _commitTime = TimeSpan.FromSeconds(96);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_import_of_ten_thousand_rows_with_a_reference_a_sequence_and_a_validation_checks_and_commits_within_the_time_limits_in_one_transaction()
    {
        var models = new ReleaseModelCache(PackageCatalog.Empty);
        var check = new RecordImportCheckHandler(models, new RecordImportSettings(RowCount), NullLoggerFactory.Instance, NullLogger<RecordImportCheckHandler>.Instance);
        var commit = new RecordImportCommitHandler(models, NullLoggerFactory.Instance, NullLogger<RecordImportCommitHandler>.Instance);

        // The first import compiles the model and warms the connections, so the timed steps measure the work.
        var warmUp = await UploadAsync(File("W", WarmUpRowCount));
        await RunAsync(RecordImports.CheckWorkItemKind, check);
        await CommitAsync(warmUp);
        await RunAsync(RecordImports.CommitWorkItemKind, commit);
        Assert.Equal("committed", await StateAsync(warmUp));

        var upload = await UploadAsync(File("C", RowCount));
        var checkElapsed = await RunAsync(RecordImports.CheckWorkItemKind, check);
        TestContext.Current.TestOutputHelper?.WriteLine($"The dry run of {RowCount} rows took {checkElapsed}.");
        Assert.Equal("checked", await StateAsync(upload));
        Assert.True(checkElapsed < _checkTime, $"The dry run took {checkElapsed}.");

        await CommitAsync(upload);
        var commitElapsed = await RunAsync(RecordImports.CommitWorkItemKind, commit);
        TestContext.Current.TestOutputHelper?.WriteLine($"The commit of {RowCount} rows took {commitElapsed}.");
        Assert.Equal("committed", await StateAsync(upload));
        Assert.True(commitElapsed < _commitTime, $"The commit took {commitElapsed}.");

        // Every row has its own sequence number and a reference, and all of them were written by one transaction.
        // The import row and the audit records are left out, as the rows are written in a savepoint of the commit.
        var rows = await RowsAsync();
        Assert.Equal((RowCount, RowCount, RowCount, 1L), rows);

        Assert.Equal(
            await fixture.ScalarAsync($"SELECT {EntityNaming.Quote(EntityNaming.IdColumn)} FROM {Table("Supplier")} WHERE {Column("name")} = 'Supplier 1'"),
            await fixture.ScalarAsync($"SELECT {Column("supplier")} FROM {Table("Item")} WHERE {Column("name")} = 'C-1'"));
    }

    /// <summary>Row n of a file is named <c>prefix-n</c>, has quantity 1 + n % 50 and refers to Supplier n % 100 by its display value.</summary>
    private static string File(string prefix, int rows)
    {
        var csv = new StringBuilder("name,quantity,supplier\n");
        for (var n = 1; n <= rows; n++)
        {
            csv.Append(prefix).Append('-').Append(n).Append(',').Append(1 + n % 50).Append(",Supplier ").Append(n % SupplierCount).Append('\n');
        }

        return csv.ToString();
    }

    private async Task<Guid> UploadAsync(string csv)
    {
        using var request = Request(HttpMethod.Post, $"{Items}/imports", Host, csv, "text/csv");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private async Task CommitAsync(Guid id)
    {
        using var request = Request(HttpMethod.Post, $"{Items}/imports/{id:D}/commit", Host, null, null);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task<object?> StateAsync(Guid id) =>
        await fixture.ScalarAsync($"SELECT state FROM axis.record_imports WHERE id = '{id:D}'");

    /// <summary>Claims and runs the one work item of <paramref name="kind"/> the tenant has queued, and returns how long it took.</summary>
    private async Task<TimeSpan> RunAsync(string kind, IExternalWorkItemHandler handler)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        var ran = 0;
        var stopwatch = Stopwatch.StartNew();
        while (await WorkItemQueue.ClaimAsync(connection, TenantA, [kind], TimeSpan.FromMinutes(5), CancellationToken) is { } item)
        {
            Assert.Equal(WorkItemOutcome.Completed, await handler.ExecuteAsync(item, connection, CancellationToken));
            ran++;
        }

        stopwatch.Stop();
        Assert.Equal(1, ran);
        return stopwatch.Elapsed;
    }

    private async Task<(long Rows, long Numbers, long References, long Transactions)> RowsAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT count(*), count(DISTINCT {Column("number")}), count({Column("supplier")}), count(DISTINCT xmin::text)
            FROM {Table("Item")} WHERE {Column("name")} LIKE 'C-%'
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }

    private static string Column(string field) => EntityNaming.Quote(EntityNaming.Column(field));

    /// <summary>The quoted, schema-qualified table of the fixture entity <paramref name="entity"/>.</summary>
    private string Table(string entity)
    {
        Assert.True(fixture.Model.TryGetEntity(entity, out var model));
        return EntityNaming.QualifiedTable(EntityNaming.Table(model.Id));
    }

    /// <summary>
    /// The PerformanceServerFixture with the ImportPerformanceApp fixture and 100 suppliers named Supplier 0 to
    /// Supplier 99.
    /// </summary>
    public sealed class ImportFixture : PerformanceServerFixture
    {
        public ImportFixture()
            : base("ImportPerformanceApp", "importer")
        {
        }

        protected override async Task SeedAsync(NpgsqlDataSource dataSource, ApplicationModel model)
        {
            Assert.True(model.TryGetEntity("Supplier", out var supplier));
            var id = EntityNaming.Quote(EntityNaming.IdColumn);
            await using (var insert = dataSource.CreateCommand(
                $"""
                INSERT INTO {Table(supplier)} ({id}, {Column("name")})
                SELECT gen_random_uuid(), 'Supplier ' || k FROM generate_series(0, {SupplierCount - 1}) AS k
                """))
            {
                await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            // Updates the planner's statistics, as autovacuum does in production.
            await using (var analyze = dataSource.CreateCommand($"ANALYZE {Table(supplier)}"))
            {
                await analyze.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
        }

        public async Task<object?> ScalarAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }
    }
}
