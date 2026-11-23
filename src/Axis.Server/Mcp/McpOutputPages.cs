using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Axis.Studio;

namespace Axis.Server.Mcp;

/// <summary>
/// Cuts the output of a paged MCP tool that is over 1 MiB into pages of <c>{ part, cursor }</c>, as the
/// <see href="docs/reference/mcp-server.md#limits">limits</see> of the MCP server say. <c>part</c> is the next piece of the output's JSON
/// text and each page serializes to at most <see cref="StudioDiffPages.PageBytes"/>. The pager keeps no state: the cursor is the
/// Base64url text of the SHA-256 hash of the whole output and the offset of the next piece, and each call runs the tool again, so a
/// cursor of an output that has changed since is refused.
/// </summary>
internal static class McpOutputPages
{
    /// <summary>The tool error of a cursor that the current output did not give.</summary>
    internal const string ForeignCursor = "The cursor is not one this output gave.";

    /// <summary>
    /// The page of the output that the cursor names, or the first page without a cursor. The page is null when the output is answered
    /// whole: it has no cursor and is at most <see cref="StudioDiffPages.PageBytes"/>. The error is <see cref="ForeignCursor"/> when
    /// the cursor does not decode, names another output or an offset outside this one.
    /// </summary>
    internal static (JsonObject? Page, string? Error) Page(string output, string? cursor)
    {
        if (cursor is null && Encoding.UTF8.GetByteCount(output) <= StudioDiffPages.PageBytes)
        {
            return (null, null);
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(output)));
        var start = 0;
        if (cursor is not null && !TryReadOffset(cursor, hash, output.Length, out start))
        {
            return (null, ForeignCursor);
        }

        // The page of the largest slice that fits is found by binary search, because a page only grows with its slice.
        var best = Build(output, hash, start, NotInPair(output, start, start + 1));
        var low = start + 2;
        var high = output.Length;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var end = NotInPair(output, start, middle);
            var page = Build(output, hash, start, end);
            if (Encoding.UTF8.GetByteCount(page.ToJsonString()) <= StudioDiffPages.PageBytes)
            {
                best = page;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return (best, null);
    }

    // A slice that ends after a high surrogate would split a surrogate pair, so it ends before the pair or, when it holds only the high surrogate, after the pair.
    private static int NotInPair(string output, int start, int end)
    {
        if (end >= output.Length || !char.IsHighSurrogate(output[end - 1]))
        {
            return end;
        }

        return end - 1 > start ? end - 1 : end + 1;
    }

    private static JsonObject Build(string output, string hash, int start, int end)
    {
        return new JsonObject
        {
            ["part"] = output[start..end],
            ["cursor"] = end == output.Length ? null : Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{hash}:{end}")),
        };
    }

    private static bool TryReadOffset(string cursor, string hash, int length, out int offset)
    {
        offset = 0;
        string text;
        try
        {
            text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        }
        catch (FormatException)
        {
            return false;
        }

        var colon = text.LastIndexOf(':');
        return colon > 0
            && text[..colon] == hash
            && int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out offset)
            && offset > 0
            && offset < length;
    }
}
