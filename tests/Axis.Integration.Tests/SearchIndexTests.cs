using Axis.Configuration.Model;
using Axis.Data.DataSources;
using Axis.Data.Records;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A record <c>search</c> and a data source <c>search</c> can use the trigram index of each searched
/// field. On a few hundred rows PostgreSQL rightly reads the whole table, so the test turns off the
/// sequential, plain index and index-only scans. A bitmap scan of the trigram indexes is then the
/// only way left to use an index, and each plan must name both of them. The time limit over 100,000
/// rows is in <see cref="SearchPerformanceTests"/>.
/// </summary>
public sealed class SearchIndexTests(DataDatabaseFixture fixture) : IClassFixture<DataDatabaseFixture>
{
    private const int ArticleCount = 500;
    private const int PageSize = 20;

    private static readonly SemaphoreSlim _setup = new(1, 1);
    private static ApplicationModel? _model;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_record_search_can_use_the_trigram_index_of_each_searched_field()
    {
        var model = await SeedAsync();
        Assert.True(model.TryGetEntity("Article", out var article));
        var search = new RecordSearch(SearchApp.RareWord, RecordSearch.Searched(article, _ => true));
        var statement = RecordQueries.ListStatement(article, null, search, null);
        var indexes = SearchApp.TrigramIndexes(article);

        AssertUsesTrigramIndexes(indexes, await ExplainAsync(statement.CountSql, search.AddTo));
        AssertUsesTrigramIndexes(indexes, await ExplainAsync(statement.PageSql, command => BindPage(search.AddTo, command)));
    }

    [Fact]
    public async Task A_data_source_search_can_use_the_trigram_index_of_each_searched_field()
    {
        var model = await SeedAsync();
        Assert.True(model.TryGetEntity("Article", out var article));
        Assert.True(model.TryGetDataSource("ArticleSearch", out var articles));
        var statement = DataSourceQueries.Statement(model, articles, null, null, search: true);
        void Bind(NpgsqlCommand command)
        {
            DataSourceQueries.AddValues(command, statement.Values);
            command.Parameters.Add(RecordQueries.SearchParameter(SearchApp.RareWord));
        }

        var indexes = SearchApp.TrigramIndexes(article);

        AssertUsesTrigramIndexes(indexes, await ExplainAsync(statement.CountSql, Bind));
        AssertUsesTrigramIndexes(indexes, await ExplainAsync(statement.PageSql, command => BindPage(Bind, command)));
    }

    private async Task<ApplicationModel> SeedAsync()
    {
        await _setup.WaitAsync(CancellationToken);
        try
        {
            return _model ??= await SearchApp.SeedAsync(fixture.ConnectionString, ArticleCount, 50, CancellationToken);
        }
        finally
        {
            _setup.Release();
        }
    }

    /// <summary>Plans <paramref name="sql"/> on a connection that has turned off every scan but the bitmap scan.</summary>
    private async Task<string> ExplainAsync(string sql, Action<NpgsqlCommand> bind)
    {
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken);
        await using (var settings = new NpgsqlCommand("SET enable_seqscan = off; SET enable_indexscan = off; SET enable_indexonlyscan = off", connection))
        {
            await settings.ExecuteNonQueryAsync(CancellationToken);
        }

        await using var command = new NpgsqlCommand($"EXPLAIN (COSTS OFF) {sql}", connection);
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

    private static void BindPage(Action<NpgsqlCommand> bind, NpgsqlCommand command)
    {
        bind(command);
        command.Parameters.AddWithValue("limit", PageSize);
        command.Parameters.AddWithValue("offset", 0L);
    }

    private static void AssertUsesTrigramIndexes(string[] indexes, string plan)
    {
        foreach (var index in indexes)
        {
            Assert.Contains($"Bitmap Index Scan on {index}", plan, StringComparison.Ordinal);
        }
    }
}
