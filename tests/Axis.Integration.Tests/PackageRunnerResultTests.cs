using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Processes.Work;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// A customer-hosted runner posts the <c>succeeded</c>, <c>retryable</c> or <c>failed</c> result of a call it took at <c>POST /api/package-runner/leases/{leaseId}/result</c>, see
/// <c>docs/reference/package-runners.md#routes</c>, with real PostgreSQL and Keycloak. The calls are left as staging leaves them, and the runner
/// takes each one first. The denied cases of the token and the body are in <see cref="PackageRunnerRegistrationTests"/>. Each test makes the
/// realm of its tenant with a unique name and deletes it at the end.
/// </summary>
public sealed class PackageRunnerResultTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private const string Take = "/api/package-runner/calls/take";
    private const string HoldsTools = """{ "packages": [{ "id": "tools", "version": "1.0.0" }] }""";

    private static readonly string _sumManifest = CustomerHostedReleases.ToolsManifest("1.0.0", "sum");

    private readonly PackageRunnerServer _server = new(database, keycloak);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task A_succeeded_result_writes_the_output_to_the_subject_settles_the_call_and_moves_the_instance_on()
    {
        await _server.PrepareAsync();
        var (id, tenantDatabase) = await _server.AddTenantAsync();
        try
        {
            var releases = new CustomerHostedReleases();
            var releaseId = await releases.ActivateOwnWithOrderProcessAsync(tenantDatabase, id, _sumManifest, CancellationToken);
            await PostgreSqlServer.GrantRuntimeRoleAsync(tenantDatabase, CancellationToken);
            var (callId, instanceId, orderId) = await releases.StageOrderSumCallAsync(tenantDatabase, releaseId, "1.0.0", CancellationToken);
            await using var factory = _server.Start();
            using var admin = PackageRunnerServer.Client(factory, id).SignedInAs("admin");
            var runner = await _server.CreateRunnerAsync(admin, id);
            using var client = PackageRunnerServer.Client(factory, id);
            var lease = await RegisterAndTakeAsync(client, runner);
            Assert.Null(await TotalOfAsync(tenantDatabase, orderId));

            using var response = await PackageRunnerServer.PostAsync(client, ResultPath(lease), runner.Token, """{ "outcome": "succeeded", "output": { "total": "5" } }""");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal("5", await TotalOfAsync(tenantDatabase, orderId));
            Assert.Equal("succeeded", await CallAsync(tenantDatabase, callId, "state"));
            Assert.Equal("1", await CallAsync(tenantDatabase, callId, "attempts"));
            Assert.Equal("""{"total": "5"}""", await CallAsync(tenantDatabase, callId, "returned"));
            Assert.Equal(1L, await CountAsync(tenantDatabase, $"SELECT count(*) FROM axis.audit_records WHERE process_instance_id = '{instanceId}' AND action = 'operation.called' AND details->>'outcome' = 'succeeded'"));
            Assert.Equal("running/done", await InstanceAsync(tenantDatabase, instanceId, "state || '/' || step"));
            Assert.Equal(1L, await CountAsync(tenantDatabase, $"SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = '{instanceId}' AND kind = 'process.step'"));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task An_output_that_does_not_fit_answers_204_and_fails_the_call_and_the_instance()
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

            using var response = await PackageRunnerServer.PostAsync(client, ResultPath(lease), runner.Token, """{ "outcome": "succeeded", "output": { "total": "five" } }""");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal("failed", await CallAsync(tenantDatabase, callId, "state"));
            Assert.Equal(
                "The output of the operation 'sum' of the package 'tools' 1.0.0 did not fit its manifest.",
                await CallAsync(tenantDatabase, callId, "last_error"));
            var instanceId = await CallAsync(tenantDatabase, callId, "process_instance_id");
            Assert.Equal("failed", await InstanceAsync(tenantDatabase, Guid.Parse(instanceId!), "state"));
            Assert.Equal(1L, await CountAsync(tenantDatabase, $"SELECT count(*) FROM axis.audit_records WHERE process_instance_id = '{instanceId}' AND action = 'process.failed'"));
            Assert.Equal(1L, await CountAsync(tenantDatabase, $"SELECT count(*) FROM axis.audit_records WHERE process_instance_id = '{instanceId}' AND action = 'operation.called' AND details->>'outcome' = 'failed'"));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_result_for_the_lease_of_another_runner_or_for_an_unknown_lease_answers_404_and_the_call_stays_sending()
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
            var other = await _server.CreateRunnerAsync(admin, id);
            using var client = PackageRunnerServer.Client(factory, id);
            var lease = await RegisterAndTakeAsync(client, runner);
            const string body = """{ "outcome": "succeeded", "output": { "total": "5" } }""";

            using var otherRunner = await PackageRunnerServer.PostAsync(client, ResultPath(lease), other.Token, body);
            using var unknown = await PackageRunnerServer.PostAsync(client, ResultPath(Guid.NewGuid()), runner.Token, body);

            Assert.Equal(HttpStatusCode.NotFound, otherRunner.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal("sending", await CallAsync(tenantDatabase, callId, "state"));
            Assert.Equal(lease, Guid.Parse((await CallAsync(tenantDatabase, callId, "lease_id"))!));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_second_result_answers_409_and_changes_nothing()
    {
        await _server.PrepareAsync();
        var (id, tenantDatabase) = await _server.AddTenantAsync();
        try
        {
            var releases = new CustomerHostedReleases();
            var releaseId = await releases.ActivateOwnWithOrderProcessAsync(tenantDatabase, id, _sumManifest, CancellationToken);
            await PostgreSqlServer.GrantRuntimeRoleAsync(tenantDatabase, CancellationToken);
            var (callId, instanceId, orderId) = await releases.StageOrderSumCallAsync(tenantDatabase, releaseId, "1.0.0", CancellationToken);
            await using var factory = _server.Start();
            using var admin = PackageRunnerServer.Client(factory, id).SignedInAs("admin");
            var runner = await _server.CreateRunnerAsync(admin, id);
            using var client = PackageRunnerServer.Client(factory, id);
            var lease = await RegisterAndTakeAsync(client, runner);
            using (var first = await PackageRunnerServer.PostAsync(client, ResultPath(lease), runner.Token, """{ "outcome": "succeeded", "output": { "total": "5" } }"""))
            {
                Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
            }

            var before = await SnapshotAsync(tenantDatabase, callId, instanceId, orderId);

            using var second = await PackageRunnerServer.PostAsync(client, ResultPath(lease), runner.Token, """{ "outcome": "succeeded", "output": { "total": "9" } }""");

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal(before, await SnapshotAsync(tenantDatabase, callId, instanceId, orderId));
            Assert.Equal("5", await TotalOfAsync(tenantDatabase, orderId));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_result_after_the_lease_has_ended_answers_409_and_changes_nothing()
    {
        await _server.PrepareAsync();
        var (id, tenantDatabase) = await _server.AddTenantAsync();
        try
        {
            var releases = new CustomerHostedReleases();
            var releaseId = await releases.ActivateOwnWithOrderProcessAsync(tenantDatabase, id, _sumManifest, CancellationToken);
            await PostgreSqlServer.GrantRuntimeRoleAsync(tenantDatabase, CancellationToken);
            var (callId, instanceId, orderId) = await releases.StageOrderSumCallAsync(tenantDatabase, releaseId, "1.0.0", CancellationToken);
            await using var factory = _server.Start();
            using var admin = PackageRunnerServer.Client(factory, id).SignedInAs("admin");
            var runner = await _server.CreateRunnerAsync(admin, id);
            using var client = PackageRunnerServer.Client(factory, id);
            var lease = await RegisterAndTakeAsync(client, runner);
            await PostgreSqlServer.AdminScalarAsync(
                tenantDatabase, $"UPDATE axis.package_calls SET lease_until = now() - interval '1 second' WHERE id = '{callId}'", CancellationToken);
            var before = await SnapshotAsync(tenantDatabase, callId, instanceId, orderId);

            using var response = await PackageRunnerServer.PostAsync(client, ResultPath(lease), runner.Token, """{ "outcome": "succeeded", "output": { "total": "5" } }""");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(before, await SnapshotAsync(tenantDatabase, callId, instanceId, orderId));
            Assert.Equal("sending", await CallAsync(tenantDatabase, callId, "state"));
            Assert.Equal("1", await CallAsync(tenantDatabase, callId, "attempts"));
            Assert.Null(await TotalOfAsync(tenantDatabase, orderId));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_retryable_result_sets_the_call_back_to_pending_and_a_runner_takes_it_again_after_the_backoff()
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
            var items = $"SELECT count(*) FROM axis.process_work_items WHERE package_call_id = '{callId}' AND kind = 'package.call'";

            using (var response = await PackageRunnerServer.PostAsync(client, ResultPath(lease), runner.Token, """{ "outcome": "retryable" }"""))
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }

            Assert.Equal("pending", await CallAsync(tenantDatabase, callId, "state"));
            Assert.Equal("1", await CallAsync(tenantDatabase, callId, "attempts"));
            Assert.Equal("Axis.Extensions.RetryableException", await CallAsync(tenantDatabase, callId, "last_error"));
            Assert.Equal(1L, await CountAsync(tenantDatabase, items));
            Assert.Equal(1L, await CountAsync(tenantDatabase, $"{items} AND due_at > now() + interval '20 seconds'"));
            Assert.Equal(1L, await CountAsync(tenantDatabase, $"SELECT count(*) FROM axis.process_call_attempts WHERE call_id = '{callId}' AND outcome = 'retryable'"));
            using (var early = await PackageRunnerServer.PostAsync(client, Take, runner.Token, HoldsTools))
            {
                Assert.Equal(HttpStatusCode.NoContent, early.StatusCode);
            }

            await PostgreSqlServer.AdminScalarAsync(tenantDatabase, $"UPDATE axis.process_work_items SET due_at = now() WHERE package_call_id = '{callId}'", CancellationToken);
            Assert.Equal(WorkItemOutcome.Completed, await PackageRunnerServer.RunPackageCallItemAsync(id, tenantDatabase));

            Assert.Equal(0L, await CountAsync(tenantDatabase, items));
            Assert.Equal("pending", await CallAsync(tenantDatabase, callId, "state"));
            Assert.Equal("1", await CallAsync(tenantDatabase, callId, "attempts"));
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
    public async Task A_retryable_result_on_the_last_attempt_fails_the_call_and_the_instance()
    {
        await _server.PrepareAsync();
        var (id, tenantDatabase) = await _server.AddTenantAsync();
        try
        {
            var releases = new CustomerHostedReleases();
            var releaseId = await releases.ActivateOwnWithProcessAsync(tenantDatabase, _sumManifest, CancellationToken);
            var callId = await releases.StageToolsSumCallAsync(tenantDatabase, releaseId, "1.0.0", CancellationToken);
            await PostgreSqlServer.AdminScalarAsync(tenantDatabase, $"UPDATE axis.package_calls SET attempts = 2 WHERE id = '{callId}'", CancellationToken);
            await using var factory = _server.Start();
            using var admin = PackageRunnerServer.Client(factory, id).SignedInAs("admin");
            var runner = await _server.CreateRunnerAsync(admin, id);
            using var client = PackageRunnerServer.Client(factory, id);
            var lease = await RegisterAndTakeAsync(client, runner);
            Assert.Equal("3", await CallAsync(tenantDatabase, callId, "attempts"));

            using var response = await PackageRunnerServer.PostAsync(client, ResultPath(lease), runner.Token, """{ "outcome": "retryable" }""");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            await AssertFailedAsync(tenantDatabase, callId, "Axis.Extensions.RetryableException");
            Assert.Equal(0L, await CountAsync(tenantDatabase, $"SELECT count(*) FROM axis.process_work_items WHERE package_call_id = '{callId}'"));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_failed_result_fails_the_call_and_the_instance_at_once_with_the_posted_type()
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
            Assert.Equal("1", await CallAsync(tenantDatabase, callId, "attempts"));

            using var response = await PackageRunnerServer.PostAsync(client, ResultPath(lease), runner.Token, """{ "outcome": "failed", "error": "Acme.PaymentDeclined" }""");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            await AssertFailedAsync(tenantDatabase, callId, "Acme.PaymentDeclined");
            Assert.Equal("1", await CallAsync(tenantDatabase, callId, "attempts"));
            Assert.Equal(1L, await CountAsync(tenantDatabase, $"SELECT count(*) FROM axis.process_call_attempts WHERE call_id = '{callId}' AND outcome = 'failed'"));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task An_unknown_outcome_or_a_failed_result_without_an_error_answers_400_and_changes_nothing()
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

            foreach (var body in new[] { """{ "outcome": "done" }""", """{ "outcome": "failed" }""", """{ "outcome": "failed", "error": "" }""" })
            {
                using var response = await PackageRunnerServer.PostAsync(client, ResultPath(lease), runner.Token, body);

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }

            Assert.Equal("sending", await CallAsync(tenantDatabase, callId, "state"));
            Assert.Equal("1", await CallAsync(tenantDatabase, callId, "attempts"));
            Assert.Equal("waiting", await InstanceAsync(tenantDatabase, Guid.Parse((await CallAsync(tenantDatabase, callId, "process_instance_id"))!), "state"));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    // The call and its instance are failed with the error, and the history and the audit records say so.
    private static async Task AssertFailedAsync(string tenantDatabase, Guid callId, string error)
    {
        var instanceId = (await CallAsync(tenantDatabase, callId, "process_instance_id"))!;
        Assert.Equal("failed", await CallAsync(tenantDatabase, callId, "state"));
        Assert.Equal(error, await CallAsync(tenantDatabase, callId, "last_error"));
        Assert.Equal("failed", await InstanceAsync(tenantDatabase, Guid.Parse(instanceId), "state"));
        Assert.Equal(1L, await CountAsync(tenantDatabase, $"SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = '{instanceId}' AND step = 'sum' AND error = '{error}'"));
        Assert.Equal(1L, await CountAsync(tenantDatabase, $"SELECT count(*) FROM axis.audit_records WHERE process_instance_id = '{instanceId}' AND action = 'process.failed'"));
        Assert.Equal(1L, await CountAsync(tenantDatabase, $"SELECT count(*) FROM axis.audit_records WHERE process_instance_id = '{instanceId}' AND action = 'operation.called' AND details->>'outcome' = 'failed'"));
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

    // The rows a result writes, so a test can show that a refused result changed none of them.
    private static async Task<string?> SnapshotAsync(string tenantDatabase, Guid callId, Guid instanceId, Guid orderId) =>
        string.Join(
            "|",
            await CallAsync(tenantDatabase, callId, "state || '/' || attempts || '/' || coalesce(settled_at::text, '') || '/' || coalesce(last_error, '')"),
            await InstanceAsync(tenantDatabase, instanceId, "state || '/' || step || '/' || revision"),
            await TotalOfAsync(tenantDatabase, orderId),
            await CountAsync(tenantDatabase, $"SELECT count(*) FROM axis.audit_records WHERE process_instance_id = '{instanceId}'"),
            await CountAsync(tenantDatabase, $"SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = '{instanceId}'"));

    private static async Task<string?> CallAsync(string tenantDatabase, Guid callId, string expression) =>
        await TextAsync(tenantDatabase, $"SELECT ({expression})::text FROM axis.package_calls WHERE id = '{callId}'");

    private static async Task<string?> InstanceAsync(string tenantDatabase, Guid instanceId, string expression) =>
        await TextAsync(tenantDatabase, $"SELECT ({expression})::text FROM axis.process_instances WHERE id = '{instanceId}'");

    private static async Task<string?> TotalOfAsync(string tenantDatabase, Guid orderId) =>
        await TextAsync(
            tenantDatabase,
            $"SELECT {EntityNaming.Quote(EntityNaming.Column("total"))}::text FROM {EntityNaming.QualifiedTable(EntityNaming.Table(OrderEntityId))} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = '{orderId}'");

    private static async Task<long> CountAsync(string tenantDatabase, string sql) =>
        Convert.ToInt64(await PostgreSqlServer.AdminScalarAsync(tenantDatabase, sql, CancellationToken), CultureInfo.InvariantCulture);

    private static async Task<string?> TextAsync(string tenantDatabase, string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(tenantDatabase, sql, CancellationToken), CultureInfo.InvariantCulture) is { Length: > 0 } text ? text : null;

    private static Guid OrderEntityId => CustomerHostedReleases.OrderEntityId;
}
