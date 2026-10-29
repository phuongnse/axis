using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Realms;
using Axis.Tenancy.Realms;

namespace Axis.Tenancy.Tests.Realms;

public sealed class RealmSyncTests
{
    private const string ClientsPath = "/admin/realms/r/clients?max=1000";
    private const string ClientUpdatePath = "/admin/realms/r/clients/u1";
    private const string MappersPath = "/admin/realms/r/clients/u1/protocol-mappers/models";

    private const string ClientsWithRoles = """
        [
          {"id":"u0","clientId":"axis","protocolMappers":[
            {"id":"p1","name":"roles","protocol":"openid-connect","protocolMapper":"oidc-usermodel-client-role-mapper","config":{"claim.name":"roles","multivalued":"true"}},
            {"id":"p2","name":"identity provider","protocol":"openid-connect","protocolMapper":"oidc-hardcoded-claim-mapper","config":{"claim.name":"x"}}
          ]},
          {"id":"u1","clientId":"site-a"}
        ]
        """;

    [Fact]
    public async Task A_conflict_on_a_client_update_reads_the_clients_again_and_retries()
    {
        using var handler = new ScriptedHandler(HttpStatusCode.Conflict, HttpStatusCode.NoContent);

        await Sync(handler);

        Assert.Equal(2, handler.Count("PUT", ClientUpdatePath));
        Assert.Equal(2, handler.Count("GET", ClientsPath));
    }

    [Fact]
    public async Task A_conflict_on_every_attempt_fails_after_three_updates()
    {
        using var handler = new ScriptedHandler(HttpStatusCode.Conflict, HttpStatusCode.Conflict, HttpStatusCode.Conflict, HttpStatusCode.NoContent);

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Sync(handler));

