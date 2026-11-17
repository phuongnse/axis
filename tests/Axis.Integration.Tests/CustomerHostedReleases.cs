using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Microsoft.EntityFrameworkCore;

namespace Axis.Integration.Tests;

/// <summary>
/// Compiles and activates releases of one application whose own package <c>tools</c> is customer-hosted, in a tenant database, for the tests of
/// the registration check in <c>docs/reference/extension-packages.md#customer-hosted-runners</c>.
/// </summary>
internal sealed class CustomerHostedReleases
{
    private readonly string _name = $"Registered{Guid.NewGuid():N}";

    /// <summary>The id of the application whose releases this activates.</summary>
    public Guid ApplicationId { get; } = Guid.NewGuid();

    /// <summary>The manifest of the own package <c>tools</c> at <paramref name="version"/>, with the one operation <paramref name="operation"/>.</summary>
    public static string ToolsManifest(string version, string operation) =>
        $$"""
        {
          "id": "tools",
          "version": "{{version}}",
          "label": "Tools",
          "language": "python",
          "runner": "customer",
          "adapter": "adapter.py",
          "lock": "requirements.txt",
          "operations": [
            {
              "name": "{{operation}}",
              "type": "adapter.{{operation}}",
              "idempotent": true,
              "input": [
                { "name": "a", "type": "integer", "required": true },
                { "name": "b", "type": "integer", "required": true }
              ],
              "output": [{ "name": "total", "type": "integer" }]
            }
          ]
        }
        """;

    /// <summary>Compiles and activates the application with the own package files, and no installed package pinned.</summary>
    public Task<Guid> ActivateOwnAsync(string connectionString, string manifest, CancellationToken cancellationToken) =>
        ActivateAsync(connectionString, [], "[]", cancellationToken, ("packages/tools/axis-package.json", manifest));

    /// <summary>Compiles and activates the application with <paramref name="packages"/> as its <c>packages</c> entry, and returns the release's id.</summary>
    public async Task<Guid> ActivateAsync(
        string connectionString,
        IReadOnlyCollection<PackageVersion> versions,
        string packages,
        CancellationToken cancellationToken,
        params (string Path, string Content)[] files)
    {
        var application = $$"""
            { "id": "{{ApplicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1, "packages": {{packages}} }
            """;
        var resources = new List<ResourceContent> { new("application.json", application) };
        resources.AddRange(files.Select(file => new ResourceContent(file.Path, file.Content)));

        await using var context = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connectionString).Options);
        var compiled = await ReleaseCompiler.CompileAsync(resources, context, versions, [], cancellationToken);
        Assert.Empty(compiled.Diagnostics);
        var releaseId = compiled.Release!.Id;
        var set = await new ActiveReleaseStore(context).TrySetAsync(ApplicationId, _name, releaseId, [], DateTimeOffset.UtcNow, ActivationActor.Operator, cancellationToken);
        Assert.Equal(SetActiveReleaseResult.Set, set);
        return releaseId;
    }
}
