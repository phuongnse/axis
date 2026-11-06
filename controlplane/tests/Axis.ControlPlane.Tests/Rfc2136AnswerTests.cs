using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Axis.ControlPlane.Dns;

namespace Axis.ControlPlane.Tests;

/// <summary>The answers of a DNS server that the real server in the integration tests never gives.</summary>
public sealed class Rfc2136AnswerTests
{
    private const string Key = "c2VjcmV0LWtleS1ieXRlcy0xMjM0NTY3ODkwMTIzNDU2";

    [Fact]
    public async Task A_success_answer_with_no_signature_is_a_failure()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var served = ServeAsync(listener, rcode: 0);
        var records = NewProvider(((IPEndPoint)listener.LocalEndpoint).Port);

        var exception = await Assert.ThrowsAsync<DnsUpdateException>(
            () => records.SetCnameAsync("acme.vn.example.com", "ingress.vn-1.vn.example.com", TestContext.Current.CancellationToken));

        await served;
        Assert.Contains("without a signature", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_names_the_answer_code_of_the_server()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var served = ServeAsync(listener, rcode: 5);
        var records = NewProvider(((IPEndPoint)listener.LocalEndpoint).Port);

        var exception = await Assert.ThrowsAsync<DnsUpdateException>(
            () => records.RemoveCnameAsync("acme.vn.example.com", TestContext.Current.CancellationToken));

        await served;
        Assert.Contains("REFUSED", exception.Message, StringComparison.Ordinal);
        Assert.Contains("acme.vn.example.com", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_server_that_does_not_listen_is_a_failure_with_no_key_in_the_error()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var records = NewProvider(port);

        var exception = await Assert.ThrowsAsync<DnsUpdateException>(
            () => records.RemoveCnameAsync("acme.vn.example.com", TestContext.Current.CancellationToken));

        Assert.DoesNotContain(Key, exception.ToString(), StringComparison.Ordinal);
    }

    private static Rfc2136DnsRecords NewProvider(int port) =>
        new(
            new ControlPlaneDnsOptions
            {
                Provider = DnsProviders.Rfc2136,
                Server = "127.0.0.1",
                Port = port,
                Zone = "vn.example.com",
                KeyName = "axis-controlplane",
                Key = Key,
            },
            TimeProvider.System);

    // Reads one update and answers it with the same id, the response flag, the update opcode and the given answer code.
    private static async Task ServeAsync(TcpListener listener, int rcode)
    {
        using var client = await listener.AcceptTcpClientAsync();
        var stream = client.GetStream();
        var length = new byte[2];
        await stream.ReadExactlyAsync(length);
        var request = new byte[BinaryPrimitives.ReadUInt16BigEndian(length)];
        await stream.ReadExactlyAsync(request);

        var answer = new byte[14];
        BinaryPrimitives.WriteUInt16BigEndian(answer, 12);
        request.AsSpan(0, 2).CopyTo(answer.AsSpan(2));
        BinaryPrimitives.WriteUInt16BigEndian(answer.AsSpan(4), (ushort)(0x8000 | (5 << 11) | rcode));
        await stream.WriteAsync(answer);
    }
}
