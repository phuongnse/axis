using System.Text.Json.Nodes;

namespace Axis.Configuration.Tests;

/// <summary>
/// Installs the builds of the test-only extension package, which <c>tests/packages/TestPackages.targets</c>
/// copies to the test output, in a folder that acts as <c>Extensions:Path</c>.
/// </summary>
internal static class TestPackages
{
    public const string Id = "test-package";

    /// <summary>The id of the Python test package, which <see cref="InstallPython"/> installs.</summary>
    public const string PythonId = "python-test-package";

    /// <summary>The build of the initializer package, whose code writes marker files when it runs. Pass it to <see cref="Install"/>.</summary>
    public const string InitializerBuild = "initializer";

    /// <summary>Copies the given builds, such as the versions <c>1.0.0</c>, <c>1.1.0</c> and <c>2.0.0</c> of the test package or <see cref="InitializerBuild"/>, to <paramref name="root"/>.</summary>
    public static TemporaryFolder Install(this TemporaryFolder root, params string[] versions)
    {
        foreach (var version in versions)
        {
            var source = Path.Combine(AppContext.BaseDirectory, "TestPackages", version);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(root.Path, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }

        return root;
    }

    /// <summary>
    /// Copies the Python test package and its dependency folder into one version folder, <c>1.0.0</c> of
    /// <see cref="PythonId"/>, as a build leaves it, without pip. The test project must copy <c>PythonPackage</c> to its output.
    /// </summary>
    public static TemporaryFolder InstallPython(this TemporaryFolder root)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "PythonPackage");
        var target = Path.Combine(root.Path, PythonId, "1.0.0");
        foreach (var folder in new[] { "package", "dependency" })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(source, folder), "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(Path.Combine(source, folder), file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
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
