using System.Text.Json.Nodes;
using Axis.Server.Mcp;

namespace Axis.Server.Tests;

/// <summary>A tool input of the type <c>array</c> is a list of strings, see docs/reference/mcp-server.md#tools.</summary>
public sealed class McpToolInputTests
{
    private static readonly ToolDefinition _tool = new(
        "tool", "A tool.", [new ToolProperty("names", "Some names.", false, "array"), new ToolProperty("application", "The name.", true)]);

    [Fact]
    public void A_list_of_strings_is_kept_as_its_json_text()
    {
        var arguments = _tool.Read(JsonNode.Parse("""{"application":"A","names":["a","b"]}"""));

        Assert.Equal("""["a","b"]""", arguments!["names"]);
        Assert.Equal("[]", _tool.Read(JsonNode.Parse("""{"application":"A","names":[]}"""))!["names"]);
    }

    [Theory]
    [InlineData("\"a\"")]
    [InlineData("[1]")]
    [InlineData("[\"a\",null]")]
    [InlineData("[[\"a\"]]")]
    [InlineData("[{\"a\":\"b\"}]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("1")]
    public void A_value_that_is_not_a_list_of_strings_breaks_the_input_schema(string value)
    {
        Assert.Null(_tool.Read(JsonNode.Parse($$"""{"application":"A","names":{{value}}}""")));
    }

    [Fact]
    public void The_schema_describes_the_list_with_items_of_type_string()
    {
        var properties = _tool.Describe()["inputSchema"]!["properties"]!;

        Assert.Equal("array", properties["names"]!["type"]!.GetValue<string>());
        Assert.Equal("string", properties["names"]!["items"]!["type"]!.GetValue<string>());
        Assert.Null(properties["application"]!["items"]);
    }

    [Theory]
    [InlineData("read_reference")]
    [InlineData("read_resource")]
    [InlineData("read_module_resource")]
    [InlineData("read_package_file")]
    public void A_paged_tool_lists_an_optional_cursor_and_says_how_to_read_the_pages(string name)
    {
        var tool = Assert.Single(ListedTools(), entry => entry["name"]!.GetValue<string>() == name);

        var schema = tool["inputSchema"]!;
        Assert.Equal("string", schema["properties"]!["cursor"]!["type"]!.GetValue<string>());
        Assert.DoesNotContain(schema["required"]!.AsArray(), required => required!.GetValue<string>() == "cursor");
        Assert.Contains("cursor", tool["description"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_that_is_not_paged_lists_no_cursor_unless_it_has_its_own()
    {
        var tools = ListedTools().ToDictionary(entry => entry["name"]!.GetValue<string>());

        Assert.Null(tools["write_resource"]["inputSchema"]!["properties"]!["cursor"]);
        Assert.Null(tools["list_resources"]["inputSchema"]!["properties"]!["cursor"]);
    }

    [Fact]
    public void A_paged_tool_accepts_a_cursor_and_a_tool_that_is_not_paged_does_not()
    {
        var paged = _tool with { Paged = true };

        Assert.Equal("c", paged.Read(JsonNode.Parse("""{"application":"A","cursor":"c"}"""))!["cursor"]);
        Assert.Null(_tool.Read(JsonNode.Parse("""{"application":"A","cursor":"c"}""")));
        Assert.Null(paged.Read(JsonNode.Parse("""{"application":"A","cursor":1}""")));
    }

    private static IEnumerable<JsonNode> ListedTools() => McpServer.ListTools()["tools"]!.AsArray().Select(tool => tool!);
}
