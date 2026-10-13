using System.Text.Json.Nodes;

namespace Axis.Configuration.Tests;

/// <summary>
/// Installs the builds of the test-only extension package, which <c>tests/packages/TestPackages.targets</c>
/// copies to the test output, in a folder that acts as <c>Extensions:Path</c>.
/// </summary>
internal static class TestPackages
{
    public const string Id = "test-package";

    /// <summary>Copies the given versions, 1 for <c>1.0.0</c> and 2 for <c>2.0.0</c>, to <paramref name="root"/>.</summary>
    public static TemporaryFolder Install(this TemporaryFolder root, params int[] majorVersions)
    {
        foreach (var major in majorVersions)
        {
            var source = Path.Combine(AppContext.BaseDirectory, "TestPackages", $"v{major}");
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(root.Path, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }

        return root;
    }

    /// <summary>Changes the manifest of an installed version in place.</summary>
    public static TemporaryFolder EditManifest(this TemporaryFolder root, string version, Action<JsonObject> edit)
    {
        var path = Path.Combine(root.Path, Id, version, "axis-package.json");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(manifest);
        File.WriteAllText(path, manifest.ToJsonString());
        return root;
    }
}
