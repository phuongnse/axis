using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using static Axis.Integration.Tests.AdminTenantRestoreTests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// After a request commits a work item due now, the server sends <c>NOTIFY axis_work</c> with the tenant id on the platform database,
/// under the directory source only. A test listens on that channel, and a worker under the directory source listens too: a signal
/// for its tenant runs the tenant's work at once, even with a long poll interval, and the worker listens again when the connection drops.
/// </summary>
public sealed class WakeSignalTests(AdminBackupFixture fixture) : IClassFixture<AdminBackupFixture>
{
    private const string App = "/api/apps/PurchaseRequests";

    private const string Waiting = "waiting:managerApproval";

    private static readonly TimeSpan _received = TimeSpan.FromSeconds(10);

    // Long next to every wait of a test, so only a wake signal or the first poll after a connect can start work.
    private static readonly TimeSpan _longPoll = TimeSpan.FromSeconds(30);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_process_start_through_the_server_on_the_directory_sends_the_wake_signal_for_its_tenant()
    {
        await using var listener = await WorkSignalListener.StartAsync(fixture.Scenario.Platform);
        await using var server = CreateDirectoryServer(fixture.Scenario.Platform);
        await RefreshAsync(server);
        using var requester = server.CreateClient().SignedInAs("requester");
        var subject = await AdminBackupFixture.CreateRequestAsync(requester);
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
        listener.Drain();

        await StartAsync(requester, subject);

        Assert.True(await listener.ReceivesAsync(fixture.TenantId, _received));
    }

