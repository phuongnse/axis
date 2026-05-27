using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axis.Api.Tests.Helpers;

/// <summary>
/// Minimal in-process implementation of the Confluent Schema Registry REST API,
/// backed by <see cref="HttpListener"/> so it is completely independent of the
/// ASP.NET Core hosting stack.
///
/// <b>Why not <c>WebApplication.CreateBuilder()</c>?</b> Both test collections
/// (<c>"Api"</c> and <c>"Api-E2E"</c>) run in parallel. <c>WebApplicationFactory</c>
/// uses <c>HostFactoryResolver</c> which subscribes to <c>DiagnosticListener.AllListeners</c>
/// — a process-wide static hook — to intercept the host build event inside
/// <c>Program.cs</c>. A concurrent <c>WebApplication.CreateBuilder().Build()</c> call
/// fires the same <c>"HostBuilt"</c> diagnostic event and can be captured by the wrong
/// factory's listener, causing the real <c>Program</c> host to never be seen ("entry
/// point exited without ever building an IHost"). <c>HttpListener</c> never participates
/// in that machinery and is therefore safe to start alongside <c>WebApplicationFactory</c>.
///
/// Because all Avro producers and consumers run in the same process (modulith
/// architecture), schemas registered by the producer are immediately visible to
/// the consumer — no distributed consistency concern.
/// </summary>
internal sealed class InProcessSchemaRegistryServer : IAsyncDisposable
{
    private sealed record RegisteredEntry(string Subject, string Schema, int Version);

    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _backgroundTask;

    // Schema state — all handler calls below access these shared structures.
    private readonly ConcurrentDictionary<int, RegisteredEntry> _byId = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _bySubjectSchema = new();
    private readonly object _registrationLock = new();
    private int _nextId;

    /// <summary>The base URL (e.g. <c>http://localhost:54321</c>) the server listens on.</summary>
    public string BaseUrl { get; }

    private InProcessSchemaRegistryServer(HttpListener listener, string baseUrl)
    {
        _listener = listener;
        BaseUrl = baseUrl;
        _backgroundTask = ProcessRequestsAsync(_cts.Token);
    }

    /// <summary>
    /// Starts the server on an OS-assigned port and returns it ready to accept requests.
    /// </summary>
    public static Task<InProcessSchemaRegistryServer> StartAsync()
    {
        int port = GetFreePort();
        string prefix = $"http://localhost:{port}/";

        HttpListener listener = new();
        listener.Prefixes.Add(prefix);
        listener.Start();

        return Task.FromResult(new InProcessSchemaRegistryServer(listener, $"http://localhost:{port}"));
    }

    /// <summary>Finds an available TCP port by binding a TcpListener to port 0.</summary>
    private static int GetFreePort()
    {
        using TcpListener tcp = new(IPAddress.Loopback, 0);
        tcp.Start();
        int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    private async Task ProcessRequestsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }

