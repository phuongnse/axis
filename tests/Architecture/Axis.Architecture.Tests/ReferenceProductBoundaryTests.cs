using FluentAssertions;

namespace Axis.Architecture.Tests;

public sealed class ReferenceProductBoundaryTests
{
    [Fact]
    public void ReferenceProductIdentifiers_WhenProductionInspected_AreAbsent()
    {
        string root = Conventions.RepositoryRootPath;
        string[] productionRoots =
        [
            Path.Combine(root, "src"),
            Path.Combine(root, "frontend", "src"),
        ];
        string[] identifiers =
        [
            "axis-reference-product",
            "axis_reference_product",
            "reference_application",
            "loan_application",
        ];

        string[] matches = productionRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => Path.GetExtension(path) is ".cs" or ".csproj" or ".json" or ".ts" or ".tsx")
            .Where(path => !path.Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj" or "node_modules"))
            .SelectMany(path => identifiers
                .Where(identifier => File.ReadAllText(path).Contains(identifier, StringComparison.OrdinalIgnoreCase))
                .Select(identifier => $"{Path.GetRelativePath(root, path)}: {identifier}"))
            .ToArray();

        matches.Should().BeEmpty(
            "Axis production must not contain a reference-product key, route, seed, or provisioning path");
    }
}
