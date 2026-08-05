using System.Net;
using System.Net.Sockets;
using System.Text;
using Axis.Mcp.Authentication;

namespace Axis.Mcp.Tests;

[CollectionDefinition(nameof(OAuthLoopbackCollection), DisableParallelization = true)]
public sealed class OAuthLoopbackCollection;

[Collection(nameof(OAuthLoopbackCollection))]
public sealed class LoopbackAuthorizationListenerTests
{
    [Fact]
    public async Task Listener_WhenStateMatches_ReturnsTheAuthorizationCode()
    {
        int port = GetFreePort();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        LoopbackAuthorizationListener listener = new(port);
        Task<string> callback = listener.WaitForCodeAsync("expected-state", timeout.Token);

        await SendCallbackAsync(
            port,
            "/callback?code=auth%2Bcode&state=expected-state",
            timeout.Token);

        Assert.Equal("auth+code", await callback);
    }

    [Fact]
    public async Task Listener_WhenStateDiffers_RejectsTheCallback()
    {
        int port = GetFreePort();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        LoopbackAuthorizationListener listener = new(port);
        Task<string> callback = listener.WaitForCodeAsync("expected-state", timeout.Token);

        await SendCallbackAsync(
            port,
            "/callback?code=auth-code&state=wrong-state",
            timeout.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => callback);
    }

    private static int GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static async Task SendCallbackAsync(
        int port,
        string target,
        CancellationToken cancellationToken)
    {
        using TcpClient client = new();
        await ConnectWithRetryAsync(client, port, cancellationToken);
        await using NetworkStream stream = client.GetStream();
        byte[] request = Encoding.ASCII.GetBytes(
            $"GET {target} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(request, cancellationToken);

        byte[] response = new byte[4 * 1024];
        while (await stream.ReadAsync(response, cancellationToken) > 0)
        {
        }
    }

    private static async Task ConnectWithRetryAsync(
        TcpClient client,
        int port,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
                return;
            }
            catch (SocketException) when (attempt < 49)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
            }
        }

        throw new InvalidOperationException("The test callback listener did not start.");
    }
}
