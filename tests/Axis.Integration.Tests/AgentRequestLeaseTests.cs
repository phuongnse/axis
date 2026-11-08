using Axis.Agent;
using Axis.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>The agent host claims an authoring request with a lease, commits each step, resumes after a stop and writes nothing once its claim is gone.</summary>
public sealed class AgentRequestLeaseTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static readonly AuthoringLoopOptions _options = new(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(100));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task A_host_that_stops_in_a_step_leaves_the_request_claimed_until_the_lease_ends_and_the_next_host_resumes_after_the_last_finished_step()
    {
        var (connectionString, requestId) = await CreateRequestAsync("working");
        var reached = NewSignal();
        var working = new TestStep("working", "work", "checking");
        var checkingOfFirstHost = new TestStep("checking", "check", "reviewing", async (context, token) =>
        {
            await context.AddTokensAsync(100, token);
            reached.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        });
        var firstHost = BuildAgent(connectionString, new LogCollector(), working, checkingOfFirstHost);
        await firstHost.StartAsync(CancellationToken);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);

        Assert.Equal("100|100", await ScalarAsync(connectionString, $"SELECT r.tokens || '|' || s.tokens FROM axis.authoring_requests r JOIN axis.authoring_request_steps s ON s.request_id = r.id AND s.finished_at IS NULL WHERE r.id = '{requestId}'"));
        var claimedBy = await ScalarAsync(connectionString, $"SELECT claimed_by FROM axis.authoring_requests WHERE id = '{requestId}'");
        await firstHost.StopAsync(CancellationToken);
        await firstHost.DisposeAsync();

        Assert.Equal($"{claimedBy}|true", await ScalarAsync(connectionString, $"SELECT claimed_by || '|' || (claimed_until > now()) FROM axis.authoring_requests WHERE id = '{requestId}'"));

        var checkingOfSecondHost = new TestStep("checking", "check", "reviewing", (context, token) => context.AddTokensAsync(40, token));
        await using var secondHost = BuildAgent(connectionString, new LogCollector(), working, checkingOfSecondHost);
        await secondHost.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(connectionString, requestId) == "reviewing", "The second host did not move the request to reviewing.");

        Assert.Equal(1, working.Runs);
        Assert.Equal(1, checkingOfFirstHost.Runs);
        Assert.Equal(1, checkingOfSecondHost.Runs);
        Assert.Equal("1:work:done:0:true,2:check:interrupted:100:true,3:check:done:40:true", await StepsAsync(connectionString, requestId));
        Assert.Equal("reviewing|140|-", await RequestAsync(connectionString, requestId));
    }

    [Theory]
    [InlineData("claimed by another host", "UPDATE axis.authoring_requests SET claimed_by = 'other', claimed_until = now() + interval '1 hour' WHERE id = '{0}'", "working|0|other")]
    [InlineData("dropped", "UPDATE axis.authoring_requests SET state = 'dropped', finished_at = now(), claimed_by = NULL, claimed_until = NULL WHERE id = '{0}'", "dropped|0|-")]
    public async Task A_host_that_lost_its_claim_or_whose_request_was_dropped_commits_nothing_more(string reason, string takeSql, string expectedRequest)
    {
        var (connectionString, requestId) = await CreateRequestAsync("working");
        var logs = new LogCollector();
        var reached = NewSignal();
        var release = NewSignal();
        var ended = NewSignal();
        Exception? refused = null;
        var working = new TestStep("working", "work", "checking", async (context, _) =>
        {
            reached.SetResult();
            await release.Task;
            try
            {
                await context.AddTokensAsync(50, CancellationToken.None);
            }
            catch (AuthoringClaimLostException exception)
            {
                refused = exception;
            }

            ended.SetResult();
        });
        var checking = new TestStep("checking", "check", "reviewing");
        await using var host = BuildAgent(connectionString, logs, working, checking);
        await host.StartAsync(CancellationToken);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);

        await ScalarAsync(connectionString, string.Format(System.Globalization.CultureInfo.InvariantCulture, takeSql, requestId) + " RETURNING 1");
        release.SetResult();
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);
        await WaitUntilAsync(
            () => logs.Entries.Any(entry => entry.Contains("no longer holds the claim", StringComparison.Ordinal)),
            $"The host did not notice that it lost the request ({reason}).");
        await host.StopAsync(CancellationToken);

        Assert.IsType<AuthoringClaimLostException>(refused);
        Assert.Equal(expectedRequest, await RequestAsync(connectionString, requestId));
        Assert.Equal("1:work:-:0:false", await StepsAsync(connectionString, requestId));
        Assert.Equal(0, checking.Runs);
    }

    [Fact]
    public async Task The_tokens_of_a_step_are_on_its_row_and_on_the_request_while_the_step_still_runs()
    {
        var (connectionString, requestId) = await CreateRequestAsync("working");
        var reached = NewSignal();
        var release = NewSignal();
        var working = new TestStep("working", "work", "checking", async (context, token) =>
        {
            await context.AddTokensAsync(30, token);
            await context.AddTokensAsync(12, token);
            reached.SetResult();
            await release.Task.WaitAsync(token);
        });
        await using var host = BuildAgent(connectionString, new LogCollector(), working);
        await host.StartAsync(CancellationToken);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);

        Assert.Equal("working|42|true", await ScalarAsync(connectionString, $"SELECT r.state || '|' || r.tokens || '|' || (s.tokens = 42 AND s.finished_at IS NULL) FROM axis.authoring_requests r JOIN axis.authoring_request_steps s ON s.request_id = r.id WHERE r.id = '{requestId}'"));

        release.SetResult();
        await WaitUntilAsync(async () => await StateAsync(connectionString, requestId) == "checking", "The host did not commit the step.");

        Assert.Equal("checking|42|-", await RequestAsync(connectionString, requestId));
        Assert.Equal("1:work:done:42:true", await StepsAsync(connectionString, requestId));
    }

    [Fact]
    public async Task A_host_keeps_the_claim_while_the_next_state_has_a_step_and_frees_the_request_when_it_has_none()
    {
        var (connectionString, requestId) = await CreateRequestAsync("planning");
        var working = new TestStep("working", "work", "checking");
        var planning = new TestStep("planning", "plan", "awaitingApproval");
        await using var host = BuildAgent(connectionString, new LogCollector(), planning, working);
        await host.StartAsync(CancellationToken);

        await WaitUntilAsync(async () => await StateAsync(connectionString, requestId) == "awaitingApproval", "The host did not finish the plan.");

        Assert.Equal("awaitingApproval|0|-", await RequestAsync(connectionString, requestId));
        Assert.Equal("1:plan:done:0:true", await StepsAsync(connectionString, requestId));
        Assert.Equal(0, working.Runs);
    }

    [Fact]
    public async Task A_run_of_short_steps_that_outlasts_the_lease_keeps_the_claim_alive()
    {
        var (connectionString, requestId) = await CreateRequestAsync("planning");
        var stepTime = _options.Lease / 4 + TimeSpan.FromMilliseconds(100);
        Func<AuthoringStepContext, CancellationToken, Task> pause = (_, token) => Task.Delay(stepTime, token);
        string? claimed = null;
        var planning = new TestStep("planning", "plan", "working", pause);
        var working = new TestStep("working", "work", "checking", pause);
        var checking = new TestStep("checking", "check", "reviewing", pause);
        var reviewing = new TestStep("reviewing", "review", "fixing", pause);
        var fixing = new TestStep("fixing", "fix", "awaitingApproval", async (_, _) =>
            claimed = await ScalarAsync(connectionString, $"SELECT claimed_until > now() FROM axis.authoring_requests WHERE id = '{requestId}'"));
        await using var host = BuildAgent(connectionString, new LogCollector(), planning, working, checking, reviewing, fixing);
        var started = DateTimeOffset.UtcNow;
        await host.StartAsync(CancellationToken);

        await WaitUntilAsync(async () => await StateAsync(connectionString, requestId) == "awaitingApproval", "The host did not finish the steps.");

        Assert.True(DateTimeOffset.UtcNow - started > _options.Lease, "The steps must run longer than the lease.");
        Assert.Equal("True", claimed);
        Assert.Equal(5, (await StepsAsync(connectionString, requestId)).Split(',').Length);
    }

    [Fact]
    public async Task A_host_with_no_registered_step_claims_nothing()
    {
        var (connectionString, requestId) = await CreateRequestAsync("working");
        await using var host = BuildAgent(connectionString, new LogCollector());
        await host.StartAsync(CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken);

        Assert.Equal("working|0|-", await RequestAsync(connectionString, requestId));
    }

    private async Task<(string ConnectionString, Guid RequestId)> CreateRequestAsync(string state)
    {
        var connectionString = await database.CreateDatabaseAsync("agent_lease", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(connectionString, CancellationToken);
        var requestId = Guid.CreateVersion7();
        await ScalarAsync(
            connectionString,
            $"INSERT INTO axis.authoring_requests (id, application_id, author, text, state) VALUES ('{requestId}', '{Guid.NewGuid()}', 'author-1', 'Add a field.', '{state}') RETURNING 1");
        return (connectionString, requestId);
    }

    private static WebApplication BuildAgent(string connectionString, LogCollector logs, params TestStep[] steps)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Health:Urls"] = "http://127.0.0.1:0",
            ["Ai:Bindings:main:Kind"] = "anthropic",
            ["Ai:Bindings:main:Model"] = "model-1",
            ["Ai:DefaultBinding"] = "main",
            ["Tenants:a:Hosts:0"] = "a.example.test",
            ["Tenants:a:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(connectionString),
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddSingleton(_options);
        foreach (var step in steps)
        {
            builder.Services.AddSingleton<IAuthoringStep>(step);
        }

        builder.Services.AddAxisAgent(builder.Configuration, builder.Environment);
        var app = builder.Build();
        app.MapHostHealth();
        return app;
    }

    // The rows are read and changed as the superuser of the container, which names the database through the connection string.
    private static async Task<string> ScalarAsync(string connectionString, string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(connectionString, sql, CancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private static Task<string> StateAsync(string connectionString, Guid requestId) =>
        ScalarAsync(connectionString, $"SELECT state FROM axis.authoring_requests WHERE id = '{requestId}'");

    private static Task<string> RequestAsync(string connectionString, Guid requestId) =>
        ScalarAsync(connectionString, $"SELECT state || '|' || tokens || '|' || coalesce(claimed_by, '-') FROM axis.authoring_requests WHERE id = '{requestId}'");

    private static Task<string> StepsAsync(string connectionString, Guid requestId) =>
        ScalarAsync(
            connectionString,
            $"SELECT string_agg(ordinal || ':' || kind || ':' || coalesce(outcome, '-') || ':' || tokens || ':' || (finished_at IS NOT NULL), ',' ORDER BY ordinal) FROM axis.authoring_request_steps WHERE request_id = '{requestId}'");

    private sealed class TestStep(string state, string kind, string nextState, Func<AuthoringStepContext, CancellationToken, Task>? body = null) : IAuthoringStep
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public string State { get; } = state;

        public string Kind { get; } = kind;

        public async Task<AuthoringStepResult> RunAsync(AuthoringStepContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runs);
            if (body is not null)
            {
                await body(context, cancellationToken);
            }

            return new AuthoringStepResult(nextState, "done");
        }
    }
}
