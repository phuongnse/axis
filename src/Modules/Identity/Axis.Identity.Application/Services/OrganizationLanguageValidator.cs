using System.Text.RegularExpressions;

namespace Axis.Identity.Application.Services;

public static partial class OrganizationLanguageValidator
{
    [GeneratedRegex(@"^[a-z]{2}(-[A-Z]{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTagPattern();

    public static bool IsValid(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return true;

        return LanguageTagPattern().IsMatch(language.Trim());
    }
}
