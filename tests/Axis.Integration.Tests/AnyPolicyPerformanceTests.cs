using System.Diagnostics;
using System.Text.RegularExpressions;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A record list under a policy filter with <c>any</c> over a child collection stays fast at a
/// realistic size: one read answers within the time limit, and the page's plan finds each
/// record's child rows through the child table's owner index.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class AnyPolicyPerformanceTests(DataDatabaseFixture fixture) : IClassFixture<DataDatabaseFixture>
{
    private const int TicketCount = 100_000;
    private const int AssigneesPerTicket = 5;
    private const int PageSize = 50;

    // Assignee row j of ticket p names user-((5 * p + j) % 100). So user-7 is on ticket p exactly
    // when p % 20 == 1: 1 ticket in 20, spread evenly. The page then stops after about a thousand
    // tickets, and one owner index lookup per ticket is the cheapest plan. A user on only a few
    // tickets would rightly make PostgreSQL read the whole child table instead.
    private const string User = "user-7";
    private const int UserCount = 100;

    // One read is the count and the page. The count is one pass over the 500,000 child rows with a
    // hash join, and the page stops early. The list took about 30 ms locally. Well above that, so
    // shared CI runners do not make the test flaky, and it still catches a slowdown of ten times or
    // more. The plan check, not the time, is what catches a lost owner index.
    private static readonly TimeSpan _readTime = TimeSpan.FromSeconds(2);

    private static readonly SemaphoreSlim _setup = new(1, 1);
    private static ApplicationModel? _model;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_list_over_a_hundred_thousand_records_with_an_any_filter_answers_within_the_time_limit_reading_child_rows_by_the_owner_index()
    {
        var model = await SeedAsync();
        Assert.True(model.TryGetEntity("Ticket", out var ticket));
        Assert.True(model.TryGetEntity("Assignee", out var assignee));
        var condition = AssigneeCondition(model, ticket);
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken);
        await RecordQueries.ListAsync(connection, null, ticket, 1, PageSize, null, null, condition, CancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var page = await RecordQueries.ListAsync(connection, null, ticket, 1, PageSize, null, null, condition, CancellationToken);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"The record list took {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _readTime, $"The record list took {stopwatch.Elapsed}.");
        Assert.NotNull(page);
        var assigned = Enumerable.Range(0, TicketCount).Count(IsAssigned);
        Assert.Equal(assigned, page.TotalCount);
        Assert.Equal(PageSize, page.Items.Count);

        var statement = RecordQueries.ListStatement(ticket, null, null, condition);

        // The count reads the whole child table once with a hash join, which is the right plan for
        // a pass over every ticket, so only the page's plan is checked.
        await ExplainAsync(dataSource, statement.CountSql, condition.AddTo);
        AssertPolicyReadsChildRowsByOwnerIndex(
            assignee, await ExplainAsync(dataSource, statement.PageSql, command => BindPage(condition.AddTo, command)));
    }

    /// <summary>Whether <see cref="User"/> is one of the assignees of ticket <paramref name="p"/>, by the seed's formula.</summary>
    private static bool IsAssigned(int p) =>
        Enumerable.Range(0, AssigneesPerTicket).Any(j => $"user-{(AssigneesPerTicket * p + j) % UserCount}" == User);

    /// <summary>The read filters of the assignee role as one condition for <see cref="User"/>, as the endpoints build it.</summary>
    private static RecordCondition AssigneeCondition(ApplicationModel model, EntityModel ticket)
    {
        var read = PolicyEvaluator.Access(model, ["assignee"], ticket, PolicyAction.Read);
        Assert.True(read.Granted);
        Assert.NotNull(read.Filters);
        return RecordFilters.Condition(model, ticket, [.. read.Filters.Select(filter => filter.Expression)], User);
    }

    /// <summary>
    /// Activates the AnyPolicyPerformanceApp fixture and inserts the tickets and their assignees,
    /// once for every test of the class, and returns its model.
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
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "AnyPolicyPerformanceApp"), configuration, CancellationToken);
            Assert.Empty(compiled.Diagnostics);
            var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, CancellationToken);
            Assert.Empty(activated.Diagnostics);
            var model = compiled.Model!;
            Assert.True(model.TryGetEntity("Ticket", out var ticket));
            Assert.True(model.TryGetEntity("Assignee", out var assignee));

            // Ticket p, from 0, has the id tickets[p], so its assignee rows can name it as owner.
            var tickets = Enumerable.Range(0, TicketCount).Select(_ => Guid.NewGuid()).ToArray();
            var id = EntityNaming.Quote(EntityNaming.IdColumn);
            await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
            await using (var insert = dataSource.CreateCommand(
                $"""
                INSERT INTO {Table(ticket)} ({id}, {Column("title")})
                SELECT (@tickets::uuid[])[p + 1], 'Ticket ' || p
                FROM generate_series(0, {TicketCount - 1}) AS p
                """))
            {
                insert.Parameters.AddWithValue("tickets", tickets);
                await insert.ExecuteNonQueryAsync(CancellationToken);
            }

            await using (var insert = dataSource.CreateCommand(
                $"""
                INSERT INTO {Table(assignee)} ({id}, {EntityNaming.Quote(EntityNaming.OwnerColumn)}, {EntityNaming.Quote(EntityNaming.PositionColumn)}, {Column("userId")})
                SELECT gen_random_uuid(), (@tickets::uuid[])[p + 1], j, 'user-' || (({AssigneesPerTicket} * p + j) % {UserCount})
                FROM generate_series(0, {TicketCount - 1}) AS p, generate_series(0, {AssigneesPerTicket - 1}) AS j
                """))
            {
                insert.Parameters.AddWithValue("tickets", tickets);
                await insert.ExecuteNonQueryAsync(CancellationToken);
            }

            // Updates the planner's statistics, as autovacuum does in production.
            await using (var analyze = dataSource.CreateCommand($"ANALYZE {Table(ticket)}, {Table(assignee)}"))
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
    /// The policy filter's <c>any</c> step, the subquery aliased <c>a0</c>, finds each ticket's
    /// assignee rows through the child table's owner index and never reads the whole child table.
    /// </summary>
    private static void AssertPolicyReadsChildRowsByOwnerIndex(EntityModel assignee, string plan)
    {
        var table = Regex.Escape(EntityNaming.Table(assignee.Id));
        var index = Regex.Escape(EntityNaming.Index(EntityNaming.Table(assignee.Id), [EntityNaming.OwnerColumn]));
        Assert.Matches($@"(Index Scan|Index Only Scan)( Backward)? using {index} on {table} a0\b|Bitmap Index Scan on {index}\b", plan);
        Assert.DoesNotMatch($@"Seq Scan on {table} a\d+\b", plan);
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
