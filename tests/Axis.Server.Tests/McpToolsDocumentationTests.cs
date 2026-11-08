using System.Text.RegularExpressions;
using Axis.Server.Mcp;

namespace Axis.Server.Tests;

/// <summary>
/// The tools table of docs/reference/mcp-server.md lists, without a planned marker, exactly the tools of <see cref="McpServer.Tools"/>
/// and exactly the tools that the <c>tools/list</c> answer offers.
/// </summary>
public sealed partial class McpToolsDocumentationTests
{
    [Fact]
    public void The_tools_table_lists_without_a_planned_marker_the_tools_the_server_serves()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "reference", "mcp-server.md"));
        var documented = new List<string>();
        var tables = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!TableHeader().IsMatch(lines[i]))
            {
                continue;
            }

            tables++;
            for (var j = i + 1; j < lines.Length && lines[j].StartsWith('|'); j++)
            {
                if (ToolRow().Match(lines[j]) is { Success: true } row && !row.Groups[2].Value.Contains("_(planned for", StringComparison.Ordinal))
                {
                    documented.Add(row.Groups[1].Value);
                }
            }
        }

        Assert.True(tables == 1, $"Expected exactly one table with header '| Tool | Input | Output |' in docs/reference/mcp-server.md, found {tables}.");
        Assert.NotEmpty(documented);

        var served = McpServer.Tools.ToList();
        Assert.True(!served.Except(documented).Any(), $"Served but not listed without a planned marker in docs/reference/mcp-server.md: {string.Join(", ", served.Except(documented))}.");
        Assert.True(!documented.Except(served).Any(), $"Listed without a planned marker but not served by McpServer: {string.Join(", ", documented.Except(served))}.");
        Assert.True(documented.Distinct().Count() == documented.Count, "The tools table in docs/reference/mcp-server.md lists a tool twice.");

        var offered = McpServer.ListTools()["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>()).ToList();
        Assert.True(!offered.Except(served).Any() && !served.Except(offered).Any(), $"The tools/list answer and McpServer.Tools differ: only in tools/list: {string.Join(", ", offered.Except(served))}, only in McpServer.Tools: {string.Join(", ", served.Except(offered))}.");
        Assert.True(offered.Distinct().Count() == offered.Count, "The tools/list answer offers a tool twice.");
    }

    [GeneratedRegex(@"^\|\s*Tool\s*\|\s*Input\s*\|\s*Output\s*\|")]
    private static partial Regex TableHeader();

    [GeneratedRegex(@"^\|\s*`([a-z_]+)`([^|]*)\|")]
    private static partial Regex ToolRow();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Axis.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Axis.slnx was not found above the test output folder.");
    }
}