        Assert.Contains("status 409", exception.Message, StringComparison.Ordinal);
        Assert.Contains("update the client 'site-a'", exception.Message, StringComparison.Ordinal);
        Assert.Equal(3, handler.Count("PUT", ClientUpdatePath));
    }

    [Fact]
    public async Task A_forbidden_client_update_fails_at_once_without_a_retry()
    {
        using var handler = new ScriptedHandler(HttpStatusCode.Forbidden, HttpStatusCode.NoContent);

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Sync(handler));

        Assert.Contains("status 403", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.Count("PUT", ClientUpdatePath));
    }

    [Fact]
    public async Task A_mapper_of_the_platform_client_that_the_site_client_lacks_is_created()
    {
        using var handler = new ScriptedHandler(clients: ClientsWithRoles, siteMappers: "[]");

        await Sync(handler);

        var created = Assert.Single(handler.Requests("POST", MappersPath), request => MapperName(request.Body) == "roles");
        using var body = JsonDocument.Parse(created.Body!);
        Assert.Equal("oidc-usermodel-client-role-mapper", body.RootElement.GetProperty("protocolMapper").GetString());
        Assert.Equal("openid-connect", body.RootElement.GetProperty("protocol").GetString());
        Assert.Equal("roles", body.RootElement.GetProperty("config").GetProperty("claim.name").GetString());
        Assert.Equal("true", body.RootElement.GetProperty("config").GetProperty("multivalued").GetString());
    }

    [Fact]
    public async Task A_copy_with_a_different_config_is_updated_to_the_config_of_the_platform_client()
    {
        using var handler = new ScriptedHandler(
            clients: ClientsWithRoles,
            siteMappers: """[{"id":"m1","name":"roles","protocol":"openid-connect","protocolMapper":"oidc-usermodel-client-role-mapper","config":{"claim.name":"old","multivalued":"true"}}]""");

        await Sync(handler);

        Assert.DoesNotContain(handler.Requests("POST", MappersPath), request => MapperName(request.Body) == "roles");
        var updated = Assert.Single(handler.Requests("PUT", MappersPath + "/m1"));
        using var body = JsonDocument.Parse(updated.Body!);
        Assert.Equal("m1", body.RootElement.GetProperty("id").GetString());
        Assert.Equal("roles", body.RootElement.GetProperty("config").GetProperty("claim.name").GetString());
    }

    [Fact]
    public async Task A_matching_copy_is_left_as_it_is()
    {
        using var handler = new ScriptedHandler(
            clients: ClientsWithRoles,
            siteMappers: """[{"id":"m1","name":"roles","protocol":"openid-connect","protocolMapper":"oidc-usermodel-client-role-mapper","config":{"claim.name":"roles","multivalued":"true"}}]""");

        await Sync(handler);

        Assert.DoesNotContain(handler.Requests("POST", MappersPath), request => MapperName(request.Body) == "roles");
        Assert.Empty(handler.Requests("PUT", MappersPath + "/m1"));
    }

    [Fact]
    public async Task A_copy_with_an_extra_config_key_is_updated()
    {
        using var handler = new ScriptedHandler(
            clients: ClientsWithRoles,
            siteMappers: """[{"id":"m1","name":"roles","protocol":"openid-connect","protocolMapper":"oidc-usermodel-client-role-mapper","config":{"claim.name":"roles","multivalued":"true","extra":"x"}}]""");

        await Sync(handler);

        Assert.Single(handler.Requests("PUT", MappersPath + "/m1"));
    }

    private static string? MapperName(string? body)
    {
        using var document = JsonDocument.Parse(body!);
        return document.RootElement.GetProperty("name").GetString();
    }

    private static async Task Sync(ScriptedHandler handler)
    {
        using var http = new HttpClient(handler, disposeHandler: false);
        await new RealmSync(http, new Uri("http://idp.test/")).SyncAsync("r", "s", new RealmState([new RealmSiteClient("a", Accounts: true, [])], []), CancellationToken.None);
    }

    private sealed class ScriptedHandler(
        HttpStatusCode[] clientUpdates,
        string clients = """[{"id":"u1","clientId":"site-a"}]""",
        string siteMappers = "[]") : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _clientUpdates = new(clientUpdates);
        private readonly List<(string Method, string Path, string? Body)> _requests = [];

        public ScriptedHandler(params HttpStatusCode[] clientUpdates)
            : this(clientUpdates, """[{"id":"u1","clientId":"site-a"}]""", "[]")
        {
        }

        public ScriptedHandler(string clients, string siteMappers)
            : this([HttpStatusCode.NoContent], clients, siteMappers)
        {
        }

        public int Count(string method, string path) => _requests.Count(request => request.Method == method && request.Path == path);

        public List<(string Method, string Path, string? Body)> Requests(string method, string path) =>
            [.. _requests.Where(request => request.Method == method && request.Path == path)];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            _requests.Add((request.Method.Method, path, request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult()));
            return Task.FromResult(Answer(request.Method.Method, path));
        }

        private HttpResponseMessage Answer(string method, string path) => (method, path) switch
        {
            ("POST", "/realms/r/protocol/openid-connect/token") => Json(HttpStatusCode.OK, """{"access_token":"t"}"""),
            ("GET", ClientsPath) => Json(HttpStatusCode.OK, clients),
            ("PUT", ClientUpdatePath) => new HttpResponseMessage(_clientUpdates.Dequeue()),
            ("GET", MappersPath) => Json(HttpStatusCode.OK, siteMappers),
            ("POST", MappersPath) => new HttpResponseMessage(HttpStatusCode.Created),
            ("PUT", MappersPath + "/m1") => new HttpResponseMessage(HttpStatusCode.NoContent),
            ("GET", "/admin/realms/r/identity-provider/instances") => Json(HttpStatusCode.OK, "[]"),
            ("GET", "/admin/realms/r/localization/en") => Json(HttpStatusCode.OK, "{}"),
            ("GET", "/admin/realms/r/localization/vi") => Json(HttpStatusCode.OK, "{}"),
            _ => throw new InvalidOperationException($"Unexpected request {method} {path}."),
        };

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
