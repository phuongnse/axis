using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Axis.Api.Tests.Helpers;

/// <summary>
/// Minimal in-process implementation of the Confluent Schema Registry REST API.
///
/// Used by <see cref="ProvisioningE2EFixture"/> so Wolverine's Avro serializer
/// (which speaks to the Schema Registry over HTTP) can exercise the real Kafka
/// transport layer without requiring a separate container.
///
/// Because all Avro producers and consumers run in the same process (modulith
/// architecture), schemas registered by the producer are immediately visible to
/// the consumer — no distributed consistency issue.
/// </summary>
internal sealed class InProcessSchemaRegistryServer : IAsyncDisposable
{
    private sealed record RegisteredEntry(string Subject, string Schema, int Version);

    private readonly WebApplication _app;

    /// <summary>The base URL (e.g. <c>http://127.0.0.1:54321</c>) the server listens on.</summary>
    public string BaseUrl { get; }

    private InProcessSchemaRegistryServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    /// <summary>Starts the server on an OS-assigned port and returns it ready to accept requests.</summary>
    public static async Task<InProcessSchemaRegistryServer> StartAsync()
    {
        // Shared mutable state — all route-handler closures below capture these references.
        ConcurrentDictionary<int, RegisteredEntry> byId = new();
        ConcurrentDictionary<string, ConcurrentDictionary<string, int>> bySubjectSchema = new();
        object registrationLock = new();
        int nextId = 0;

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        // "urls" is WebHostDefaults.ServerUrlsKey — port 0 lets the OS assign an available port.
        builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);  // suppress startup noise in test output

        WebApplication app = builder.Build();

        // POST /subjects/{subject}/versions — register a schema or return the existing ID (idempotent by content).
        app.MapPost("/subjects/{subject}/versions", async (string subject, HttpContext ctx) =>
        {
            JsonNode? body = await JsonNode.ParseAsync(ctx.Request.Body);
            string schema = body?["schema"]?.GetValue<string>() ?? string.Empty;
            string normalized = NormalizeSchema(schema);

            lock (registrationLock)
            {
                ConcurrentDictionary<string, int> subjectSchemas =
                    bySubjectSchema.GetOrAdd(subject, _ => new ConcurrentDictionary<string, int>());

                if (subjectSchemas.TryGetValue(normalized, out int existingId))
                    return Results.Json(new { id = existingId });

                int newId = ++nextId;
                int version = subjectSchemas.Count + 1;
                subjectSchemas[normalized] = newId;
                byId[newId] = new RegisteredEntry(subject, schema, version);
                return Results.Json(new { id = newId });
            }
        });

        // POST /subjects/{subject} — look up whether a schema is already registered (does not create).
        app.MapPost("/subjects/{subject}", async (string subject, HttpContext ctx) =>
        {
            JsonNode? body = await JsonNode.ParseAsync(ctx.Request.Body);
            string schema = body?["schema"]?.GetValue<string>() ?? string.Empty;
            string normalized = NormalizeSchema(schema);

            if (bySubjectSchema.TryGetValue(subject, out ConcurrentDictionary<string, int>? subjectSchemas)
                && subjectSchemas.TryGetValue(normalized, out int id)
                && byId.TryGetValue(id, out RegisteredEntry? entry))
            {
                return Results.Json(new { subject, id, version = entry.Version, schema = entry.Schema });
            }

            return Results.Json(
                new { error_code = 40401, message = "Subject not found." },
                statusCode: StatusCodes.Status404NotFound);
        });

        // GET /schemas/ids/{id} — fetch a schema by its integer ID (used by deserializers on the consume path).
        app.MapGet("/schemas/ids/{id:int}", (int id) =>
        {
            if (byId.TryGetValue(id, out RegisteredEntry? entry))
                return Results.Json(new { schema = entry.Schema, schemaType = "AVRO" });

            return Results.Json(
                new { error_code = 40403, message = "Schema not found." },
                statusCode: StatusCodes.Status404NotFound);
        });

        // GET /subjects/{subject}/versions/latest — latest registered schema for a subject.
        app.MapGet("/subjects/{subject}/versions/latest", (string subject) =>
        {
            if (!bySubjectSchema.TryGetValue(subject, out ConcurrentDictionary<string, int>? subjectSchemas)
                || subjectSchemas.IsEmpty)
            {
                return Results.Json(
                    new { error_code = 40401, message = "Subject not found." },
                    statusCode: StatusCodes.Status404NotFound);
            }

            int latestId = subjectSchemas.Values.Max();
            RegisteredEntry entry = byId[latestId];

            return Results.Json(new
            {
                subject,
                id = latestId,
                version = entry.Version,
                schema = entry.Schema,
                schemaType = "AVRO",
            });
        });

        // GET /subjects/{subject}/versions/{version} — specific version lookup.
        app.MapGet("/subjects/{subject}/versions/{version:int}", (string subject, int version) =>
        {
            if (bySubjectSchema.TryGetValue(subject, out ConcurrentDictionary<string, int>? subjectSchemas))
            {
                foreach (KeyValuePair<string, int> kv in subjectSchemas)
                {
                    if (byId.TryGetValue(kv.Value, out RegisteredEntry? entry) && entry.Version == version)
                        return Results.Json(new { subject, id = kv.Value, version, schema = entry.Schema });
                }
            }

            return Results.Json(
                new { error_code = 40402, message = "Version not found." },
                statusCode: StatusCodes.Status404NotFound);
        });

        // GET /subjects — list all registered subjects.
        app.MapGet("/subjects", () =>
            Results.Json(bySubjectSchema.Keys.ToArray()));

        // GET /config — global compatibility configuration (NONE = accept all schemas).
        app.MapGet("/config", () =>
            Results.Json(new { compatibilityLevel = "NONE" }));

        // GET /config/{subject} — per-subject compatibility (always NONE in tests).
        app.MapGet("/config/{subject}", (string subject) =>
            Results.Json(new { compatibilityLevel = "NONE" }));

        await app.StartAsync();

        string baseUrl = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new InProcessSchemaRegistryServer(app, baseUrl);
    }

    /// <summary>
    /// Normalizes an Avro JSON schema string so that equivalent schemas with different
    /// whitespace do not produce duplicate registrations.
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

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
