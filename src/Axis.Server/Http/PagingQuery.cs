using System.Globalization;

namespace Axis.Server.Http;

/// <summary>
/// The <c>page</c> and <c>pageSize</c> query parameters of a list, as the record API defines them.
/// Both are optional. A page is an integer of at least 1, and a page size is an integer from 1 to
/// <see cref="MaxPageSize"/>.
/// </summary>
internal static class PagingQuery
{
    public const int DefaultPage = 1;

    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    /// <summary>
    /// Reads <paramref name="page"/> and <paramref name="pageSize"/>, or their defaults when they
    /// are absent. Each invalid one adds its message to <paramref name="errors"/> under its name.
    /// Returns whether both are valid.
    /// </summary>
    public static bool TryRead(
        string? page,
        string? pageSize,
        IDictionary<string, string[]> errors,
        out int pageNumber,
        out int size)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var valid = true;
        pageNumber = DefaultPage;
        if (page is not null && (!TryParseInteger(page, out pageNumber) || pageNumber < 1))
        {
            errors["page"] = ["Must be an integer of at least 1."];
            valid = false;
        }

        size = DefaultPageSize;
        if (pageSize is not null && (!TryParseInteger(pageSize, out size) || size is < 1 or > MaxPageSize))
        {
            errors["pageSize"] = [$"Must be an integer from 1 to {MaxPageSize}."];
            valid = false;
        }

        return valid;
    }

    private static bool TryParseInteger(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
