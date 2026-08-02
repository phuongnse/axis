using System.Text.Json;
using Axis.Mcp.Tools;

namespace Axis.Mcp.Tests;

public sealed class McpApiCoverageTests
{
    [Fact]
    public void OpenApi_WhenLoaded_MatchesTheMcpCoverageClassification()
    {
        string openApiPath = Path.Combine(FindRepoRoot(), "openapi.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(openApiPath));

        HashSet<string> operationIds = new(StringComparer.Ordinal);
        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (JsonProperty method in path.Value.EnumerateObject())
            {
                if (method.Value.TryGetProperty("operationId", out JsonElement operationId))
                    operationIds.Add(operationId.GetString()!);
            }
        }

        HashSet<string> classified = new(
            AxisMcpOperationCatalog.OperationToTool.Keys
                .Concat(AxisMcpOperationCatalog.BlockedOperationIds)
                .Concat(AxisMcpOperationCatalog.ExcludedOperationIds),
            StringComparer.Ordinal);

        Assert.Equal(
            operationIds.OrderBy(value => value, StringComparer.Ordinal),
            classified.OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(
            AxisMcpOperationCatalog.OperationToTool.Count,
            AxisMcpOperationCatalog.OperationToTool.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(
            AxisMcpOperationCatalog.OperationToTool.Keys.Intersect(
                AxisMcpOperationCatalog.BlockedOperationIds,
                StringComparer.Ordinal));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "openapi.json")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Axis repository root.");
    }
}
