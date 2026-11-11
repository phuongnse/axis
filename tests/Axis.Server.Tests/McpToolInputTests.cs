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
}
