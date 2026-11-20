using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// A customer-hosted runner signs in with its client's token and registers its packages at <c>POST /api/package-runner/packages</c>, see
/// <c>docs/reference/package-runners.md#routes</c>, with real PostgreSQL and Keycloak. The checks that come before a route's work are
/// the same for <c>POST /api/package-runner/calls/take</c>, so its denied cases are here too. Each test makes the realms of its tenants with
/// unique names and deletes them at the end.
/// </summary>
public sealed class PackageRunnerRegistrationTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private const string Take = "/api/package-runner/calls/take";

    private static readonly string _sumManifest = CustomerHostedReleases.ToolsManifest("1.0.0", "sum");
    private static readonly string _multiplyManifest = CustomerHostedReleases.ToolsManifest("1.0.0", "multiply");

    private readonly PackageRunnerServer _server = new(database, keycloak);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task The_checks_run_in_order_unknown_host_then_the_token_and_the_runner_row_then_the_body()
    {
        await _server.PrepareAsync();
        var (id, tenantDatabase) = await _server.AddTenantAsync();
        var (otherId, _) = await _server.AddTenantAsync();
        try
        {
            await using var factory = _server.Start();
            using var admin = PackageRunnerServer.Client(factory, id).SignedInAs("admin");
            var good = await _server.CreateRunnerAsync(admin, id);
            var resigned = await _server.CreateRunnerAsync(admin, id);
            using var otherAdmin = PackageRunnerServer.Client(factory, otherId).SignedInAs("admin");
            var otherRealm = await _server.CreateRunnerAsync(otherAdmin, otherId);
            var deleted = await _server.CreateRunnerAsync(admin, id);
            var revoked = await _server.CreateRunnerAsync(admin, id);
            var expired = await _server.CreateRunnerAsync(admin, id);
            await _server.CreateClientAsync(id, "wrong-audience-client", "wrong-audience-secret", PackageRunnerServer.ServiceAudience);
            var wrongAudience = await _server.TokenAsync(id, "wrong-audience-client", "wrong-audience-secret");

            // A token from the realm, so only the check under test refuses it. The row of the runner is removed or revoked after the token is made.
            await PostgreSqlServer.AdminScalarAsync(tenantDatabase, $"DELETE FROM axis.package_runners WHERE id = '{deleted.Id}'", CancellationToken);
            using (var revoke = await PackageRunnerServer.SendAsync(admin, HttpMethod.Post, $"{PackageRunnerServer.Runners}/{revoked.Id}/revoke", csrf: true))
            {
                Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
            }

            var valid = PackageRunnerServer.RegistrationBody(_sumManifest);
            var statuses = new List<(string Case, HttpStatusCode Status)>();
            async Task Check(string name, string host, string? token, string? body, string contentType = "application/json", string route = PackageRunnerServer.Registration)
            {
                using var client = PackageRunnerServer.Client(factory, host);
                using var response = await PackageRunnerServer.PostAsync(client, route, token, body, contentType);
                statuses.Add((name, response.StatusCode));
            }

            await Check("unknown host", "unknown", good.Token, valid);
            await Check("no token", id, null, valid);
            await Check("bad signature", id, EndpointCallerTests.Resign(resigned.Token), valid);
            await Check("another realm", id, otherRealm.Token, valid);
            await Check("wrong audience", id, wrongAudience, valid);
            await Check("deleted row", id, deleted.Token, valid);
            await Check("revoked runner", id, revoked.Token, valid);
            await Check("revoked runner with a bad body", id, revoked.Token, "{}");

            _server.Clock.Advance(TimeSpan.FromHours(1));
            await Check("expired token", id, expired.Token, valid);
            _server.Clock.Advance(TimeSpan.FromHours(-1));

            await Check("no body", id, good.Token, null);
            await Check("not JSON", id, good.Token, "not json");
            await Check("not a JSON content type", id, good.Token, valid, "text/plain");
            await Check("no packages property", id, good.Token, "{}");
            await Check("empty packages", id, good.Token, """{ "packages": [] }""");
            await Check("packages is not a list", id, good.Token, """{ "packages": {} }""");
            await Check("no manifest", id, good.Token, """{ "packages": [{}] }""");
            await Check("no version", id, good.Token, """{ "packages": [{ "manifest": { "id": "tools" } }] }""");
            await Check("blank id", id, good.Token, """{ "packages": [{ "manifest": { "id": "", "version": "1.0.0" } }] }""");

            // The take route is checked in the same order, and reads its body as { "packages": [{ "id", "version" }] }.
            const string TakeBody = """{ "packages": [{ "id": "tools", "version": "1.0.0" }] }""";
            await Check("take: unknown host", "unknown", good.Token, TakeBody, route: Take);
            await Check("take: no token", id, null, TakeBody, route: Take);
            await Check("take: revoked runner", id, revoked.Token, TakeBody, route: Take);
            await Check("take: revoked runner with a bad body", id, revoked.Token, "{}", route: Take);
            await Check("take: no body", id, good.Token, null, route: Take);
            await Check("take: not JSON", id, good.Token, "not json", route: Take);
            await Check("take: not a JSON content type", id, good.Token, TakeBody, "text/plain", Take);
            await Check("take: no packages property", id, good.Token, "{}", route: Take);
            await Check("take: empty packages", id, good.Token, """{ "packages": [] }""", route: Take);
            await Check("take: packages is not a list", id, good.Token, """{ "packages": {} }""", route: Take);
            await Check("take: an item that is not an object", id, good.Token, """{ "packages": ["tools"] }""", route: Take);
            await Check("take: no version", id, good.Token, """{ "packages": [{ "id": "tools" }] }""", route: Take);
            await Check("take: blank id", id, good.Token, """{ "packages": [{ "id": "", "version": "1.0.0" }] }""", route: Take);
            await Check("take: a version that is not text", id, good.Token, """{ "packages": [{ "id": "tools", "version": 1 }] }""", route: Take);

            // The result route is checked in the same order, and reads { "outcome": "succeeded", "output": {...} }, { "outcome": "retryable" } or { "outcome": "failed", "error": "..." }. Only the lease decides the 404.
            var result = $"/api/package-runner/leases/{Guid.NewGuid()}/result";
            const string ResultBody = """{ "outcome": "succeeded", "output": { "total": "5" } }""";
            await Check("result: unknown host", "unknown", good.Token, ResultBody, route: result);
            await Check("result: no token", id, null, ResultBody, route: result);
            await Check("result: revoked runner", id, revoked.Token, ResultBody, route: result);
            await Check("result: revoked runner with a bad body", id, revoked.Token, "{}", route: result);
            await Check("result: no body", id, good.Token, null, route: result);
            await Check("result: not JSON", id, good.Token, "not json", route: result);
            await Check("result: not a JSON content type", id, good.Token, ResultBody, "text/plain", result);
            await Check("result: no outcome", id, good.Token, """{ "output": {} }""", route: result);
            await Check("result: an unknown outcome", id, good.Token, """{ "outcome": "done" }""", route: result);
            await Check("result: failed with no error", id, good.Token, """{ "outcome": "failed" }""", route: result);
            await Check("result: no output", id, good.Token, """{ "outcome": "succeeded" }""", route: result);
            await Check("result: an output that is not an object", id, good.Token, """{ "outcome": "succeeded", "output": 5 }""", route: result);
            await Check("result: a lease that is not known", id, good.Token, ResultBody, route: result);

            Assert.Equal(
                [
                    ("unknown host", HttpStatusCode.NotFound),
                    ("no token", HttpStatusCode.Unauthorized),
                    ("bad signature", HttpStatusCode.Unauthorized),
                    ("another realm", HttpStatusCode.Unauthorized),
                    ("wrong audience", HttpStatusCode.Unauthorized),
                    ("deleted row", HttpStatusCode.Unauthorized),
                    ("revoked runner", HttpStatusCode.Unauthorized),
                    ("revoked runner with a bad body", HttpStatusCode.Unauthorized),
                    ("expired token", HttpStatusCode.Unauthorized),
                    ("no body", HttpStatusCode.BadRequest),
                    ("not JSON", HttpStatusCode.BadRequest),
                    ("not a JSON content type", HttpStatusCode.BadRequest),
                    ("no packages property", HttpStatusCode.BadRequest),
                    ("empty packages", HttpStatusCode.BadRequest),
                    ("packages is not a list", HttpStatusCode.BadRequest),
                    ("no manifest", HttpStatusCode.BadRequest),
                    ("no version", HttpStatusCode.BadRequest),
                    ("blank id", HttpStatusCode.BadRequest),
                    ("take: unknown host", HttpStatusCode.NotFound),
                    ("take: no token", HttpStatusCode.Unauthorized),
                    ("take: revoked runner", HttpStatusCode.Unauthorized),
                    ("take: revoked runner with a bad body", HttpStatusCode.Unauthorized),
                    ("take: no body", HttpStatusCode.BadRequest),
                    ("take: not JSON", HttpStatusCode.BadRequest),
                    ("take: not a JSON content type", HttpStatusCode.BadRequest),
                    ("take: no packages property", HttpStatusCode.BadRequest),
                    ("take: empty packages", HttpStatusCode.BadRequest),
                    ("take: packages is not a list", HttpStatusCode.BadRequest),
                    ("take: an item that is not an object", HttpStatusCode.BadRequest),
                    ("take: no version", HttpStatusCode.BadRequest),
                    ("take: blank id", HttpStatusCode.BadRequest),
                    ("take: a version that is not text", HttpStatusCode.BadRequest),
                    ("result: unknown host", HttpStatusCode.NotFound),
                    ("result: no token", HttpStatusCode.Unauthorized),
                    ("result: revoked runner", HttpStatusCode.Unauthorized),
                    ("result: revoked runner with a bad body", HttpStatusCode.Unauthorized),
                    ("result: no body", HttpStatusCode.BadRequest),
                    ("result: not JSON", HttpStatusCode.BadRequest),
                    ("result: not a JSON content type", HttpStatusCode.BadRequest),
                    ("result: no outcome", HttpStatusCode.BadRequest),
                    ("result: an unknown outcome", HttpStatusCode.BadRequest),
                    ("result: failed with no error", HttpStatusCode.BadRequest),
                    ("result: no output", HttpStatusCode.BadRequest),
                    ("result: an output that is not an object", HttpStatusCode.BadRequest),
                    ("result: a lease that is not known", HttpStatusCode.NotFound),
                ],
                statuses);
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
            await _server.DeleteRealmAsync(otherId);
        }
    }

    [Fact]
    public async Task A_service_account_token_is_a_401_on_the_runner_route_and_a_runner_token_is_a_401_on_a_record_route()
    {
        await _server.PrepareAsync();
        var (id, _) = await _server.AddTenantAsync(PackageRunnerServer.ServiceAudience);
        try
        {
            await using var factory = _server.Start();
            using var admin = PackageRunnerServer.Client(factory, id).SignedInAs("admin");
            var runner = await _server.CreateRunnerAsync(admin, id);
            await _server.CreateClientAsync(id, "service-client", "service-secret", PackageRunnerServer.ServiceAudience);
            var service = await _server.TokenAsync(id, "service-client", "service-secret");
            using var client = PackageRunnerServer.Client(factory, id);
            const string RecordRoute = "/api/apps/purchasing/entities/request/records";

            using var serviceOnRunnerRoute = await PackageRunnerServer.RegisterAsync(client, service, _sumManifest);
            using var serviceOnTake = await PackageRunnerServer.PostAsync(
                client, Take, service, """{ "packages": [{ "id": "tools", "version": "1.0.0" }] }""");
            using var serviceOnResult = await PackageRunnerServer.PostAsync(
                client, $"/api/package-runner/leases/{Guid.NewGuid()}/result", service, """{ "outcome": "succeeded", "output": {} }""");
            using var runnerOnRecordRoute = await PackageRunnerServer.GetAsync(client, RecordRoute, runner.Token);
            using var serviceOnRecordRoute = await PackageRunnerServer.GetAsync(client, RecordRoute, service);

            Assert.Equal(HttpStatusCode.Unauthorized, serviceOnRunnerRoute.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, serviceOnTake.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, serviceOnResult.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, runnerOnRecordRoute.StatusCode);
            Assert.NotEqual(HttpStatusCode.Unauthorized, serviceOnRecordRoute.StatusCode);
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_manifest_that_differs_from_the_pin_or_that_no_release_pins_is_a_409_that_names_the_package_and_stores_nothing()
    {
        await _server.PrepareAsync();
        var (id, tenantDatabase) = await _server.AddTenantAsync();
        try
        {
            await new CustomerHostedReleases().ActivateOwnAsync(tenantDatabase, _sumManifest, CancellationToken);
            await using var factory = _server.Start();
            using var admin = PackageRunnerServer.Client(factory, id).SignedInAs("admin");
            var runner = await _server.CreateRunnerAsync(admin, id);
            using var client = PackageRunnerServer.Client(factory, id);

            using var different = await PackageRunnerServer.RegisterAsync(client, runner.Token, _multiplyManifest);
            using var otherVersion = await PackageRunnerServer.RegisterAsync(client, runner.Token, CustomerHostedReleases.ToolsManifest("1.0.1", "sum"));
            using var mixed = await PackageRunnerServer.RegisterAsync(client, runner.Token, _sumManifest, _multiplyManifest);

            Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, otherVersion.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, mixed.StatusCode);
            var differentTitle = await TitleAsync(different);
            var otherVersionTitle = await TitleAsync(otherVersion);
            Assert.Contains("'tools' version 1.0.0", differentTitle, StringComparison.Ordinal);
            Assert.Contains("'tools' version 1.0.1", otherVersionTitle, StringComparison.Ordinal);
            Assert.Contains("'tools' version 1.0.0", await TitleAsync(mixed), StringComparison.Ordinal);
            Assert.Equal([], await StoredPackagesAsync(admin));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_manifest_equal_to_the_customer_hosted_pin_is_a_204_that_the_list_shows_once_even_after_a_repeat()
    {
        await _server.PrepareAsync();
        var (id, tenantDatabase) = await _server.AddTenantAsync();
        try
        {
            await new CustomerHostedReleases().ActivateOwnAsync(tenantDatabase, _sumManifest, CancellationToken);
            await using var factory = _server.Start();
            using var admin = PackageRunnerServer.Client(factory, id).SignedInAs("admin");
            var runner = await _server.CreateRunnerAsync(admin, id);
            using var client = PackageRunnerServer.Client(factory, id);

            using var first = await PackageRunnerServer.RegisterAsync(client, runner.Token, _sumManifest);
            using var repeated = await PackageRunnerServer.RegisterAsync(client, runner.Token, _sumManifest, _sumManifest);

            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
            Assert.Equal([("tools", "1.0.0")], await StoredPackagesAsync(admin));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    private static async Task<string> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("title").GetString()!;

    // The packages of the administrator's one runner list, as id and version.
    private static async Task<List<(string Id, string Version)>> StoredPackagesAsync(HttpClient admin)
    {
        using var response = await PackageRunnerServer.SendAsync(admin, HttpMethod.Get, PackageRunnerServer.Runners, csrf: false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        return
        [
            .. items.EnumerateArray()
                .SelectMany(item => item.GetProperty("packages").EnumerateArray())
                .Select(package => (package.GetProperty("id").GetString()!, package.GetProperty("version").GetString()!)),
        ];
    }
}
