using System.Diagnostics;
using System.Text.RegularExpressions;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.DataSources;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A record list and an aggregate data source under a policy filter with a reference path and
/// <c>currentUser()</c> stay fast at a realistic size: each read answers within the time limit,
/// and its plan finds each department through the primary key index.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class PolicyQueryPerformanceTests(DataDatabaseFixture fixture) : IClassFixture<DataDatabaseFixture>
{
    private const int RequestCount = 100_000;

    // On a tiny Department table a full read is the right plan, so the index check would prove
    // nothing. At a thousand rows the primary key lookup is the right plan.
    private const int DepartmentCount = 1_000;
    private const int PageSize = 50;

    // Department 7 is managed by manager-7, and request n belongs to department n % 1000, so this
    // manager may read 100 requests.
    private const int ManagedDepartment = 7;
    private const string Manager = "manager-7";

    // One read is the count and the page: each is one pass over the 100,000 requests with one
    // primary key lookup of a department per request. The list took about 165 ms and the data
    // source about 140 ms locally. Well above that, so shared CI runners do not make the test
    // flaky, and it still catches a slowdown of ten times or more. The plan check, not the time,
    // is what catches a lost index.
    private static readonly TimeSpan _readTime = TimeSpan.FromSeconds(2);

    private static readonly SemaphoreSlim _setup = new(1, 1);
    private static ApplicationModel? _model;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_managers_list_over_a_hundred_thousand_requests_answers_within_the_time_limit_reading_departments_by_their_primary_key()
    {
        var model = await SeedAsync();
        Assert.True(model.TryGetEntity("Request", out var request));
        var condition = ManagerCondition(model, request);
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken);
        await RecordQueries.ListAsync(connection, null, request, 1, PageSize, null, null, condition, CancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var page = await RecordQueries.ListAsync(connection, null, request, 1, PageSize, null, null, condition, CancellationToken);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"The record list took {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _readTime, $"The record list took {stopwatch.Elapsed}.");
        Assert.NotNull(page);
        Assert.Equal(RequestCount / DepartmentCount, page.TotalCount);
        Assert.Equal(PageSize, page.Items.Count);

        var statement = RecordQueries.ListStatement(request, null, false, condition);
        AssertPolicyReadsDepartmentsByPrimaryKey(model, await ExplainAsync(dataSource, statement.CountSql, condition.AddTo));
        AssertPolicyReadsDepartmentsByPrimaryKey(
            model, await ExplainAsync(dataSource, statement.PageSql, command => BindPage(condition.AddTo, command)));
    }

    [Fact]
    public async Task A_managers_aggregate_data_source_over_a_hundred_thousand_requests_answers_within_the_time_limit_reading_departments_by_their_primary_key()
    {
        var model = await SeedAsync();
        Assert.True(model.TryGetEntity("Request", out var request));
        Assert.True(model.TryGetDataSource("RequestTotals", out var totals));
        var condition = ManagerCondition(model, request);
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken);
        await DataSourceQueries.ListAsync(connection, null, model, totals, 1, PageSize, null, [], condition, CancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var page = await DataSourceQueries.ListAsync(connection, null, model, totals, 1, PageSize, null, [], condition, CancellationToken);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"The data source took {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _readTime, $"The data source took {stopwatch.Elapsed}.");
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        var row = Assert.Single(page.Items);

        // The manager's requests that pass the data source filter amount > 0.
        var amounts = Enumerable.Range(1, RequestCount)
            .Where(n => n % DepartmentCount == ManagedDepartment)
            .Select(Amount)
            .Where(amount => amount > 0)
            .ToList();
        Assert.Equal(amounts.Count, row.Values["requests"]!.GetValue<long>());
        Assert.Equal(amounts.Sum(), row.Values["amountSum"]!.GetValue<long>());

        var statement = DataSourceQueries.Statement(model, totals, null, condition);
        void Bind(NpgsqlCommand command)
        {
            DataSourceQueries.AddValues(command, statement.Values);
            condition.AddTo(command);
        }

        AssertPolicyReadsDepartmentsByPrimaryKey(model, await ExplainAsync(dataSource, statement.PageSql, command => BindPage(Bind, command)));

        // Without a group field the data source is always one total row, so PostgreSQL counts it
        // without reading any table.
        Assert.DoesNotContain("Scan", await ExplainAsync(dataSource, statement.CountSql, Bind), StringComparison.Ordinal);
    }

    /// <summary>The amount of request <paramref name="n"/>, so 1 in 100 requests has amount 0.</summary>
    private static long Amount(int n) => n % 100;

    /// <summary>The read filters of the manager role as one condition for <see cref="Manager"/>, as the endpoints build it.</summary>
    private static RecordCondition ManagerCondition(ApplicationModel model, EntityModel request)
    {
        var read = PolicyEvaluator.Access(model, ["manager"], request, PolicyAction.Read);
        Assert.True(read.Granted);
        Assert.NotNull(read.Filters);
        return RecordFilters.Condition(model, request, [.. read.Filters.Select(filter => filter.Expression)], Manager);
    }

    /// <summary>
    /// Activates the PolicyPerformanceApp fixture and inserts the departments and requests, once
    /// for every test of the class, and returns its model.
    /// </summary>
    private async Task<ApplicationModel> SeedAsync()
    {
        await _setup.WaitAsync(CancellationToken);
        try
        {
            if (_model is not null)
            {
                return _model;
            }

            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(CancellationToken);
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            var compiled = await ReleaseCompiler.CompileAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "PolicyPerformanceApp"), configuration, CancellationToken);
            Assert.Empty(compiled.Diagnostics);
            var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, CancellationToken);
            Assert.Empty(activated.Diagnostics);
            var model = compiled.Model!;
            Assert.True(model.TryGetEntity("Department", out var department));
            Assert.True(model.TryGetEntity("Request", out var request));

            // Department k, from 0, has the id departments[k] and the manager manager-k. Request n
            // belongs to department n % 1000.
            var departments = Enumerable.Range(0, DepartmentCount).Select(_ => Guid.NewGuid()).ToArray();
            var id = EntityNaming.Quote(EntityNaming.IdColumn);
            await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
            await using (var insert = dataSource.CreateCommand(
                $"""
                INSERT INTO {Table(department)} ({id}, {Column("name")}, {Column("manager")})
                SELECT (@departments::uuid[])[k + 1], 'Dept ' || k, 'manager-' || k
                FROM generate_series(0, {DepartmentCount - 1}) AS k
                """))
            {
                insert.Parameters.AddWithValue("departments", departments);
                await insert.ExecuteNonQueryAsync(CancellationToken);
            }

            await using (var insert = dataSource.CreateCommand(
                $"""
                INSERT INTO {Table(request)} ({id}, {Column("title")}, {Column("amount")}, {Column("department")})
                SELECT gen_random_uuid(), 'Request ' || n, n % 100, (@departments::uuid[])[1 + n % {DepartmentCount}]
                FROM generate_series(1, {RequestCount}) AS n
                """))
            {
                insert.Parameters.AddWithValue("departments", departments);
                await insert.ExecuteNonQueryAsync(CancellationToken);
            }

            // Updates the planner's statistics, as autovacuum does in production.
            await using (var analyze = dataSource.CreateCommand($"ANALYZE {Table(department)}, {Table(request)}"))
            {
                await analyze.ExecuteNonQueryAsync(CancellationToken);
            }

            return _model = model;
        }
        finally
        {
            _setup.Release();
        }
    }

    private static string Table(EntityModel entity) => EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id));

    private static string Column(string field) => EntityNaming.Quote(EntityNaming.Column(field));

    /// <summary>Adds the parameters of the first page to a page query.</summary>
    private static void BindPage(Action<NpgsqlCommand> bind, NpgsqlCommand command)
    {
        bind(command);
        command.Parameters.AddWithValue("limit", PageSize);
        command.Parameters.AddWithValue("offset", 0L);
    }

    /// <summary>
    /// The policy filter's reference step, the subquery aliased <c>s0</c>, finds each department
    /// through the primary key index and never reads the whole Department table. The plan may read
    /// the whole Request table: no field has an index yet, so the filter is checked on every
    /// request. A page's label join, aliased <c>l0</c>, is not checked: for 50 rows PostgreSQL may
    /// rightly read the small Department table once instead of looking up each label.
    /// </summary>
    private static void AssertPolicyReadsDepartmentsByPrimaryKey(ApplicationModel model, string plan)
    {
        Assert.True(model.TryGetEntity("Department", out var department));
        var table = Regex.Escape(EntityNaming.Table(department.Id));
        var index = Regex.Escape(EntityNaming.PrimaryKey(EntityNaming.Table(department.Id)));
        Assert.Matches($@"(Index Scan|Index Only Scan)( Backward)? using {index} on {table} s0\b|Bitmap Index Scan on {index}\b", plan);
        Assert.DoesNotMatch($@"Seq Scan on {table} s\d+\b", plan);
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
