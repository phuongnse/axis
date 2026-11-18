using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Axis.Studio;

/// <summary>
/// The paging of a diff, which a draft's diff and the live diff share: a page holds whole entries that serialize to at
/// most <see cref="PageBytes"/>, or one larger entry alone, and a cursor names the <c>main</c> or branch commit the first
/// page was read at and the next entry. See <c>docs/reference/revisions-and-drafts.md#drafts</c>.
/// </summary>
internal static class StudioDiffPages
{
    /// <summary>The most bytes a page of a diff holds, as the server serializes it. A single entry larger than this fills a page alone.</summary>
    internal const int PageBytes = 1024 * 1024;

    // Room in a page for the start, the tip, the cursor and the braces around the entries.
    private const int EnvelopeBytes = 1024;

    /// <summary>
    /// The entries of a page that starts at <paramref name="from"/>, and the cursor of the next page, which names
    /// <paramref name="sha"/> and the next entry, or null on the last page.
    /// </summary>
    public static (IReadOnlyList<T> Items, string? Cursor) Page<T>(IReadOnlyList<T> entries, int from, string sha)
    {
        var size = EnvelopeBytes;
        var next = from;
        while (next < entries.Count)
        {
            size += JsonSerializer.SerializeToUtf8Bytes(entries[next], JsonSerializerOptions.Web).Length + 1;
            if (next > from && size > PageBytes)
            {
                break;
            }

            next++;
        }

        var items = new List<T>(next - from);
        for (var index = from; index < next; index++)
        {
            items.Add(entries[index]);
        }

        return (items, next < entries.Count ? WriteCursor(sha, next) : null);
    }

    // A cursor is the base64url of "<commit id>:<index of the next entry>". Null when it is not that, or the index is below 1.
    public static (string Sha, int Index)? ReadCursor(string cursor)
    {
        try
        {
            var text = Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars(cursor));
            var parts = text.Split(':');
            return parts.Length == 2
                && parts[0].Length == 40
                && parts[0].All(char.IsAsciiHexDigitLower)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index >= 1
                    ? (parts[0], index)
                    : null;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static string WriteCursor(string sha, int index) =>
        System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{sha}:{index}")));
}
