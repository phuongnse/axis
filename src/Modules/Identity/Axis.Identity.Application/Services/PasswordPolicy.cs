namespace Axis.Identity.Application.Services;

public static class PasswordPolicy
{
    public const int MinimumLength = 15;
    public const int MaximumLength = 128;

    public const string RequiredMessage = "Password is required.";
    public const string TooShortMessage = "Password must be at least 15 characters.";
    public const string TooLongMessage = "Password must be 128 characters or fewer.";
    public const string CommonPasswordMessage = "Choose a less predictable password.";

    private const int MinimumSequentialRunLength = 6;

    private static readonly string[] KeyboardSequences =
    [
        "qwertyuiop",
        "asdfghjkl",
        "zxcvbnm",
    ];

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456789012345",
        "adminadminadmin",
        "correcthorsebatterystaple",
        "letmeinletmein",
        "passwordpassword",
        "passwordpassword1",
        "qwertyqwerty123",
        "testpassword123",
        "welcome123456789",
    };

    public static string? Validate(string? password, params string?[] contextValues)
    {
        if (string.IsNullOrEmpty(password))
            return RequiredMessage;

        if (password.Length < MinimumLength)
            return TooShortMessage;

        if (password.Length > MaximumLength)
            return TooLongMessage;

        if (IsCommonOrPredictable(password, contextValues))
            return CommonPasswordMessage;

        return null;
    }

    public static bool IsCommonOrPredictable(string password, params string?[] contextValues)
    {
        string normalized = Normalize(password);
        if (CommonPasswords.Contains(normalized))
            return true;

        if (IsPredictable(normalized))
            return true;

        return contextValues
            .SelectMany(GetContextCandidates)
            .Any(candidate => string.Equals(normalized, candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPredictable(string normalized) =>
        HasRepeatedShortPattern(normalized)
        || HasSequentialRun(normalized, MinimumSequentialRunLength)
        || HasKeyboardSequence(normalized, MinimumSequentialRunLength);

    private static bool HasRepeatedShortPattern(string normalized)
    {
        int maxPatternLength = Math.Min(4, normalized.Length / 2);
        for (int patternLength = 1; patternLength <= maxPatternLength; patternLength++)
        {
            if (normalized.Length % patternLength != 0)
                continue;

            string pattern = normalized[..patternLength];
            bool repeats = true;
            for (int index = patternLength; index < normalized.Length; index += patternLength)
            {
                if (!string.Equals(
                    pattern,
                    normalized.Substring(index, patternLength),
                    StringComparison.OrdinalIgnoreCase))
                {
                    repeats = false;
                    break;
                }
            }

            if (repeats)
                return true;
        }

        return false;
    }

    private static bool HasSequentialRun(string normalized, int minimumRunLength)
    {
        int ascendingRun = 1;
        int descendingRun = 1;

        for (int index = 1; index < normalized.Length; index++)
        {
            char previous = normalized[index - 1];
            char current = normalized[index];

            ascendingRun = IsNextCharacter(previous, current, ascending: true) ? ascendingRun + 1 : 1;
            descendingRun = IsNextCharacter(previous, current, ascending: false) ? descendingRun + 1 : 1;

            if (ascendingRun >= minimumRunLength || descendingRun >= minimumRunLength)
                return true;
        }

        return false;
    }

    private static bool HasKeyboardSequence(string normalized, int minimumRunLength) =>
        KeyboardSequences.Any(sequence =>
            HasKeyboardSequence(normalized, sequence, minimumRunLength)
            || HasKeyboardSequence(normalized, new string(sequence.Reverse().ToArray()), minimumRunLength));

    private static bool HasKeyboardSequence(string normalized, string sequence, int minimumRunLength)
    {
        if (normalized.Length < minimumRunLength)
            return false;

        for (int index = 0; index <= sequence.Length - minimumRunLength; index++)
        {
            string candidate = sequence.Substring(index, minimumRunLength);
            if (normalized.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsNextCharacter(char previous, char current, bool ascending)
    {
        if (char.IsAsciiDigit(previous) && char.IsAsciiDigit(current))
        {
            int previousValue = previous - '0';
            int currentValue = current - '0';
            int expected = ascending ? (previousValue + 1) % 10 : (previousValue + 9) % 10;
            return currentValue == expected;
        }

        if (char.IsAsciiLetter(previous) && char.IsAsciiLetter(current))
        {
            int previousValue = char.ToLowerInvariant(previous) - 'a';
            int currentValue = char.ToLowerInvariant(current) - 'a';
            int expected = ascending ? (previousValue + 1) % 26 : (previousValue + 25) % 26;
            return currentValue == expected;
        }

        return false;
    }

    private static IEnumerable<string> GetContextCandidates(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        string normalized = Normalize(value);
        if (normalized.Length >= MinimumLength)
            yield return normalized;

        int atIndex = value.IndexOf('@', StringComparison.Ordinal);
        if (atIndex > 0)
        {
            string localPart = Normalize(value[..atIndex]);
            if (localPart.Length >= MinimumLength)
                yield return localPart;
        }
    }

    private static string Normalize(string value) =>
        new(value.Trim().Where(c => !char.IsWhiteSpace(c)).ToArray());
}
