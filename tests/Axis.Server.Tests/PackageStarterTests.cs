using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Releases;

namespace Axis.Server.Tests;

/// <summary>
/// Fills in each language's package starter, the SPA's <c>packageStarters.json</c>, for a new package, and compiles it in an
/// application with application packages on. Vitest and this class test the same starters.
/// </summary>
public sealed class PackageStarterTests
{
    private const string PackageId = "purchasing-tools";
    private const string PackageLabel = "Purchasing tools";
    private const string PackageName = "PurchasingTools";

    private const string Application = """{ "id": "5e2d8f1a-7c3b-4a90-8d61-0b9f2e4c7a13", "kind": "application", "name": "Sample", "formatVersion": 1 }""";

    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (var language in Starters().Select(pair => pair.Key))
        {
            data.Add(language);
        }

        return data;
    }

    [Fact]
    public void Starters_exist_for_exactly_csharp_node_and_python()
    {
        var languages = Starters().Select(pair => pair.Key).Order(StringComparer.Ordinal);

        Assert.Equal(["csharp", "node", "python"], languages);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Starter_compiles_with_no_diagnostic_under_its_package_folder(string language)
    {
        var files = new List<ResourceContent> { new("application.json", Application) };
        foreach (var (path, text) in Starters().Single(pair => pair.Key == language).Value!.AsObject())
        {
            files.Add(new ResourceContent($"packages/{PackageId}/{Fill(path)}", Fill(text!.GetValue<string>())));
        }

        var result = ApplicationCompiler.CompileFiles(files, null, null, applicationPackages: true);

        var problems = result.Diagnostics.Where(diagnostic => diagnostic.File.StartsWith($"packages/{PackageId}/", StringComparison.Ordinal)).ToList();
        Assert.True(problems.Count == 0, $"The {language} starter has diagnostics: {string.Join("; ", problems.Select(problem => $"{problem.File} {problem.Code} {problem.Message}"))}");
    }

    private static string Fill(string text) =>
        text.Replace("$id", PackageId, StringComparison.Ordinal)
            .Replace("$label", PackageLabel, StringComparison.Ordinal)
            .Replace("$name", PackageName, StringComparison.Ordinal);

    private static IEnumerable<KeyValuePair<string, JsonNode?>> Starters()
    {
        var json = JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Starters", "packageStarters.json")))!.AsObject();
        return json.Select(pair => KeyValuePair.Create(pair.Key, pair.Value));
    }
}
