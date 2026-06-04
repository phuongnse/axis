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
    /// generated from. This verifies it against the running app and fails if it drifted,
    /// so the FE types can never silently diverge from the real API contract.
    /// </summary>
    [Fact]
    public void OpenApiDocument_IsInSyncWithCommittedSnapshot()
    {
        using IServiceScope scope = fixture.CreateScope();
        ISwaggerProvider provider = scope.ServiceProvider.GetRequiredService<ISwaggerProvider>();

        OpenApiDocument doc = provider.GetSwagger("v1");
        string fresh = doc.SerializeAsJson(OpenApiSpecVersion.OpenApi3_0).ReplaceLineEndings("\n");

        // The schema must match the camelCase wire the app actually emits.
        fresh.Should().Contain("\"orgName\"");
        fresh.Should().NotContain("\"org_name\"");

        string path = Path.Combine(RepoRoot(), "openapi.json");
        string? committed = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;

        if (committed != fresh)
        {
            committed.Should().Be(
                fresh,
                "openapi.json drifted from the API. Run `scripts/generate-api-contracts.ps1` "
                    + "to regenerate openapi.json and frontend/src/lib/api-types.ts, then commit both");
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
