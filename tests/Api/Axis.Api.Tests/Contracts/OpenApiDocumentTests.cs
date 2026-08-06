using Axis.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;

namespace Axis.Api.Tests.Contracts;

[Collection("Api")]
public class OpenApiDocumentTests(ApiTestFixture fixture)
{
    /// <summary>
    /// The committed <c>openapi.json</c> is the source of truth the frontend types are
    /// generated from. This verifies it against the running app and fails if it drifted,
    /// so the FE types can never silently diverge from the real API contract.
    /// </summary>
    [Fact]
    public async Task OpenApiDocument_WhenGeneratedFromRunningApi_MatchesCommittedSnapshot()
    {
        using IServiceScope scope = fixture.CreateScope();
        ISwaggerProvider provider = scope.ServiceProvider.GetRequiredService<ISwaggerProvider>();

        OpenApiDocument doc = provider.GetSwagger("v1");
        string fresh = (await doc.SerializeAsJsonAsync(
            OpenApiSpecVersion.OpenApi3_0,
            TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

        string path = Path.Combine(RepoRoot(), "openapi.json");
        string? committed = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;

        if (committed != fresh)
        {
            committed.Should().Be(
                fresh,
                "openapi.json drifted from the API. Run `python scripts/axis.py frontend gen-api-types` "
                    + "to regenerate openapi.json and frontend/src/lib/api-generated, then commit both");
        }
    }

    [Fact]
    public void OpenApiDocument_WhenGenerated_DoesNotPublishBrowserWorkspaceTransitionOperations()
    {
        using IServiceScope scope = fixture.CreateScope();
        ISwaggerProvider provider = scope.ServiceProvider.GetRequiredService<ISwaggerProvider>();

        OpenApiDocument doc = provider.GetSwagger("v1");

        doc.Paths.Keys.Should().NotContain(path => path.StartsWith(
            "/api/workspace-context",
            StringComparison.Ordinal));
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Axis.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root.");
    }
}
