using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Axis.Server.Tests;

/// <summary>
/// The in-memory server that the OpenAPI document comes from. The unit test of the document and the
/// <c>tests/Axis.OpenApiDocument</c> console that writes it both use it, so the document and the routes the test compares it
/// with come from the same start-up. See <c>docs/development.md#build</c>.
/// </summary>
internal static class OpenApiHost
{
    /// <summary>The document's path, relative to the repository root.</summary>
    public const string DocumentPath = "artifacts/openapi/axis-server.json";

    // Port 1 on loopback refuses connections immediately, and the host never reaches the database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    public static WebApplicationFactory<Program> Create() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform();
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
            builder.UseTestOidc("default");
        });

    /// <summary>Writes the OpenAPI 3.1 document of the server to <paramref name="path"/> as JSON.</summary>
    public static async Task WriteAsync(WebApplicationFactory<Program> factory, string path, CancellationToken ct)
    {
        var provider = factory.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var document = await provider.GetOpenApiDocumentAsync(ct);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var stream = File.Create(path);
        await document.SerializeAsJsonAsync(stream, OpenApiSpecVersion.OpenApi3_1, ct);
    }
}
