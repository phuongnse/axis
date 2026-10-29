using System.Net;
using System.Text;
using Axis.Configuration.Realms;
using Axis.Tenancy.Realms;

namespace Axis.Tenancy.Tests.Realms;

public sealed class RealmSyncTests
{
    private const string ClientsPath = "/admin/realms/r/clients?max=1000";
    private const string ClientUpdatePath = "/admin/realms/r/clients/u1";

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

    private static async Task Sync(ScriptedHandler handler)
    {
        using var http = new HttpClient(handler, disposeHandler: false);
        await new RealmSync(http, new Uri("http://idp.test/")).SyncAsync("r", "s", new RealmState([new RealmSiteClient("a", Accounts: true, [])], []), CancellationToken.None);
    }

    private sealed class ScriptedHandler(params HttpStatusCode[] clientUpdates) : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _clientUpdates = new(clientUpdates);
        private readonly List<(string Method, string Path)> _requests = [];

        public int Count(string method, string path) => _requests.Count(request => request.Method == method && request.Path == path);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            _requests.Add((request.Method.Method, path));
            return Task.FromResult(Answer(request.Method.Method, path));
        }

        private HttpResponseMessage Answer(string method, string path) => (method, path) switch
        {
            ("POST", "/realms/r/protocol/openid-connect/token") => Json(HttpStatusCode.OK, """{"access_token":"t"}"""),
            ("GET", ClientsPath) => Json(HttpStatusCode.OK, """[{"id":"u1","clientId":"site-a"}]"""),
            ("PUT", ClientUpdatePath) => new HttpResponseMessage(_clientUpdates.Dequeue()),
            ("GET", "/admin/realms/r/clients/u1/protocol-mappers/models") => Json(HttpStatusCode.OK, "[]"),
            ("POST", "/admin/realms/r/clients/u1/protocol-mappers/models") => new HttpResponseMessage(HttpStatusCode.Created),
            ("GET", "/admin/realms/r/identity-provider/instances") => Json(HttpStatusCode.OK, "[]"),
            ("GET", "/admin/realms/r/localization/en") => Json(HttpStatusCode.OK, "{}"),
            ("GET", "/admin/realms/r/localization/vi") => Json(HttpStatusCode.OK, "{}"),
            _ => throw new InvalidOperationException($"Unexpected request {method} {path}."),
        };

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
