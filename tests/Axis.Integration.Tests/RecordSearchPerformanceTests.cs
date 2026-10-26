using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A record <c>search</c> over 100,000 rows is answered from the trigram index of each searched
/// column, so it does not read the whole table. The fixture's Article has a display field
/// <c>title</c> and a searchable <c>note</c>.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class RecordSearchPerformanceTests(DataDatabaseFixture fixture) : IClassFixture<DataDatabaseFixture>
{
    private const int ArticleCount = 100_000;
    private const int PageSize = 50;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_search_over_a_hundred_thousand_rows_finds_the_trigram_index_of_a_searchable_field()
    {
        var article = await SeedAsync();
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        var note = await NoteAsync(dataSource, 42);
        var search = new RecordSearch(note[5..15], RecordSearch.Searched(article, _ => true));
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken);

        var page = await RecordQueries.ListAsync(connection, null, article, 1, PageSize, null, search, null, CancellationToken);

        Assert.NotNull(page);
        Assert.True(page.TotalCount >= 1);
        Assert.Contains(page.Items, record => record.Values["note"]!.GetValue<string>() == note);

        var statement = RecordQueries.ListStatement(article, null, search, null);
        var plan = await ExplainAsync(dataSource, statement.CountSql, search.AddTo);
        var table = EntityNaming.Table(article.Id);
        Assert.Contains(EntityNaming.TrigramIndex(table, EntityNaming.Column("note")), plan, StringComparison.Ordinal);
        Assert.Contains(EntityNaming.TrigramIndex(table, EntityNaming.Column("title")), plan, StringComparison.Ordinal);
        Assert.Contains("Bitmap Index Scan", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("Seq Scan", plan, StringComparison.Ordinal);
    }

    /// <summary>The note of article <paramref name="n"/>: the 32 hexadecimal digits of the MD5 hash of <paramref name="n"/>, as the seed computes it.</summary>
    private static async Task<string> NoteAsync(NpgsqlDataSource dataSource, int n)
    {
        await using var command = dataSource.CreateCommand("SELECT md5(@n::text)");
        command.Parameters.AddWithValue("n", n);
        return (string)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    /// <summary>
    /// Activates the SearchPerformanceApp fixture, which creates the trigram index of each searched
    /// column on the empty table, then inserts the articles and returns the Article entity.
    /// </summary>
    private async Task<EntityModel> SeedAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var compiled = await ReleaseCompiler.CompileAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "SearchPerformanceApp"), configuration, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, CancellationToken);
        Assert.Empty(activated.Diagnostics);
        Assert.True(compiled.Model!.TryGetEntity("Article", out var article));

        var table = EntityNaming.QualifiedTable(EntityNaming.Table(article.Id));
        var id = EntityNaming.Quote(EntityNaming.IdColumn);
        var title = EntityNaming.Quote(EntityNaming.Column("title"));
        var note = EntityNaming.Quote(EntityNaming.Column("note"));
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        await using (var insert = dataSource.CreateCommand(
            $"""
            INSERT INTO {table} ({id}, {title}, {note})
            SELECT gen_random_uuid(), 'Article ' || n, md5(n::text)
            FROM generate_series(1, {ArticleCount}) AS n
            """))
        {
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        // Updates the planner's statistics, as autovacuum does in production.
        await using (var analyze = dataSource.CreateCommand($"ANALYZE {table}"))
        {
            await analyze.ExecuteNonQueryAsync(CancellationToken);
        }

        return article;
    }

    /// <summary>Plans <paramref name="sql"/> with its parameters, writes the plan to the test output and returns it.</summary>
    private static async Task<string> ExplainAsync(NpgsqlDataSource dataSource, string sql, Action<NpgsqlCommand> bind)
    {
        await using var command = dataSource.CreateCommand($"EXPLAIN (COSTS OFF) {sql}");
        bind(command);
        var lines = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(CancellationToken))
        {
            while (await reader.ReadAsync(CancellationToken))
            {
                lines.Add(reader.GetString(0));
            }
        }

        var plan = string.Join('\n', lines);
        TestContext.Current.TestOutputHelper?.WriteLine(plan);
        return plan;
    }
}
