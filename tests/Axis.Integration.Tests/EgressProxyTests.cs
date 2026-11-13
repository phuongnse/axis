using System.Net;
using System.Net.Sockets;
using System.Text;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

/// <summary>
/// The worker's egress proxy on its own, with two stubs as upstream hosts, see
/// docs/reference/extension-packages.md#package-runners. The tests need no database. A request goes through
/// the proxy as a runner's client sends it, with the runner's token as the proxy credentials.
/// </summary>
public sealed class EgressProxyTests : IAsyncLifetime
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private readonly EgressProxy _proxy = new(0, allowHttp: true);
    private ConnectorStub _first = null!;
    private ConnectorStub _second = null!;

    public async ValueTask InitializeAsync()
    {
        _first = await ConnectorStub.StartAsync();
        _second = await ConnectorStub.StartAsync();
        _proxy.EnsureStarted();
    }

    public async ValueTask DisposeAsync()
    {
        await _proxy.DisposeAsync();
        await _first.DisposeAsync();
        await _second.DisposeAsync();
    }

    [Fact]
    public async Task A_runner_reaches_the_hosts_of_its_own_call_and_never_those_of_another_runners_call()
    {
        var a = _proxy.Register();
        var b = _proxy.Register();
        _proxy.Admit(a, [new Uri(_first.BaseUrl)]);
        _proxy.Admit(b, [new Uri(_second.BaseUrl)]);

        using (var refused = await GetAsync(a, _second.BaseUrl))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        Assert.Empty(_second.Requests);
        using (var own = await GetAsync(a, _first.BaseUrl))
        {
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        }

        using (var other = await GetAsync(b, _second.BaseUrl))
        {
            Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        }

        Assert.Contains("axis-egress-proxy", Assert.Single(_first.Requests).Headers!["Via"], StringComparison.Ordinal);
        Assert.Single(_second.Requests);

        // Only the runner whose request was refused is marked.
        Assert.True(_proxy.EndCall(a));
        Assert.False(_proxy.EndCall(b));
    }

    [Fact]
    public async Task A_runner_reaches_nothing_before_its_call_is_admitted_and_after_it_ends()
    {
        var token = _proxy.Register();

        using (var idle = await GetAsync(token, _first.BaseUrl))
        {
            Assert.Equal(HttpStatusCode.Forbidden, idle.StatusCode);
        }

        _proxy.Admit(token, [new Uri(_first.BaseUrl)]);
        using (var admitted = await GetAsync(token, _first.BaseUrl))
        {
            Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        }

        Assert.False(_proxy.EndCall(token));
        using (var ended = await GetAsync(token, _first.BaseUrl))
        {
            Assert.Equal(HttpStatusCode.Forbidden, ended.StatusCode);
        }

        // A new call starts clean.
        _proxy.Admit(token, [new Uri(_first.BaseUrl)]);
        Assert.False(_proxy.EndCall(token));
        Assert.Single(_first.Requests);
    }

    [Fact]
    public async Task A_request_with_no_known_token_gets_407_and_reaches_nothing()
    {
        var known = _proxy.Register();
        _proxy.Admit(known, [new Uri(_first.BaseUrl)]);

        using var anonymous = await GetAsync(null, _first.BaseUrl);
        using var unknown = await GetAsync("not-a-token", _first.BaseUrl);
        _proxy.Remove(known);
        using var removed = await GetAsync(known, _first.BaseUrl);

        Assert.Equal(HttpStatusCode.ProxyAuthenticationRequired, anonymous.StatusCode);
        Assert.Contains(anonymous.Headers.ProxyAuthenticate, challenge => challenge.Scheme == "Basic");
        Assert.Equal(HttpStatusCode.ProxyAuthenticationRequired, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.ProxyAuthenticationRequired, removed.StatusCode);
        Assert.Empty(_first.Requests);
    }

    [Fact]
    public async Task A_request_to_the_declared_host_with_another_port_or_scheme_is_refused()
    {
        var token = _proxy.Register();
        _proxy.Admit(token, [new Uri(_first.BaseUrl)]);
        var other = new UriBuilder(_first.BaseUrl) { Port = ((IPEndPoint)Reserved()).Port }.Uri.AbsoluteUri;

        using var port = await GetAsync(token, other);

        // An https request opens a tunnel first, and the client reports the 403 that refuses it as an error.
        var scheme = await Assert.ThrowsAsync<HttpRequestException>(() => GetAsync(token, _first.BaseUrl.Replace("http://", "https://", StringComparison.Ordinal)));

        Assert.Equal(HttpStatusCode.Forbidden, port.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, scheme.StatusCode);
        Assert.Empty(_first.Requests);
        Assert.True(_proxy.EndCall(token));
    }

    [Fact]
    public async Task Plain_http_is_refused_unless_the_proxy_allows_it()
    {
        await using var strict = new EgressProxy(0, allowHttp: false);
        strict.EnsureStarted();
        var token = strict.Register();
        strict.Admit(token, [new Uri(_first.BaseUrl)]);

        using var response = await GetAsync(token, _first.BaseUrl, strict.Port);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_first.Requests);
        Assert.True(strict.EndCall(token));
    }

    [Fact]
    public async Task A_tunnel_opens_to_a_declared_https_host_and_carries_the_bytes()
    {
        using var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        var port = ((IPEndPoint)upstream.LocalEndpoint).Port;
        var token = _proxy.Register();
        _proxy.Admit(token, [new Uri($"https://127.0.0.1:{port}")]);

        using var client = await ConnectTunnelAsync(token, $"127.0.0.1:{port}");
        var stream = client.GetStream();
        var status = await ReadLineAsync(stream);
        await stream.WriteAsync("ping"u8.ToArray(), CancellationToken);

        using var accepted = await upstream.AcceptTcpClientAsync(CancellationToken);
        var received = new byte[4];
        await accepted.GetStream().ReadExactlyAsync(received, CancellationToken);
        Assert.StartsWith("HTTP/1.1 200", status, StringComparison.Ordinal);
        Assert.Equal("ping", Encoding.ASCII.GetString(received));
        Assert.False(_proxy.EndCall(token));
    }

    [Fact]
    public async Task A_tunnel_to_a_host_that_only_http_declares_or_that_nothing_declares_is_refused()
    {
        var token = _proxy.Register();
        _proxy.Admit(token, [new Uri(_first.BaseUrl)]);
        var port = new Uri(_first.BaseUrl).Port;

        using var plain = await ConnectTunnelAsync(token, $"127.0.0.1:{port}");
        using var undeclared = await ConnectTunnelAsync(token, "example.test:443");

        Assert.StartsWith("HTTP/1.1 403", await ReadLineAsync(plain.GetStream()), StringComparison.Ordinal);
        Assert.StartsWith("HTTP/1.1 403", await ReadLineAsync(undeclared.GetStream()), StringComparison.Ordinal);
        Assert.True(_proxy.EndCall(token));
    }

    private async Task<HttpResponseMessage> GetAsync(string? token, string url, int? proxyPort = null)
    {
        var proxy = new WebProxy($"http://127.0.0.1:{proxyPort ?? _proxy.Port}") { Credentials = token is null ? null : new NetworkCredential("axis", token) };
        using var handler = new HttpClientHandler { Proxy = proxy, UseProxy = true };
        using var client = new HttpClient(handler);
        return await client.GetAsync(new Uri($"{url}/probe"), CancellationToken);
    }

    private async Task<TcpClient> ConnectTunnelAsync(string token, string target)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _proxy.Port, CancellationToken);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"axis:{token}"));
        var request = $"CONNECT {target} HTTP/1.1\r\nHost: {target}\r\nProxy-Authorization: Basic {credentials}\r\n\r\n";
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(request), CancellationToken);
        return client;
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream)
    {
        var line = new StringBuilder();
        var buffer = new byte[1];
        while (await stream.ReadAsync(buffer, CancellationToken) == 1 && buffer[0] != '\n')
        {
            line.Append((char)buffer[0]);
        }

        return line.ToString().TrimEnd('\r');
    }

    /// <summary>A port that nothing listens on, from the one the OS hands out.</summary>
    private static EndPoint Reserved()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return listener.LocalEndpoint;
    }
}
