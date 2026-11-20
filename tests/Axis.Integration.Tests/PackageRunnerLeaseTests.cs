using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Processes.Work;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// A customer-hosted runner's take queues a <c>package.call</c> item that is due when the lease ends, a posted result deletes it, and a lease that ends
/// with no result is an unknown outcome, see <c>docs/reference/connector-and-package-calls.md#package-call-outcomes</c>, with real PostgreSQL and Keycloak. The
/// lease end is forced by making the item due, and the item runs on the worker's handler. Each test makes the realm of its tenant with a unique name and
/// deletes it at the end.
/// </summary>
public sealed class PackageRunnerLeaseTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private const string Take = "/api/package-runner/calls/take";
    private const string HoldsTools = """{ "packages": [{ "id": "tools", "version": "1.0.0" }] }""";
    private const string Timeout = "The operation did not finish within its timeout.";

    private static readonly string _sumManifest = CustomerHostedReleases.ToolsManifest("1.0.0", "sum");

    private readonly PackageRunnerServer _server = new(database, keycloak);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task A_take_queues_one_item_due_at_the_end_of_the_lease_and_a_result_deletes_it()
    {
        await _server.PrepareAsync();
        var (id, tenantDatabase) = await _server.AddTenantAsync();
        try
        {
            var releases = new CustomerHostedReleases();
            var releaseId = await releases.ActivateOwnWithProcessAsync(tenantDatabase, _sumManifest, CancellationToken);
            var callId = await releases.StageToolsSumCallAsync(tenantDatabase, releaseId, "1.0.0", CancellationToken);
            await using var factory = _server.Start();
            using var admin = PackageRunnerServer.Client(factory, id).SignedInAs("admin");
            var runner = await _server.CreateRunnerAsync(admin, id);
            using var client = PackageRunnerServer.Client(factory, id);
            var lease = await RegisterAndTakeAsync(client, runner);
            var items = ItemsOf(callId);

            Assert.Equal(1L, await CountAsync(tenantDatabase, items));
            Assert.Equal(1L, await CountAsync(tenantDatabase, $"{items} AND due_at = (SELECT lease_until FROM axis.package_calls WHERE id = '{callId}')"));

            using var response = await PackageRunnerServer.PostAsync(client, ResultPath(lease), runner.Token, """{ "outcome": "succeeded", "output": { "total": "5" } }""");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(0L, await CountAsync(tenantDatabase, items));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_lease_that_ends_with_no_result_leaves_a_call_that_is_not_idempotent_unknown_and_the_instance_waiting()
    {
        await _server.PrepareAsync();
        var (id, tenantDatabase) = await _server.AddTenantAsync();
        try
        {
            var releases = new CustomerHostedReleases();
            var releaseId = await releases.ActivateOwnWithProcessAsync(tenantDatabase, _sumManifest, CancellationToken);
            var callId = await releases.StageToolsSumCallAsync(tenantDatabase, releaseId, "1.0.0", CancellationToken);
            await PostgreSqlServer.AdminScalarAsync(tenantDatabase, $"UPDATE axis.package_calls SET idempotent = false WHERE id = '{callId}'", CancellationToken);
            await using var factory = _server.Start();
            using var admin = PackageRunnerServer.Client(factory, id).SignedInAs("admin");
            var runner = await _server.CreateRunnerAsync(admin, id);
            using var client = PackageRunnerServer.Client(factory, id);
            await RegisterAndTakeAsync(client, runner);
            var instanceId = (await CallAsync(tenantDatabase, callId, "process_instance_id"))!;

            Assert.Equal(WorkItemOutcome.Completed, await EndLeaseAsync(id, tenantDatabase, callId));

            Assert.Equal("unknown", await CallAsync(tenantDatabase, callId, "state"));
            Assert.Equal(Timeout, await CallAsync(tenantDatabase, callId, "last_error"));
            Assert.Equal("1", await CallAsync(tenantDatabase, callId, "attempts"));
            Assert.Equal(1L, await CountAsync(tenantDatabase, $"SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = '{instanceId}' AND step = 'sum' AND output->>'outcome' = 'unknown' AND error = '{Timeout}'"));
            Assert.Equal(1L, await CountAsync(tenantDatabase, $"SELECT count(*) FROM axis.process_call_attempts WHERE call_id = '{callId}' AND outcome = 'unknown'"));
            Assert.Equal("waiting/sum", await InstanceAsync(tenantDatabase, instanceId, "state || '/' || step"));
            Assert.Equal(0L, await CountAsync(tenantDatabase, ItemsOf(callId)));
            Assert.Equal(0L, await CountAsync(tenantDatabase, $"SELECT count(*) FROM axis.audit_records WHERE process_instance_id = '{instanceId}' AND action = 'operation.called'"));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_lease_that_ends_with_no_result_sets_an_idempotent_call_back_to_pending_and_a_runner_takes_it_again_after_the_backoff()
    {
        await _server.PrepareAsync();
        var (id, tenantDatabase) = await _server.AddTenantAsync();
        try
        {
            var releases = new CustomerHostedReleases();
            var releaseId = await releases.ActivateOwnWithProcessAsync(tenantDatabase, _sumManifest, CancellationToken);
            var callId = await releases.StageToolsSumCallAsync(tenantDatabase, releaseId, "1.0.0", CancellationToken);
            await using var factory = _server.Start();
            using var admin = PackageRunnerServer.Client(factory, id).SignedInAs("admin");
            var runner = await _server.CreateRunnerAsync(admin, id);
            using var client = PackageRunnerServer.Client(factory, id);
            await RegisterAndTakeAsync(client, runner);
            var items = ItemsOf(callId);

            Assert.Equal(WorkItemOutcome.Rescheduled, await EndLeaseAsync(id, tenantDatabase, callId));

            Assert.Equal("pending", await CallAsync(tenantDatabase, callId, "state"));
            Assert.Equal(Timeout, await CallAsync(tenantDatabase, callId, "last_error"));
            Assert.Equal(1L, await CountAsync(tenantDatabase, items));
            Assert.Equal(1L, await CountAsync(tenantDatabase, $"{items} AND due_at > now() + interval '20 seconds'"));
            using (var early = await PackageRunnerServer.PostAsync(client, Take, runner.Token, HoldsTools))
            {
                Assert.Equal(HttpStatusCode.NoContent, early.StatusCode);
            }

            Assert.Equal(WorkItemOutcome.Completed, await EndLeaseAsync(id, tenantDatabase, callId));

            Assert.Equal(0L, await CountAsync(tenantDatabase, items));
            using var again = await PackageRunnerServer.PostAsync(client, Take, runner.Token, HoldsTools);
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            Assert.Equal(callId, (await again.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("operationId").GetGuid());
            Assert.Equal("2", await CallAsync(tenantDatabase, callId, "attempts"));
            Assert.Equal("sending", await CallAsync(tenantDatabase, callId, "state"));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_result_after_the_lease_end_item_ran_answers_409_and_changes_nothing()
    {
        await _server.PrepareAsync();
        var (id, tenantDatabase) = await _server.AddTenantAsync();
        try
        {
            var releases = new CustomerHostedReleases();
            var releaseId = await releases.ActivateOwnWithProcessAsync(tenantDatabase, _sumManifest, CancellationToken);
            var callId = await releases.StageToolsSumCallAsync(tenantDatabase, releaseId, "1.0.0", CancellationToken);
            await PostgreSqlServer.AdminScalarAsync(tenantDatabase, $"UPDATE axis.package_calls SET idempotent = false WHERE id = '{callId}'", CancellationToken);
            await using var factory = _server.Start();
            using var admin = PackageRunnerServer.Client(factory, id).SignedInAs("admin");
            var runner = await _server.CreateRunnerAsync(admin, id);
            using var client = PackageRunnerServer.Client(factory, id);
            var lease = await RegisterAndTakeAsync(client, runner);
            var instanceId = (await CallAsync(tenantDatabase, callId, "process_instance_id"))!;
            Assert.Equal(WorkItemOutcome.Completed, await EndLeaseAsync(id, tenantDatabase, callId));

            using var response = await PackageRunnerServer.PostAsync(client, ResultPath(lease), runner.Token, """{ "outcome": "succeeded", "output": { "total": "5" } }""");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("unknown", await CallAsync(tenantDatabase, callId, "state"));
            Assert.Equal("1", await CallAsync(tenantDatabase, callId, "attempts"));
            Assert.Equal("waiting/sum", await InstanceAsync(tenantDatabase, instanceId, "state || '/' || step"));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    private static string ItemsOf(Guid callId) =>
        $"SELECT count(*) FROM axis.process_work_items WHERE package_call_id = '{callId}' AND kind = 'package.call'";

    // Makes the call's item due, as the end of the lease does, and runs it on the worker's handler.
    private static async Task<WorkItemOutcome?> EndLeaseAsync(string tenantId, string tenantDatabase, Guid callId)
    {
        await PostgreSqlServer.AdminScalarAsync(tenantDatabase, $"UPDATE axis.process_work_items SET due_at = now() WHERE package_call_id = '{callId}'", CancellationToken);
        return await PackageRunnerServer.RunPackageCallItemAsync(tenantId, tenantDatabase);
    }

    private static string ResultPath(Guid lease) => $"/api/package-runner/leases/{lease}/result";

    // Registers the runner's package and takes the one waiting call, and returns the lease.
    private static async Task<Guid> RegisterAndTakeAsync(HttpClient client, PackageRunnerServer.RunnerCredential runner)
    {
        using (var registered = await PackageRunnerServer.RegisterAsync(client, runner.Token, _sumManifest))
        {
            Assert.Equal(HttpStatusCode.NoContent, registered.StatusCode);
        }

        using var taken = await PackageRunnerServer.PostAsync(client, Take, runner.Token, HoldsTools);
        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
        return (await taken.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("leaseId").GetGuid();
    }

    private static async Task<string?> CallAsync(string tenantDatabase, Guid callId, string expression) =>
        await TextAsync(tenantDatabase, $"SELECT ({expression})::text FROM axis.package_calls WHERE id = '{callId}'");

    private static async Task<string?> InstanceAsync(string tenantDatabase, string instanceId, string expression) =>
        await TextAsync(tenantDatabase, $"SELECT ({expression})::text FROM axis.process_instances WHERE id = '{instanceId}'");

    private static async Task<long> CountAsync(string tenantDatabase, string sql) =>
        Convert.ToInt64(await PostgreSqlServer.AdminScalarAsync(tenantDatabase, sql, CancellationToken), CultureInfo.InvariantCulture);

    private static async Task<string?> TextAsync(string tenantDatabase, string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(tenantDatabase, sql, CancellationToken), CultureInfo.InvariantCulture) is { Length: > 0 } text ? text : null;
}
