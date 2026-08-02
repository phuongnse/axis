using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Axis.Mcp.Authentication;

public sealed class LoopbackAuthorizationListener
{
    private readonly int _port;

    public LoopbackAuthorizationListener(int port)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));

        _port = port;
    }

    public async Task<string> WaitForCodeAsync(
        string expectedState,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedState);

        TcpListener listener = new(IPAddress.Loopback, _port);
        try
        {
            listener.Start();
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            using NetworkStream stream = client.GetStream();

            string request = await ReadRequestAsync(stream, cancellationToken);
            Uri callbackUri = ParseCallbackUri(request);
            string? state = GetQueryValue(callbackUri, "state");
            string? code = GetQueryValue(callbackUri, "code");

            if (!string.Equals(state, expectedState, StringComparison.Ordinal))
            {
                await WriteResponseAsync(stream, 400, "Authorization state did not match.", cancellationToken);
                throw new InvalidOperationException("The OAuth authorization state did not match.");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                await WriteResponseAsync(stream, 400, "Authorization did not return a code.", cancellationToken);
                throw new InvalidOperationException("The OAuth authorization response did not contain a code.");
            }

            await WriteResponseAsync(stream, 200, "Axis authorization completed. You can close this tab.", cancellationToken);
            return code;
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                $"Could not bind the local OAuth callback on 127.0.0.1:{_port}.",
                ex);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        int read = 0;

        while (read < buffer.Length)
        {
            int count = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken);
            if (count == 0)
                break;

            read += count;
            if (buffer.AsSpan(0, read).IndexOf("\r\n\r\n"u8) >= 0)
                break;
        }

        if (read == 0)
            throw new InvalidOperationException("The local OAuth callback returned an empty request.");

        string request = Encoding.ASCII.GetString(buffer, 0, read);
        string requestLine = request.Split("\r\n", 2, StringSplitOptions.None)[0];
        if (!requestLine.StartsWith("GET ", StringComparison.Ordinal) ||
            !requestLine.EndsWith(" HTTP/1.1", StringComparison.Ordinal))
            throw new InvalidOperationException("The local OAuth callback used an unsupported HTTP request.");

        return requestLine[4..^9];
    }

    private static Uri ParseCallbackUri(string requestTarget)
    {
        if (!Uri.TryCreate($"http://127.0.0.1{requestTarget}", UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.AbsolutePath, "/callback", StringComparison.Ordinal))
            throw new InvalidOperationException("The local OAuth callback path was invalid.");

        return uri;
    }

    private static string? GetQueryValue(Uri uri, string key)
    {
        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), key, StringComparison.Ordinal))
                return Uri.UnescapeDataString(parts[1].Replace('+', ' '));
        }

        return null;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int statusCode,
        string body,
        CancellationToken cancellationToken)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes($"<!doctype html><title>Axis</title><p>{body}</p>");
        string headers =
            $"HTTP/1.1 {statusCode} {(statusCode == 200 ? "OK" : "Bad Request")}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";

        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
    }
}
