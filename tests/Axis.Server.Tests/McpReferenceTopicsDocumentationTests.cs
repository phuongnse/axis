using System.Reflection;
using System.Text.RegularExpressions;
using Axis.Configuration.Resources;
using Axis.Server.Mcp;

namespace Axis.Server.Tests;

/// <summary>
/// The topics table of docs/reference/mcp-server.md maps each topic to the same pages as <see cref="McpReference.Pages"/>. The
/// topics are exactly the resource kinds, every mapped page is embedded in the server and every topic can be read.
/// </summary>
public sealed partial class McpReferenceTopicsDocumentationTests
{
    [Fact]
    public void The_topics_table_maps_each_topic_to_the_pages_the_server_serves()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "reference", "mcp-server.md"));
        var documented = new Dictionary<string, List<string>>(StringComparer.Ordinal);
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
                if (TopicRow().Match(lines[j]) is { Success: true } row)
                {
                    Assert.True(
                        documented.TryAdd(row.Groups[1].Value, [.. PageLink().Matches(row.Groups[2].Value).Select(link => link.Groups[1].Value)]),
                        $"The topics table in docs/reference/mcp-server.md lists topic '{row.Groups[1].Value}' twice.");
                }
            }
        }

        Assert.True(tables == 1, $"Expected exactly one table with header '| Topic | Pages |' in docs/reference/mcp-server.md, found {tables}.");

        var served = McpReference.Pages.ToDictionary(entry => entry.Key, entry => entry.Value.ToList(), StringComparer.Ordinal);
        Assert.True(
            !served.Keys.Except(documented.Keys).Any() && !documented.Keys.Except(served.Keys).Any(),
            $"The topics table and McpReference.Pages differ: only in the table: {string.Join(", ", documented.Keys.Except(served.Keys))}, only in McpReference.Pages: {string.Join(", ", served.Keys.Except(documented.Keys))}.");
        foreach (var (topic, pages) in served)
        {
            Assert.True(
                pages.SequenceEqual(documented[topic]),
                $"Topic '{topic}' maps to [{string.Join(", ", pages)}] in McpReference.Pages and to [{string.Join(", ", documented[topic])}] in docs/reference/mcp-server.md.");
        }
    }

    [Fact]
    public void The_topics_are_exactly_the_resource_kinds()
    {
        var kinds = typeof(ResourceKinds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(kinds);
        Assert.Equal(kinds.Order(StringComparer.Ordinal), McpReference.Pages.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_mapped_page_is_embedded_and_every_topic_can_be_read()
    {
        var mapped = McpReference.Pages.Values.SelectMany(pages => pages).Distinct().Order(StringComparer.Ordinal).ToList();

        Assert.Equal(mapped, McpReference.EmbeddedPages().Order(StringComparer.Ordinal).ToList());
        foreach (var topic in McpReference.Pages.Keys)
        {
            var (output, error) = McpReference.Read(topic);
            Assert.True(output is not null && error is null, $"Topic '{topic}' cannot be read.");
        }
    }

    [Fact]
    public void An_unknown_topic_answers_the_fixed_text()
    {
        var (output, error) = McpReference.Read("no-such-kind");

        Assert.Null(output);
        Assert.Equal(McpReference.NoTopic, error);
    }

    [GeneratedRegex(@"^\|\s*Topic\s*\|\s*Pages\s*\|")]
    private static partial Regex TableHeader();

    [GeneratedRegex(@"^\|\s*`([A-Za-z]+)`\s*\|([^|]*)\|")]
    private static partial Regex TopicRow();

    [GeneratedRegex(@"\]\(([a-z0-9-]+\.md)\)")]
    private static partial Regex PageLink();

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
