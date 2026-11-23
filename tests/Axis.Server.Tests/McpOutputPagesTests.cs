using System.Text;
using System.Text.Json.Nodes;
using Axis.Server.Mcp;
using Axis.Studio;

namespace Axis.Server.Tests;

/// <summary>
/// The pager of the paged MCP tools cuts an output over 1 MiB into pages of at most 1 MiB that join back to the output, and refuses a
/// cursor that the output did not give, see docs/reference/mcp-server.md#limits.
/// </summary>
public sealed class McpOutputPagesTests
{
    private const string Mixed = "A \"quoted\" <tag> & é, Tiếng Việt, 日本語 and an emoji 😀. ";

    private static string BigOutput() =>
        new JsonObject { ["text"] = string.Concat(Enumerable.Repeat(Mixed, 3 * 1024 * 1024 / Mixed.Length)) }.ToJsonString();

    private static List<JsonObject> ReadPages(string output)
    {
        var pages = new List<JsonObject>();
        string? cursor = null;
        do
        {
            var (page, error) = McpOutputPages.Page(output, cursor);
            Assert.Null(error);
            Assert.NotNull(page);
            pages.Add(page);
            cursor = page["cursor"]?.GetValue<string>();
            Assert.True(pages.Count < 100, "The pages do not end.");
        }
        while (cursor is not null);

        return pages;
    }

    [Fact]
    public void An_output_over_one_mebibyte_is_cut_into_pages_that_join_back_to_it()
    {
        var output = BigOutput();
        Assert.True(Encoding.UTF8.GetByteCount(output) > StudioDiffPages.PageBytes);

        var pages = ReadPages(output);

        Assert.True(pages.Count > 1);
        Assert.All(pages, page => Assert.True(Encoding.UTF8.GetByteCount(page.ToJsonString()) <= StudioDiffPages.PageBytes));
        Assert.Equal(output, string.Concat(pages.Select(page => page["part"]!.GetValue<string>())));
        Assert.All(pages.SkipLast(1), page => Assert.NotNull(page["cursor"]));
        Assert.Null(pages[^1]["cursor"]);
    }

    [Fact]
    public void A_page_is_as_large_as_it_can_be()
    {
        var pages = ReadPages(BigOutput());

        Assert.All(pages.SkipLast(1), page => Assert.True(Encoding.UTF8.GetByteCount(page.ToJsonString()) > StudioDiffPages.PageBytes - 64));
    }

    [Fact]
    public void A_page_never_ends_between_the_halves_of_a_surrogate_pair()
    {
        var output = string.Concat(Enumerable.Repeat("😀a", 3 * 1024 * 1024 / 4));

        var pages = ReadPages(output);

        Assert.All(pages, page => Assert.False(char.IsHighSurrogate(page["part"]!.GetValue<string>()[^1])));
        Assert.Equal(output, string.Concat(pages.Select(page => page["part"]!.GetValue<string>())));
    }

    [Fact]
    public void An_output_of_at_most_one_mebibyte_is_answered_whole()
    {
        var output = new JsonObject { ["text"] = new string('a', StudioDiffPages.PageBytes - 20) }.ToJsonString();
        Assert.True(Encoding.UTF8.GetByteCount(output) <= StudioDiffPages.PageBytes);

        Assert.Equal((null, null), McpOutputPages.Page(output, null));
    }

    [Fact]
    public void A_cursor_that_the_output_gave_is_read()
    {
        var output = BigOutput();
        var cursor = McpOutputPages.Page(output, null).Page!["cursor"]!.GetValue<string>();

        var (page, error) = McpOutputPages.Page(output, cursor);

        Assert.Null(error);
        Assert.NotNull(page);
    }

    [Fact]
    public void A_cursor_of_an_output_that_changed_is_refused()
    {
        var output = BigOutput();
        var cursor = McpOutputPages.Page(output, null).Page!["cursor"]!.GetValue<string>();
        var changed = output.Replace("quoted", "quoteD", StringComparison.Ordinal);

        Assert.Equal((null, McpOutputPages.ForeignCursor), McpOutputPages.Page(changed, cursor));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("YTpi")]
    public void A_made_up_cursor_is_refused(string cursor)
    {
        Assert.Equal((null, McpOutputPages.ForeignCursor), McpOutputPages.Page(BigOutput(), cursor));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void A_cursor_with_an_offset_outside_the_output_is_refused(int offset)
    {
        var output = BigOutput();
        var first = McpOutputPages.Page(output, null).Page!["cursor"]!.GetValue<string>();
        var hash = Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars(first));
        hash = hash[..hash.LastIndexOf(':')];

        var cursor = System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{hash}:{offset}"));
        var atEnd = System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{hash}:{output.Length}"));

        Assert.Equal((null, McpOutputPages.ForeignCursor), McpOutputPages.Page(output, cursor));
        Assert.Equal((null, McpOutputPages.ForeignCursor), McpOutputPages.Page(output, atEnd));
    }
}
