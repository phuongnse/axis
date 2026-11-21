namespace Axis.PackageHost;

/// <summary>
/// Shapes what a process wrote to the console into the lines a protocol answer carries, see
/// docs/reference/package-runners.md#the-runner-protocol: the last <see cref="MaxLines"/> lines, each cut at <see cref="MaxLine"/> characters.
/// </summary>
internal static class OutputLines
{
    /// <summary>The most lines an answer's <c>output</c> holds, the last ones.</summary>
    public const int MaxLines = 1000;

    /// <summary>The longest line of an answer's <c>output</c>, in characters.</summary>
    public const int MaxLine = 1000;

    /// <summary>The last <see cref="MaxLines"/> lines of <paramref name="text"/>, without the line breaks and the final empty line.</summary>
    public static List<string> Of(string text)
    {
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return [.. lines.Skip(Math.Max(0, lines.Count - MaxLines)).Select(line => Cut(line, MaxLine))];
    }

    /// <summary>Cuts <paramref name="text"/> at <paramref name="maxLength"/> characters, and never between the two halves of a surrogate pair.</summary>
    public static string Cut(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..(char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength)];
    }
}
