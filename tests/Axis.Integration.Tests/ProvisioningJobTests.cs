using Axis.Admin;
using Npgsql;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

public sealed class ProvisioningJobTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static readonly TimeSpan _lease = TimeSpan.FromMinutes(5);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_publisher_runs_a_queued_suspend_job_and_writes_one_audit_record_with_its_actor()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, "shop.example.test")).Exit);
        var job = await ProvisioningJobs.QueueAsync(scenario.Platform, ProvisioningJobs.Builtin(scenario.Platform), "suspend", id, "operator-1", "key-1", "abc123", CancellationToken);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var run = scenario.RunAsync(["publisher"], stop: stop, extra: AdminScenario.Directory);
        try
        {
            await WaitUntilAsync(async () => await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{job}'") is "succeeded" or "failed", "The job did not end.", TimeSpan.FromSeconds(60));
        }
        finally
        {
            await stop.CancelAsync();
        }

        var result = await run;

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Equal("succeeded", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal("suspended", (await scenario.RowAsync(id)).Status);
        Assert.Contains($"{id} status ok", result.Output);
        Assert.Equal(1L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.audit_records WHERE tenant_id = '{id}' AND command = 'tenant suspend' AND actor = 'operator-1'"));
        Assert.Equal(1L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.audit_records WHERE changed = ARRAY['job:{job}']"));
        Assert.Equal("succeeded", await ScalarAsync(scenario, $"SELECT steps -> 0 ->> 'state' FROM platform.jobs WHERE id = '{job}'"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_step_that_throws_once_runs_the_job_again_and_the_finished_step_is_skipped()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var firstRuns = 0;
        var secondRuns = 0;
        var kinds = Kinds(
            new JobStep("first", (_, _, _) =>
            {
                firstRuns++;
                return Task.FromResult<string?>(null);
            }),
            new JobStep("second", (_, _, _) =>
            {
                secondRuns++;
                return secondRuns == 1 ? throw new InvalidOperationException("The step broke.") : Task.FromResult<string?>(null);
            }));
        var job = await ProvisioningJobs.QueueAsync(scenario.Platform, kinds, "suspend", id, "operator-1", null, null, CancellationToken);
        var jobs = new ProvisioningJobs(scenario.Platform, kinds, _lease);
        using var output = new StringWriter();

        Assert.True(await jobs.RunNextAsync(output, CancellationToken));
        Assert.Equal("running", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal(0L, await ScalarAsync(scenario, "SELECT count(*) FROM platform.audit_records"));
        Assert.True(await jobs.RunNextAsync(output, CancellationToken));

        Assert.Equal("succeeded", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal(2, await ScalarAsync(scenario, $"SELECT claims FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal(1, firstRuns);
        Assert.Equal(2, secondRuns);
        Assert.Contains($"{id} job released: the job runs again in the next round.", output.ToString());
        Assert.Equal(1L, await ScalarAsync(scenario, "SELECT count(*) FROM platform.audit_records WHERE actor = 'operator-1'"));
        Assert.False(await jobs.RunNextAsync(output, CancellationToken));
    }

    [Fact]
    public async Task A_suspend_job_for_a_provisioning_tenant_fails_at_once_with_the_refusal_and_one_audit_record()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, "shop.example.test")).Exit);
        await scenario.SetStatusAsync(id, "provisioning");
        var kinds = ProvisioningJobs.Builtin(scenario.Platform);
        var job = await ProvisioningJobs.QueueAsync(scenario.Platform, kinds, "suspend", id, "operator-1", null, null, CancellationToken);
        using var output = new StringWriter();

        Assert.True(await new ProvisioningJobs(scenario.Platform, kinds, _lease).RunNextAsync(output, CancellationToken));

        Assert.Equal("failed", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal(1, await ScalarAsync(scenario, $"SELECT claims FROM platform.jobs WHERE id = '{job}'"));
        Assert.Contains("still provisioning", (string?)await ScalarAsync(scenario, $"SELECT error FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal("failed", await ScalarAsync(scenario, $"SELECT steps -> 0 ->> 'state' FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal("provisioning", (await scenario.RowAsync(id)).Status);
        Assert.Equal(1L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.audit_records WHERE tenant_id = '{id}' AND command = 'tenant suspend' AND actor = 'operator-1'"));
        Assert.Contains($"{id} status failed: the tenant is still provisioning. Finish tenant create first.", output.ToString());
    }

    [Fact]
    public async Task A_job_for_an_unknown_tenant_fails_at_once_with_the_refusal()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var kinds = ProvisioningJobs.Builtin(scenario.Platform);
        var job = await ProvisioningJobs.QueueAsync(scenario.Platform, kinds, "resume", "no-such-tenant", "operator-1", null, null, CancellationToken);

        Assert.True(await new ProvisioningJobs(scenario.Platform, kinds, _lease).RunNextAsync(TextWriter.Null, CancellationToken));

        Assert.Equal("failed", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal("The tenant does not exist.", await ScalarAsync(scenario, $"SELECT error FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal(1L, await ScalarAsync(scenario, "SELECT count(*) FROM platform.audit_records WHERE command = 'tenant resume'"));
    }

    [Fact]
    public async Task A_step_that_always_throws_fails_the_job_on_the_third_claim_with_one_audit_record()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var kinds = Kinds(new JobStep("status", (_, _, _) => throw new InvalidOperationException("The step broke.")));
        var job = await ProvisioningJobs.QueueAsync(scenario.Platform, kinds, "suspend", id, "operator-1", null, null, CancellationToken);
        var jobs = new ProvisioningJobs(scenario.Platform, kinds, _lease);

        for (var round = 1; round <= 3; round++)
        {
            Assert.True(await jobs.RunNextAsync(TextWriter.Null, CancellationToken));
            Assert.Equal(round, await ScalarAsync(scenario, $"SELECT claims FROM platform.jobs WHERE id = '{job}'"));
            Assert.Equal(round == 3 ? 1L : 0L, await ScalarAsync(scenario, "SELECT count(*) FROM platform.audit_records"));
        }

        Assert.Equal("failed", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal("The step broke.", await ScalarAsync(scenario, $"SELECT error FROM platform.jobs WHERE id = '{job}'"));
        Assert.False(await jobs.RunNextAsync(TextWriter.Null, CancellationToken));
        Assert.Equal(1L, await ScalarAsync(scenario, "SELECT count(*) FROM platform.audit_records WHERE actor = 'operator-1'"));
    }

    [Fact]
    public async Task A_running_job_whose_lease_ran_out_after_three_claims_ends_failed_at_the_next_claim_with_its_audit_record()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var kinds = ProvisioningJobs.Builtin(scenario.Platform);
        var job = await ProvisioningJobs.QueueAsync(scenario.Platform, kinds, "suspend", id, "operator-1", null, null, CancellationToken);
        await ScalarAsync(scenario, $"UPDATE platform.jobs SET state = 'running', claims = 3, lease_until = now() - interval '1 minute' WHERE id = '{job}'");

        Assert.False(await new ProvisioningJobs(scenario.Platform, kinds, _lease).RunNextAsync(TextWriter.Null, CancellationToken));

        Assert.Equal("failed", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal(1L, await ScalarAsync(scenario, "SELECT count(*) FROM platform.audit_records WHERE actor = 'operator-1' AND command = 'tenant suspend'"));
    }

    [Fact]
    public async Task A_job_waits_while_another_job_of_the_same_tenant_is_running_and_runs_after_it_ends()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var kinds = Kinds(new JobStep("status", (_, _, _) => Task.FromResult<string?>(null)));
        var running = await ProvisioningJobs.QueueAsync(scenario.Platform, kinds, "suspend", id, "operator-1", null, null, CancellationToken);
        var queued = await ProvisioningJobs.QueueAsync(scenario.Platform, kinds, "resume", id, "operator-1", null, null, CancellationToken);
        await ScalarAsync(scenario, $"UPDATE platform.jobs SET state = 'running', claims = 1, lease_until = now() + interval '1 hour' WHERE id = '{running}'");
        var jobs = new ProvisioningJobs(scenario.Platform, kinds, _lease);

        Assert.False(await jobs.RunNextAsync(TextWriter.Null, CancellationToken));
        Assert.Equal("queued", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{queued}'"));

        await ScalarAsync(scenario, $"UPDATE platform.jobs SET state = 'succeeded' WHERE id = '{running}'");
        Assert.True(await jobs.RunNextAsync(TextWriter.Null, CancellationToken));
        Assert.Equal("succeeded", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{queued}'"));
    }

    // Both kinds that the table allows, each with the given steps.
    private static Dictionary<string, IReadOnlyList<JobStep>> Kinds(params JobStep[] steps) =>
        new() { ["suspend"] = steps, ["resume"] = steps };

    private static async Task<object?> ScalarAsync(AdminScenario scenario, string sql)
    {
        await using var connection = new NpgsqlConnection(scenario.Platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(CancellationToken);
        return value is DBNull ? null : value;
    }
}