            // Fire-and-forget per request so slow handlers don't block the accept loop.
            _ = HandleRequestAsync(context);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            await DispatchAsync(context);
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(context.Response, 500,
                new { error_code = 50001, message = ex.Message });
        }
    }

    private async Task DispatchAsync(HttpListenerContext ctx)
    {
        string method = ctx.Request.HttpMethod;
        // AbsolutePath is already percent-decoded by HttpListener on Windows; on Linux
        // it may not be, so UnescapeDataString each segment individually below.
        string rawPath = ctx.Request.Url!.AbsolutePath.TrimEnd('/');
        string[] seg = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // GET /subjects
        if (method == "GET" && seg.Length == 1 && seg[0] == "subjects")
        {
            await WriteJsonAsync(ctx.Response, 200, _bySubjectSchema.Keys.ToArray());
            return;
        }

        // GET /config
        if (method == "GET" && seg.Length == 1 && seg[0] == "config")
        {
            await WriteJsonAsync(ctx.Response, 200, new { compatibilityLevel = "NONE" });
            return;
        }

        // GET /config/{subject}
        if (method == "GET" && seg.Length == 2 && seg[0] == "config")
        {
            await WriteJsonAsync(ctx.Response, 200, new { compatibilityLevel = "NONE" });
            return;
        }

        // GET /schemas/ids/{id}
        if (method == "GET" && seg.Length == 3 && seg[0] == "schemas" && seg[1] == "ids"
            && int.TryParse(seg[2], out int schemaId))
        {
            if (_byId.TryGetValue(schemaId, out RegisteredEntry? entry))
                await WriteJsonAsync(ctx.Response, 200,
                    new { schema = entry.Schema, schemaType = "AVRO" });
            else
                await WriteJsonAsync(ctx.Response, 404,
                    new { error_code = 40403, message = "Schema not found." });
            return;
        }

        // POST /subjects/{subject}/versions  — register schema (idempotent by content)
        if (method == "POST" && seg.Length == 3 && seg[0] == "subjects" && seg[2] == "versions")
        {
            string subject = Uri.UnescapeDataString(seg[1]);
            JsonNode? body = await JsonNode.ParseAsync(ctx.Request.InputStream);
            string schema = body?["schema"]?.GetValue<string>() ?? string.Empty;
            string normalized = NormalizeSchema(schema);

            int registeredId;
            lock (_registrationLock)
            {
                ConcurrentDictionary<string, int> subjectSchemas =
                    _bySubjectSchema.GetOrAdd(subject, _ => new ConcurrentDictionary<string, int>());

                if (!subjectSchemas.TryGetValue(normalized, out registeredId))
                {
                    registeredId = ++_nextId;
                    int version = subjectSchemas.Count + 1;
                    subjectSchemas[normalized] = registeredId;
                    _byId[registeredId] = new RegisteredEntry(subject, schema, version);
                }
            }

            await WriteJsonAsync(ctx.Response, 200, new { id = registeredId });
            return;
        }

        // POST /subjects/{subject}  — look up whether schema is already registered
        if (method == "POST" && seg.Length == 2 && seg[0] == "subjects")
        {
            string subject = Uri.UnescapeDataString(seg[1]);
            JsonNode? body = await JsonNode.ParseAsync(ctx.Request.InputStream);
            string schema = body?["schema"]?.GetValue<string>() ?? string.Empty;
            string normalized = NormalizeSchema(schema);

            if (_bySubjectSchema.TryGetValue(subject, out ConcurrentDictionary<string, int>? schemas)
                && schemas.TryGetValue(normalized, out int id)
                && _byId.TryGetValue(id, out RegisteredEntry? entry))
            {
                await WriteJsonAsync(ctx.Response, 200,
                    new { subject, id, version = entry.Version, schema = entry.Schema });
            }
            else
            {
                await WriteJsonAsync(ctx.Response, 404,
                    new { error_code = 40401, message = "Subject not found." });
            }
            return;
        }

        // GET /subjects/{subject}/versions/latest
        if (method == "GET" && seg.Length == 4 && seg[0] == "subjects"
            && seg[2] == "versions" && seg[3] == "latest")
        {
            string subject = Uri.UnescapeDataString(seg[1]);
            if (_bySubjectSchema.TryGetValue(subject, out ConcurrentDictionary<string, int>? schemas)
                && !schemas.IsEmpty)
            {
                int latestId = schemas.Values.Max();
                RegisteredEntry entry = _byId[latestId];
                await WriteJsonAsync(ctx.Response, 200,
                    new { subject, id = latestId, version = entry.Version, schema = entry.Schema, schemaType = "AVRO" });
            }
            else
            {
                await WriteJsonAsync(ctx.Response, 404,
                    new { error_code = 40401, message = "Subject not found." });
            }
            return;
        }

        // GET /subjects/{subject}/versions/{version}
        if (method == "GET" && seg.Length == 4 && seg[0] == "subjects"
            && seg[2] == "versions" && int.TryParse(seg[3], out int versionNum))
        {
            string subject = Uri.UnescapeDataString(seg[1]);
            if (_bySubjectSchema.TryGetValue(subject, out ConcurrentDictionary<string, int>? schemas))
            {
                foreach (KeyValuePair<string, int> kv in schemas)
                {
                    if (_byId.TryGetValue(kv.Value, out RegisteredEntry? entry) && entry.Version == versionNum)
                    {
                        await WriteJsonAsync(ctx.Response, 200,
                            new { subject, id = kv.Value, version = versionNum, schema = entry.Schema });
                        return;
                    }
                }
            }
            await WriteJsonAsync(ctx.Response, 404,
                new { error_code = 40402, message = "Version not found." });
            return;
        }

        await WriteJsonAsync(ctx.Response, 404,
            new { error_code = 40401, message = "Not found." });
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.OutputStream.Close();
    }

    /// <summary>
    /// Normalizes Avro JSON schema whitespace so equivalent schemas with different
    /// formatting do not create duplicate registrations.
    /// </summary>
    private static string NormalizeSchema(string schema)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(schema);
            return JsonSerializer.Serialize(doc.RootElement);
        }
        catch
        {
            return schema;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        try { await _backgroundTask; }
        catch (OperationCanceledException) { /* expected on cancellation */ }
        _cts.Dispose();
    }
}
