using System.Diagnostics;
using Axis.Configuration.Model;
using Axis.Data.DataSources;
using Axis.Data.Records;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A record <c>search</c> and a data source <c>search</c> over 100,000 articles with two searched
/// fields, the display field <c>title</c> and the searchable <c>note</c>, answer a page within the
/// time limit. Each plan reads both trigram indexes and never the whole table.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class SearchPerformanceTests(DataDatabaseFixture fixture) : IClassFixture<DataDatabaseFixture>
{
    private const int ArticleCount = 100_000;
    private const int PageSize = 20;

    // Every 2,000th article holds the rare word in its title and every one between them holds it
    // in its note, so 100 articles match. A search term that matches that few keeps the planner
    // on the trigram indexes.
    private const int RareEvery = 2_000;
    private const int Matches = ArticleCount / RareEvery * 2;

    // The search is two bitmap scans of the trigram indexes and a page of the matches. Each
    // took about 2 ms locally. Far above that, so shared CI runners do not make the test flaky, and
    // it still catches the whole table read of the old search, which took seconds. The plan check, not
    // the time, is what catches a lost index.
    private static readonly TimeSpan _readTime = TimeSpan.FromSeconds(2);

    private static readonly SemaphoreSlim _setup = new(1, 1);
    private static ApplicationModel? _model;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The letter case differs from the planted word on purpose.
    private static string Search => SearchApp.RareWord[..4].ToUpperInvariant();

    [Fact]
    public async Task A_record_search_over_a_hundred_thousand_articles_answers_within_the_time_limit_using_both_trigram_indexes()
    {
        var model = await SeedAsync();
        Assert.True(model.TryGetEntity("Article", out var article));
        var search = new RecordSearch(Search, RecordSearch.Searched(article, _ => true));
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken);
        await RecordQueries.ListAsync(connection, null, article, 1, PageSize, null, search, null, CancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var page = await RecordQueries.ListAsync(connection, null, article, 1, PageSize, null, search, null, CancellationToken);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"The record search took {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _readTime, $"The record search took {stopwatch.Elapsed}.");
        Assert.NotNull(page);
        Assert.Equal(Matches, page.TotalCount);
        Assert.Equal(PageSize, page.Items.Count);

        var statement = RecordQueries.ListStatement(article, null, search, null);
        var indexes = SearchApp.TrigramIndexes(article);
        AssertUsesTrigramIndexes(indexes, await QueryPlans.ExplainAsync(dataSource, statement.CountSql, search.AddTo, CancellationToken));
        AssertUsesTrigramIndexes(indexes, await QueryPlans.ExplainAsync(
            dataSource, statement.PageSql, command => BindPage(search.AddTo, command), CancellationToken));
    }

    [Fact]
    public async Task A_data_source_search_over_a_hundred_thousand_articles_answers_within_the_time_limit_using_both_trigram_indexes()
    {
        var model = await SeedAsync();
        Assert.True(model.TryGetEntity("Article", out var article));
        Assert.True(model.TryGetDataSource("ArticleSearch", out var articles));
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken);
        await DataSourceQueries.ListAsync(connection, null, model, articles, 1, PageSize, null, [], search: Search, cancellationToken: CancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var page = await DataSourceQueries.ListAsync(connection, null, model, articles, 1, PageSize, null, [], search: Search, cancellationToken: CancellationToken);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"The data source search took {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _readTime, $"The data source search took {stopwatch.Elapsed}.");
        Assert.NotNull(page);
        Assert.Equal(Matches, page.TotalCount);
        Assert.Equal(PageSize, page.Items.Count);

        var statement = DataSourceQueries.Statement(model, articles, null, null, search: true);
        void Bind(NpgsqlCommand command)
        {
            DataSourceQueries.AddValues(command, statement.Values);
            command.Parameters.Add(RecordQueries.SearchParameter(Search));
        }

        var indexes = SearchApp.TrigramIndexes(article);
        AssertUsesTrigramIndexes(indexes, await QueryPlans.ExplainAsync(dataSource, statement.CountSql, Bind, CancellationToken));
        AssertUsesTrigramIndexes(indexes, await QueryPlans.ExplainAsync(
            dataSource, statement.PageSql, command => BindPage(Bind, command), CancellationToken));
    }

    /// <summary>Activates the SearchPerformanceApp fixture and inserts the articles, once for every test of the class.</summary>
    private async Task<ApplicationModel> SeedAsync()
    {
        await _setup.WaitAsync(CancellationToken);
        try
        {
            return _model ??= await SearchApp.SeedAsync(fixture.ConnectionString, ArticleCount, RareEvery, CancellationToken);
        }
        finally
        {
            _setup.Release();
        }
    }

    /// <summary>Adds the parameters of the first page to a page query.</summary>
    private static void BindPage(Action<NpgsqlCommand> bind, NpgsqlCommand command)
    {
        bind(command);
        command.Parameters.AddWithValue("limit", PageSize);
        command.Parameters.AddWithValue("offset", 0L);
    }

    /// <summary>The plan scans both trigram indexes, as bitmap scans, and reads no whole table.</summary>
    private static void AssertUsesTrigramIndexes(string[] indexes, string plan)
    {
        foreach (var index in indexes)
        {
            Assert.Contains($"Bitmap Index Scan on {index}", plan, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Seq Scan", plan, StringComparison.Ordinal);
    }
}
