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
    /// <summary>
    /// The committed <c>openapi.json</c> is the source of truth the frontend types are
    /// generated from. This regenerates it from the running app and fails if it drifted,
    /// so the FE types can never silently diverge from the real API contract.
    /// </summary>
    [Fact]
    public void OpenApiDocument_IsInSyncWithCommittedSnapshot()
    {
        using IServiceScope scope = fixture.CreateScope();
        ISwaggerProvider provider = scope.ServiceProvider.GetRequiredService<ISwaggerProvider>();

        OpenApiDocument doc = provider.GetSwagger("v1");
        string fresh = doc.SerializeAsJson(OpenApiSpecVersion.OpenApi3_0).ReplaceLineEndings("\n");

        // The schema must match the snake_case wire the app actually emits.
        fresh.Should().Contain("\"org_name\"");
        fresh.Should().NotContain("\"orgName\"");

        string path = Path.Combine(RepoRoot(), "openapi.json");
        string? committed = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;

        if (committed != fresh)
        {
            File.WriteAllText(path, fresh, new UTF8Encoding(false));
            committed.Should().Be(
                fresh,
                "openapi.json drifted from the API — it has been regenerated; commit it and run "
                    + "`npm run gen:api-types` in frontend/ to refresh the generated types");
        }
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Axis.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root.");
    }
}
