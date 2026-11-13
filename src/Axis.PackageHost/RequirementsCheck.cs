using Axis.Configuration.Packages;

namespace Axis.PackageHost;

/// <summary>
/// Checks the lock file of a Python package, see docs/reference/extension-packages.md#building-a-package.
/// Every requirement line of <c>requirements.txt</c> must carry a <c>--hash</c>. The file is read as pip reads
/// it: a comment and a blank line are skipped and a line that ends in a backslash continues on the next line.
/// An option line such as <c>--index-url</c> needs no hash. A line that includes another requirements file or
/// installs an editable package is refused, because the hashes it brings in are never seen here.
/// </summary>
internal static class RequirementsCheck
{
    private static readonly string[] _includeOptions = ["-r", "--requirement", "-c", "--constraint", "-e", "--editable"];

    /// <summary>The problems with the lock file of the package in <paramref name="source"/>. None means every requirement has a hash.</summary>
    public static IReadOnlyList<string> Check(string source, PackageManifest manifest)
    {
        var lockFile = Path.Combine(source, manifest.Lock);
        if (!File.Exists(lockFile))
        {
            return [$"The lock file '{manifest.Lock}' is missing."];
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(lockFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The exception message names the absolute path, so it is never passed on.
            return [$"'{manifest.Lock}' cannot be checked: it cannot be read."];
        }

        var problems = new List<string>();
        foreach (var (number, line) in Logical(lines))
        {
            if (line.StartsWith('-'))
            {
                // A short option may be written with its value attached, as in -rother.txt.
                var option = line.StartsWith("--", StringComparison.Ordinal) ? line.Split([' ', '\t', '='], 2)[0] : line[..Math.Min(2, line.Length)];
                if (_includeOptions.Contains(option))
                {
                    problems.Add($"'{manifest.Lock}' line {number} includes other requirements, which cannot be checked for hashes.");
                }

                continue;
            }

            if (!HasHash(line))
            {
                var requirement = line.Split([' ', '\t', ';'], 2)[0];
                problems.Add($"'{manifest.Lock}' line {number} has no --hash: {requirement}.");
            }
        }

        return problems;
    }

    private static bool HasHash(string line) =>
        line.Contains("--hash=", StringComparison.Ordinal) || line.Contains("--hash ", StringComparison.Ordinal);

    /// <summary>The logical lines with the number of their first physical line: continuations joined, comments and blanks dropped.</summary>
    private static IEnumerable<(int Number, string Line)> Logical(string[] lines)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            var number = index + 1;
            var line = StripComment(lines[index]);
            while (line.EndsWith('\\') && index + 1 < lines.Length)
            {
                index++;
                line = line[..^1] + " " + StripComment(lines[index]);
            }

            line = line.TrimEnd('\\').Trim();
            if (line.Length > 0)
            {
                yield return (number, line);
            }
        }
    }

    /// <summary>Removes a <c>#</c> comment that starts a line or follows whitespace.</summary>
    private static string StripComment(string line)
    {
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '#' && (index == 0 || char.IsWhiteSpace(line[index - 1])))
            {
                return line[..index].TrimEnd();
            }
        }

        return line.TrimEnd();
    }
}