    [Fact]
    public async Task A_task_completion_through_the_server_on_the_directory_sends_a_fresh_wake_signal_for_its_tenant()
    {
        await using var server = CreateDirectoryServer(fixture.Scenario.Platform);
        await RefreshAsync(server);
        using var requester = server.CreateClient().SignedInAs("requester");
        var subject = await AdminBackupFixture.CreateRequestAsync(requester);
        var instance = await StartAsync(requester, subject);

        // The server has applied the platform grants that the worker needs.
        var worker = await RestoredTenantWorkTests.BuildWorkerAsync(fixture.Scenario.Platform);
        try
        {
            await Polling.WaitUntilAsync(async () => await StateAsync(instance) == "waiting:managerApproval", "The instance did not wait for its manager.");
        }
        finally
        {
            await worker.StopAsync(CancellationToken);
            await worker.DisposeAsync();
        }

        var task = (Guid)(await PostgreSqlServer.AdminScalarAsync(
            await TenantDatabaseAsync(),
            $"SELECT id FROM axis.process_tasks WHERE process_instance_id = '{instance:D}' AND state = 'open'",
            CancellationToken))!;
        await using var listener = await WorkSignalListener.StartAsync(fixture.Scenario.Platform);
        using var manager = server.CreateClient().SignedInAs("manager-2");

        using var complete = await manager.SendAsync(
            Request(HttpMethod.Post, $"{App}/tasks/{task:D}/complete", AdminBackupFixture.Host, """{ "outcome": "approve" }"""),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.True(await listener.ReceivesAsync(fixture.TenantId, _received));
    }

    [Fact]
    public async Task A_process_start_through_the_server_on_the_configuration_source_sends_no_wake_signal()
    {
        await using var listener = await WorkSignalListener.StartAsync(fixture.Scenario.Platform);
        using var requester = fixture.CreateClient();
        var subject = await AdminBackupFixture.CreateRequestAsync(requester);
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
        listener.Drain();

        await StartAsync(requester, subject);

        Assert.False(await listener.ReceivesAsync(fixture.TenantId, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task A_process_start_through_the_server_on_the_directory_runs_its_first_step_within_a_second_of_a_long_poll_interval()
    {
        await using var server = CreateDirectoryServer(fixture.Scenario.Platform);
        await RefreshAsync(server);
        using var requester = server.CreateClient().SignedInAs("requester");
        var worker = await StartListeningWorkerAsync(requester);
        try
        {
            var subject = await AdminBackupFixture.CreateRequestAsync(requester);

            var instance = await StartAsync(requester, subject);

            await Polling.WaitUntilAsync(async () => await StateAsync(instance) == Waiting, "The instance did not wait for its manager within a second.", TimeSpan.FromSeconds(1));
        }
        finally
        {
            await StopAsync(worker);
        }
    }

    [Fact]
    public async Task The_worker_listens_again_when_its_connection_is_ended_and_a_later_signal_still_wakes_its_tenant_within_a_second()
    {
        await using var server = CreateDirectoryServer(fixture.Scenario.Platform);
        await RefreshAsync(server);
        using var requester = server.CreateClient().SignedInAs("requester");
        var worker = await StartListeningWorkerAsync(requester);
        try
        {
            var pid = await ListenerPidAsync(_received);
            await PostgreSqlServer.AdminScalarAsync(fixture.Scenario.Platform, $"SELECT pg_terminate_backend({pid})", CancellationToken);
            await Polling.WaitUntilAsync(async () => await ListenerPidAsync(TimeSpan.Zero) is { } now && now != pid, "The worker did not listen again.", _received);

            // The poll of every tenant that follows a connect has run.
            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
            var subject = await AdminBackupFixture.CreateRequestAsync(requester);

            var instance = await StartAsync(requester, subject);

            await Polling.WaitUntilAsync(async () => await StateAsync(instance) == Waiting, "The instance did not wait for its manager within a second.", TimeSpan.FromSeconds(1));
        }
        finally
        {
            await StopAsync(worker);
        }
    }

    [Fact]
    public async Task A_signal_that_names_an_unknown_tenant_claims_no_work_and_does_not_stop_the_worker()
    {
        await using var server = CreateDirectoryServer(fixture.Scenario.Platform);
        await RefreshAsync(server);
        using var warmUp = server.CreateClient().SignedInAs("requester");
        var worker = await StartListeningWorkerAsync(warmUp);
        try
        {
            var pid = await ListenerPidAsync(_received);

            // The configuration-source server sends no signal, so the instance is due and nothing wakes the worker.
            using var requester = fixture.CreateClient();
            var subject = await AdminBackupFixture.CreateRequestAsync(requester);
            var instance = await StartAsync(requester, subject);
            var before = await StateAsync(instance);
            await PostgreSqlServer.AdminScalarAsync(fixture.Scenario.Platform, "SELECT pg_notify('axis_work', 'no-such-tenant')", CancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken);

            Assert.Equal(before, await StateAsync(instance));
            Assert.NotEqual(Waiting, before);
            Assert.Equal(pid, await ListenerPidAsync(TimeSpan.Zero));
            Assert.False(worker.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);

            await PostgreSqlServer.AdminScalarAsync(fixture.Scenario.Platform, $"SELECT pg_notify('axis_work', '{fixture.TenantId}')", CancellationToken);

            await Polling.WaitUntilAsync(async () => await StateAsync(instance) == Waiting, "The instance did not wait for its manager within a second.", TimeSpan.FromSeconds(1));
        }
        finally
        {
            await StopAsync(worker);
        }
    }

    private static async Task<Guid> StartAsync(HttpClient client, Guid subject)
    {
        using var request = Request(HttpMethod.Post, $"{App}/processes/PurchaseRequestApproval/instances", AdminBackupFixture.Host, $$"""{ "subjectId": "{{subject:D}}" }""");
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    // Starts a worker on the directory with a long poll interval, waits until it listens, and lets it run one instance, so its tenant is ready.
    private async Task<WebApplication> StartListeningWorkerAsync(HttpClient requester)
    {
        var worker = await RestoredTenantWorkTests.BuildWorkerAsync(fixture.Scenario.Platform, _longPoll);
        try
        {
            Assert.NotNull(await ListenerPidAsync(_received));
            var instance = await StartAsync(requester, await AdminBackupFixture.CreateRequestAsync(requester));
            await Polling.WaitUntilAsync(async () => await StateAsync(instance) == Waiting, "The first instance did not wait for its manager.", _received);

            // The tenant has left the active list again.
            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
            return worker;
        }
        catch
        {
            await StopAsync(worker);
            throw;
        }
    }

    private static async Task StopAsync(WebApplication worker)
    {
        await worker.StopAsync(CancellationToken);
        await worker.DisposeAsync();
    }

    /// <summary>The process id of the worker's listening connection, waiting up to <paramref name="within"/> for it. Null when there is none.</summary>
    private async Task<int?> ListenerPidAsync(TimeSpan within)
    {
        var database = new NpgsqlConnectionStringBuilder(fixture.Scenario.Platform).Database;
        var sql = $"SELECT pid FROM pg_stat_activity WHERE application_name = 'axis-worker-listen' AND datname = '{database}' AND query = 'LISTEN axis_work'";
        var deadline = DateTime.UtcNow + within;
        while (true)
        {
            if (await PostgreSqlServer.AdminScalarAsync(fixture.Scenario.Platform, sql, CancellationToken) is int pid)
            {
                return pid;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken);
        }
    }

    private async Task<string?> StateAsync(Guid instance) =>
        (string?)await PostgreSqlServer.AdminScalarAsync(
            await TenantDatabaseAsync(),
            $"SELECT state || ':' || step FROM axis.process_instances WHERE id = '{instance:D}'",
            CancellationToken);

    private async Task<string> TenantDatabaseAsync() =>
        new NpgsqlConnectionStringBuilder(fixture.Scenario.Platform) { Database = (await fixture.Scenario.RowAsync(fixture.TenantId)).DatabaseName }.ConnectionString;
}
