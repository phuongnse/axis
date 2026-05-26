using System.Text.RegularExpressions;

namespace Axis.Identity.Application.Services;

public static partial class OrganizationLanguageValidator
{
    // BCP-47 primary language (2–3 letters) plus optional subtags (script, region, variant, …).
    [GeneratedRegex(@"^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTagPattern();

    public static bool IsValid(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return true;

        return LanguageTagPattern().IsMatch(language.Trim());
    }
}
