using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Server.Tests;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A customer-hosted runner takes a waiting call at <c>POST /api/package-runner/calls/take</c>, see
/// <c>docs/reference/package-runners.md#routes</c>, with real PostgreSQL and Keycloak. The calls are left as staging leaves them, because
/// staging is tested with <see cref="PackageCallTests"/>. The denied cases are in <see cref="PackageRunnerRegistrationTests"/>. Each test makes
/// the realm of its tenant with a unique name and deletes it at the end.
/// </summary>
public sealed class PackageRunnerTakeTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private const string Take = "/api/package-runner/calls/take";
    private const string HoldsTools = """{ "packages": [{ "id": "tools", "version": "1.0.0" }] }""";

    private static readonly string _sumManifest = CustomerHostedReleases.ToolsManifest("1.0.0", "sum");

    private readonly PackageRunnerServer _server = new(database, keycloak);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task A_take_gives_the_lease_the_operation_and_the_input_as_a_run_message_writes_it_and_marks_the_call_sending()
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
            using (var registered = await PackageRunnerServer.RegisterAsync(client, runner.Token, _sumManifest))
            {
                Assert.Equal(HttpStatusCode.NoContent, registered.StatusCode);
            }

            Assert.Null(await LastCallAtAsync(admin));

            using var response = await PackageRunnerServer.PostAsync(client, Take, runner.Token, HoldsTools);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            Assert.Equal(
                ["input", "leaseId", "leaseUntil", "operation", "operationId", "package", "version"],
                body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.Equal(("tools", "1.0.0", "sum", callId), (
                body.GetProperty("package").GetString(),
                body.GetProperty("version").GetString(),
                body.GetProperty("operation").GetString(),
                body.GetProperty("operationId").GetGuid()));

            // An integer is a string, exactly as in a run message.
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "a": "2", "b": "3" }"""), JsonNode.Parse(body.GetProperty("input").GetRawText())));
            Assert.True(body.GetProperty("leaseUntil").GetDateTimeOffset() > DateTimeOffset.UtcNow);
            var leaseId = body.GetProperty("leaseId").GetGuid();
            Assert.Equal(7, leaseId.Version);
            Assert.Equal("sending", await ScalarAsync(tenantDatabase, callId, "state"));
            Assert.Equal("1", await ScalarAsync(tenantDatabase, callId, "attempts"));
            Assert.Equal(runner.Id, await RunnerOfAsync(tenantDatabase, callId));
            Assert.Equal(leaseId, await LeaseOfAsync(tenantDatabase, callId));
            Assert.Equal("true", await ScalarAsync(tenantDatabase, callId, "lease_until > now()"));
            Assert.NotNull(await LastCallAtAsync(admin));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_take_answers_204_when_the_only_waiting_call_is_of_a_package_or_version_the_runner_did_not_register_or_list()
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
            var unregistered = await _server.CreateRunnerAsync(admin, id);
            var registered = await _server.CreateRunnerAsync(admin, id);
            using var client = PackageRunnerServer.Client(factory, id);
            using (var response = await PackageRunnerServer.RegisterAsync(client, registered.Token, _sumManifest))
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }

            using var neverRegistered = await PackageRunnerServer.PostAsync(client, Take, unregistered.Token, HoldsTools);
            using var otherVersion = await PackageRunnerServer.PostAsync(
                client, Take, registered.Token, """{ "packages": [{ "id": "tools", "version": "1.0.1" }] }""");
            using var otherPackage = await PackageRunnerServer.PostAsync(
                client, Take, registered.Token, """{ "packages": [{ "id": "other", "version": "1.0.0" }] }""");

            Assert.Equal(HttpStatusCode.NoContent, neverRegistered.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, otherVersion.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, otherPackage.StatusCode);
            Assert.Equal("pending/0", $"{await ScalarAsync(tenantDatabase, callId, "state")}/{await ScalarAsync(tenantDatabase, callId, "attempts")}");
            Assert.Null(await LastCallAtAsync(admin));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task Two_takes_at_once_for_one_waiting_call_give_one_200_and_one_204()
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
            var first = await _server.CreateRunnerAsync(admin, id);
            var second = await _server.CreateRunnerAsync(admin, id);
            using var client = PackageRunnerServer.Client(factory, id);
            foreach (var runner in new[] { first, second })
            {
                using var response = await PackageRunnerServer.RegisterAsync(client, runner.Token, _sumManifest);
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }

            var responses = await Task.WhenAll(
                PackageRunnerServer.PostAsync(client, Take, first.Token, HoldsTools),
                PackageRunnerServer.PostAsync(client, Take, second.Token, HoldsTools));
            try
            {
                Assert.Equal([HttpStatusCode.OK, HttpStatusCode.NoContent], responses.Select(response => response.StatusCode).Order());
                Assert.Equal("1", await ScalarAsync(tenantDatabase, callId, "attempts"));
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_take_answers_204_without_waiting_while_another_transaction_holds_the_calls_row_lock_and_200_after_it_ends()
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
            using (var registered = await PackageRunnerServer.RegisterAsync(client, runner.Token, _sumManifest))
            {
                Assert.Equal(HttpStatusCode.NoContent, registered.StatusCode);
            }

            // The lock is held by a transaction of its own, so the take must skip the row instead of waiting for it.
            var holder = new NpgsqlConnectionStringBuilder(await PostgreSqlServer.AdminConnectionStringAsync())
            {
                Database = new NpgsqlConnectionStringBuilder(tenantDatabase).Database,
                Pooling = false,
            }.ConnectionString;
            await using (var connection = new NpgsqlConnection(holder))
            {
                await connection.OpenAsync(CancellationToken);
                await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
                await using (var lockRow = new NpgsqlCommand("SELECT 1 FROM axis.package_calls WHERE id = @id FOR UPDATE", connection, transaction))
                {
                    lockRow.Parameters.AddWithValue("id", callId);
                    await lockRow.ExecuteScalarAsync(CancellationToken);
                }

                using var held = await PackageRunnerServer.PostAsync(client, Take, runner.Token, HoldsTools);

                Assert.Equal(HttpStatusCode.NoContent, held.StatusCode);
                await transaction.RollbackAsync(CancellationToken);
            }

            using var released = await PackageRunnerServer.PostAsync(client, Take, runner.Token, HoldsTools);

            Assert.Equal(HttpStatusCode.OK, released.StatusCode);
            Assert.Equal("sending", await ScalarAsync(tenantDatabase, callId, "state"));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    // The tenant's one runner list, which the administrator reads.
    private static async Task<JsonElement?> LastCallAtAsync(HttpClient admin)
    {
        using var response = await PackageRunnerServer.SendAsync(admin, HttpMethod.Get, PackageRunnerServer.Runners, csrf: false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        var lastCallAt = items.EnumerateArray().First().GetProperty("lastCallAt");
        return lastCallAt.ValueKind == JsonValueKind.Null ? null : lastCallAt.Clone();
    }

    private static async Task<string?> ScalarAsync(string tenantDatabase, Guid callId, string expression) =>
        Convert.ToString(
            await PostgreSqlServer.AdminScalarAsync(
                tenantDatabase, $"SELECT ({expression})::text FROM axis.package_calls WHERE id = '{callId}'", CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<Guid?> RunnerOfAsync(string tenantDatabase, Guid callId) =>
        Guid.TryParse(await ScalarAsync(tenantDatabase, callId, "runner_id"), out var runner) ? runner : null;

    private static async Task<Guid?> LeaseOfAsync(string tenantDatabase, Guid callId) =>
        Guid.TryParse(await ScalarAsync(tenantDatabase, callId, "lease_id"), out var lease) ? lease : null;
}
