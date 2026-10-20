using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axis.Configuration.Resources;

namespace Axis.Server.Studio;

/// <summary>
/// Forms the relative path of a new resource file from its content. Only letters, digits and, for a role,
/// hyphens reach the file name, so no request can name a path outside the application folder.
/// </summary>
internal static partial class StudioFileNames
{
    // The schemas set no limit on a locale. This one keeps the file name well under the file system limit.
    private const int MaxLength = 60;

    private static readonly Dictionary<string, string> _folders = new(StringComparer.Ordinal)
    {
        [ResourceKinds.Entity] = "entities",
        [ResourceKinds.Site] = "sites",
        [ResourceKinds.Page] = "pages",
        [ResourceKinds.Text] = "texts",
        [ResourceKinds.Seed] = "seeds",
        [ResourceKinds.DataSource] = "data-sources",
        [ResourceKinds.Rule] = "rules",
        [ResourceKinds.Sequence] = "sequences",
        [ResourceKinds.Process] = "processes",
        [ResourceKinds.Form] = "forms",
        [ResourceKinds.Role] = "roles",
        [ResourceKinds.Policy] = "policies",
        [ResourceKinds.Connector] = "connectors",
        [ResourceKinds.Event] = "events",
        [ResourceKinds.Endpoint] = "endpoints",
        [ResourceKinds.Trigger] = "triggers",
        [ResourceKinds.Migration] = "migrations",
        [ResourceKinds.Scenario] = "scenarios",
    };

    /// <summary>
    /// The path of the new file, such as <c>entities/cost-centre.json</c>, with <paramref name="id"/> set to the
    /// resource's id. When the content cannot form a safe path, it is null and <paramref name="problem"/> is a
    /// fixed title that holds no text from the request.
    /// </summary>
    internal static string? NewFile(JsonObject content, out Guid id, out string? problem)
    {
        id = Guid.Empty;
        if (String(content, "id") is not { Length: 36 } text || !Guid.TryParseExact(text, "D", out id))
        {
            problem = "The resource needs an id in the 8-4-4-4-12 hex form.";
            return null;
        }

        if (String(content, "kind") is not { } kind)
        {
            problem = "The resource needs a kind.";
            return null;
        }

        if (kind == ResourceKinds.Application)
        {
            problem = "The manifest cannot be created or deleted.";
            return null;
        }

        if (!_folders.TryGetValue(kind, out var folder))
        {
            problem = "The kind is not a resource kind.";
            return null;
        }

        // The role schema allows lower-case letters, digits and hyphens. Every other kind allows letters and digits.
        var isRole = kind == ResourceKinds.Role;
        if (String(content, "name") is not { } name || !(isRole ? RoleName() : Name()).IsMatch(name))
        {
            problem = isRole
                ? "A role name must be lower-case letters, digits and hyphens, start with a letter and have at most 60 characters."
                : "The name must be letters and digits, start with a letter and have at most 60 characters.";
            return null;
        }

        if (kind == ResourceKinds.Text)
        {
            if (String(content, "locale") is not { } locale || locale.Length > MaxLength || !Locale().IsMatch(locale))
            {
                problem = "A text needs a locale such as en or pt-BR.";
                return null;
            }

            problem = null;
            return $"{folder}/{locale.ToLowerInvariant()}.json";
        }

        problem = null;
        return $"{folder}/{(isRole ? name : Kebab(name))}.json";
    }

    /// <summary>Whether <paramref name="name"/> is an application name: a letter, then letters and digits, at most 60 characters.</summary>
    internal static bool IsName(string name) => Name().IsMatch(name);

    // A hyphen goes before a capital that follows a lower-case letter or a digit, or before the last capital
    // of an acronym that a lower-case letter follows. Everything is then lower-cased.
    internal static string Kebab(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (index > 0 && char.IsUpper(character)
                && (char.IsLower(name[index - 1]) || char.IsDigit(name[index - 1])
                    || (char.IsUpper(name[index - 1]) && index + 1 < name.Length && char.IsLower(name[index + 1]))))
            {
                builder.Append('-');
            }

            builder.Append(character);
        }

        return builder.ToString().ToLower(CultureInfo.InvariantCulture);
    }

    private static string? String(JsonObject content, string property) =>
        content[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,59}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Name();

    [GeneratedRegex("^[a-z][a-z0-9-]{0,59}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex RoleName();

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Locale();
}
