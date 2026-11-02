using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Tests;

/// <summary>
/// Layers each resource of the sample modules with all of its own content, then changes each string value in
/// turn to a name that nothing has. Every check that reports at a pointer of the layered resource then runs on
/// a part that the layer owns, so none of its diagnostics may stay in the module file.
/// </summary>
public sealed class LayerDiagnosticCoverageTests
{
    private const string Unknown = "NoSuchName";

    private static readonly string[] _identityPaths = ["", "/id", "/kind", "/name", "/formatVersion"];

    private static readonly string[] _identityProperties = ["id", "kind", "name", "formatVersion"];

    public static TheoryData<string, string> Resources()
    {
        var rows = new TheoryData<string, string>();
        var pinned = ApplicationCompiler.Compile(
            Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), SamplePackages.Versions, SampleModules.Modules).Model!.Modules.Select(module => module.Id).ToHashSet();
        foreach (var module in SampleModules.Modules.Where(module => pinned.Contains(module.Manifest.Id)))
        {
            // The test layers the resources onto the purchase request sample, so only the modules it pins have rows.
            var kindsByName = LayerChecker.KindsByName(module);
            foreach (var file in module.Files)
            {
                if (file.Path == "module.json")
                {
                    continue;
                }

                using var document = JsonDocument.Parse(file.Content);
                var kind = document.RootElement.GetProperty("kind").GetString();
                var name = document.RootElement.GetProperty("name").GetString()!;
                if (kind is ResourceKinds.Scenario or ResourceKinds.Contract
                    || kindsByName[name].Distinct(StringComparer.Ordinal).Count() != 1)
                {
                    continue;
                }

                rows.Add(module.Manifest.Id, file.Path);
            }
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public void A_diagnostic_about_a_changed_value_of_a_layered_resource_is_not_left_in_the_module_file(string moduleId, string path)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");
        var files = Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories)
            .Select(file => new ResourceContent(Path.GetRelativePath(folder, file).Replace('\\', '/'), File.ReadAllText(file)))
            .ToList();
        var module = SampleModules.Modules.Single(candidate => candidate.Manifest.Id == moduleId);
        var resource = JsonNode.Parse(module.Files.Single(file => file.Path == path).Content)!.AsObject();
        var name = resource["name"]!.GetValue<string>();
        var moduleFile = $"modules/{moduleId}/{path}";
        var layerFile = $"customizations/{moduleId}/{name}.json";

        var changes = (JsonObject)resource.DeepClone();
        foreach (var property in _identityProperties)
        {
            changes.Remove(property);
        }

        var leaves = new List<Action<JsonNode>>();
        CollectStrings(changes, [], leaves);
        Assert.NotEmpty(leaves);

        var inLayer = 0;
        var left = new List<string>();
        foreach (var change in leaves)
        {
            var changed = (JsonObject)changes.DeepClone();
            change(changed);
            var layer = new JsonObject
            {
                ["id"] = "7a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c01",
                ["kind"] = "layer",
                ["formatVersion"] = 1,
                ["target"] = $"{moduleId}/{name}",
                ["changes"] = changed,
            };

            var result = ApplicationCompiler.CompileFiles(
                [.. files, new ResourceContent(layerFile, layer.ToJsonString())], SamplePackages.Versions, SampleModules.Modules);

            inLayer += result.Diagnostics.Count(diagnostic => diagnostic.File == layerFile);
            left.AddRange(result.Diagnostics
                .Where(diagnostic => diagnostic.File == moduleFile && Array.IndexOf(_identityPaths, diagnostic.Path) < 0)
                .Select(diagnostic => $"{diagnostic.Code} at {diagnostic.Path} after {changed.ToJsonString()}"));
        }

        Assert.Empty(left.Distinct());
        Assert.True(inLayer > 0, $"No change of '{layerFile}' produced a diagnostic in the layer file, so the test checks nothing.");
    }

    private static void CollectStrings(JsonNode? node, List<object> path, List<Action<JsonNode>> leaves)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    CollectStrings(child, [.. path, name], leaves);
                }

                break;
            case JsonArray list:
                for (var index = 0; index < list.Count; index++)
                {
                    CollectStrings(list[index], [.. path, index], leaves);
                }

                break;
            case JsonValue value when value.TryGetValue<string>(out _):
                var steps = path.ToArray();
                leaves.Add(root =>
                {
                    var parent = root;
                    for (var step = 0; step < steps.Length - 1; step++)
                    {
                        parent = steps[step] is int position ? parent.AsArray()[position]! : parent.AsObject()[(string)steps[step]]!;
                    }

                    if (steps[^1] is int last)
                    {
                        parent.AsArray()[last] = Unknown;
                    }
                    else
                    {
                        parent.AsObject()[(string)steps[^1]] = Unknown;
                    }
                });
                break;
        }
    }
}
