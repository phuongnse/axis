using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Microsoft.EntityFrameworkCore;

namespace Axis.Integration.Tests;

public sealed class ModulePinningTests(ConfigurationDatabaseFixture database) : IClassFixture<ConfigurationDatabaseFixture>
{
    private const string Department = """
        {
          "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c07",
          "kind": "entity",
          "name": "Department",
          "formatVersion": 1,
          "fields": [ { "name": "name", "type": "text", "required": true } ]
        }
        """;

    // Each test uses its own application id, so tests sharing the database do not see each other's releases.
    private readonly Guid _applicationId = Guid.NewGuid();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_caret_range_pins_the_highest_installed_version_and_the_release_holds_its_files()
    {
        using var modules = Modules("1.0.0", "1.2.0");
        using var folder = ApplicationFolder();

        var compiled = await CompileAsync(folder, modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.NotNull(compiled.Release);
        Assert.Equal([new PinnedModuleModel("departments", "^1.0.0", "1.2.0")], compiled.Model!.Modules);
        var stored = await ReadReleaseAsync(compiled.Release.Id);
        var manifest = Assert.Single(stored.Resources, resource => resource.Path == "modules/departments/module.json");
        Assert.Contains("\"version\":\"1.2.0\"", manifest.Content, StringComparison.Ordinal);
        Assert.Contains(stored.Resources, resource => resource.Path == "modules/departments/Department.json");
        Assert.DoesNotContain(stored.Resources, resource => resource.Path.Contains("1.0.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Installing_a_newer_version_gives_a_new_release_and_the_older_release_keeps_its_version()
    {
        using var modules = Modules("1.0.0", "1.2.0");
        using var folder = ApplicationFolder();
        var first = await CompileAsync(folder, modules);

        Install(modules, "1.3.0");
        var second = await CompileAsync(folder, modules);

        Assert.NotNull(first.Release);
        Assert.NotNull(second.Release);
        Assert.NotEqual(first.Release.Id, second.Release.Id);
        Assert.NotEqual(first.Release.ContentHash, second.Release.ContentHash);
        var older = await ReadReleaseAsync(first.Release.Id);
        var newer = await ReadReleaseAsync(second.Release.Id);
        Assert.Contains("\"version\":\"1.2.0\"", Manifest(older), StringComparison.Ordinal);
        Assert.Contains("\"version\":\"1.3.0\"", Manifest(newer), StringComparison.Ordinal);
        Assert.Equal("1.3.0", Assert.Single(second.Model!.Modules).Version);

        var again = await CompileAsync(folder, modules);
        Assert.Equal(second.Release.Id, again.Release?.Id);
        Assert.Equal(2, await CountReleasesAsync());
    }

    [Fact]
    public async Task A_stored_release_rebuilds_its_model_after_its_module_version_is_removed()
    {
        using var modules = Modules("1.0.0", "1.2.0");
        using var folder = ApplicationFolder();
        var compiled = await CompileAsync(folder, modules);
        Assert.NotNull(compiled.Release);
        Directory.Delete(Path.Combine(modules.Path, "departments", "1.2.0"), recursive: true);

        var stored = await ReadReleaseAsync(compiled.Release.Id);
        var model = ReleaseCompiler.BuildModel(stored);

        Assert.Equal([new PinnedModuleModel("departments", "^1.0.0", "1.2.0")], model.Modules);
        Assert.Equal(stored.ContentHash, compiled.Release.ContentHash);
        Assert.Equal("1.0.0", Assert.Single(ModuleCatalog.Load(modules.Path).Modules).Manifest.Version);
    }

    private static string Manifest(Release release) =>
        release.Resources.Single(resource => resource.Path == "modules/departments/module.json").Content;

    private static void Install(TemporaryFolder modules, string version)
    {
        modules.With($"departments/{version}/module.json", $$"""{ "kind": "module", "id": "departments", "version": "{{version}}", "label": "Departments" }""");
        modules.With($"departments/{version}/Department.json", Department);
    }

    private static TemporaryFolder Modules(params string[] versions)
    {
        var root = new TemporaryFolder();
        foreach (var version in versions)
        {
            Install(root, version);
        }

        return root;
    }

    private TemporaryFolder ApplicationFolder() =>
        new TemporaryFolder().With("application.json", $$"""
            { "id": "{{_applicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1,
              "modules": [{ "id": "departments", "version": "^1.0.0" }] }
            """);

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder, TemporaryFolder modules)
    {
        var catalog = ModuleCatalog.Load(modules.Path);
        Assert.Empty(catalog.Diagnostics);
        await using var context = database.CreateContext();
        return await ReleaseCompiler.CompileAsync(folder.Path, context, [], catalog.Modules, CancellationToken);
    }

    private async Task<int> CountReleasesAsync()
    {
        await using var context = database.CreateContext();
        return await context.Releases.CountAsync(release => release.ApplicationId == _applicationId, CancellationToken);
    }

    private async Task<Release> ReadReleaseAsync(Guid id)
    {
        await using var context = database.CreateContext();
        var release = await context.Releases
            .AsNoTracking()
            .Include(r => r.Resources)
            .Include(r => r.Packages)
            .SingleAsync(r => r.Id == id, CancellationToken);
        release.Resources.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return release;
    }
}
