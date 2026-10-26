namespace Axis.Configuration.Compilation;

/// <summary>
/// Reads the placeholders of a notification text. A placeholder is <c>{name}</c>, where the name is
/// one or more ASCII letters and digits. <c>{{</c> and <c>}}</c> write a literal brace. Any other
/// brace is a problem.
/// </summary>
public static class TextPlaceholders
{
    /// <summary>
    /// Reads <paramref name="text"/>. When it is valid, <paramref name="names"/> holds the name of each
    /// placeholder in order of appearance, once for each time it appears, and <paramref name="problem"/> is
    /// null. Otherwise it returns false and <paramref name="problem"/> names the brace and its offset.
    /// </summary>
    public static bool TryParse(string text, out IReadOnlyList<string> names, out string? problem)
    {
        var found = new List<string>();
        names = found;
        problem = null;
        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];
            if (current == '{')
            {
                if (index + 1 < text.Length && text[index + 1] == '{')
                {
                    index += 2;
                    continue;
                }

                var end = index + 1;
                while (end < text.Length && IsNameCharacter(text[end]))
                {
                    end++;
                }

                if (end == index + 1 || end >= text.Length || text[end] != '}')
                {
                    problem = $"The '{{' at offset {index} does not start a placeholder. Write '{{name}}' with letters and digits only, or '{{{{' for a brace.";
                    return false;
                }

                found.Add(text[(index + 1)..end]);
                index = end + 1;
            }
            else if (current == '}')
            {
                if (index + 1 < text.Length && text[index + 1] == '}')
                {
                    index += 2;
                    continue;
                }

                problem = $"The '}}' at offset {index} does not end a placeholder. Write '}}}}' for a brace.";
                return false;
            }
            else
            {
                index++;
            }
        }

        return true;
    }

    private static bool IsNameCharacter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}
