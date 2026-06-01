using System.Text;
using Axis.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace Axis.Api.Tests;

[Collection("Api")]
public class OpenApiDocumentTests(ApiTestFixture fixture)
{
    [Fact]
    public void GenerateOpenApiDocument()
    {
        using IServiceScope scope = fixture.CreateScope();
        ISwaggerProvider provider = scope.ServiceProvider.GetRequiredService<ISwaggerProvider>();

        OpenApiDocument doc = provider.GetSwagger("v1");
        string json = doc.SerializeAsJson(OpenApiSpecVersion.OpenApi3_0);

        string outPath = Path.Combine(RepoRoot(), "artifacts", "openapi.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, json, new UTF8Encoding(false));

        // The schema must match the snake_case wire the app actually emits.
        json.Should().Contain("\"org_name\"");
        json.Should().NotContain("\"orgName\"");
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Axis.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root.");
    }
}
