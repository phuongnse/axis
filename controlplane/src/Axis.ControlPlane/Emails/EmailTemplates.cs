using System.Text.RegularExpressions;

namespace Axis.ControlPlane.Emails;

/// <summary>
/// The email templates of the control plane: for each kind a text in every language of <see cref="Languages"/>. A template is a
/// text file named <c>&lt;kind&gt;.&lt;language&gt;.txt</c>, embedded in the assembly. Its first line is <c>Subject: …</c>, a blank
/// line follows, and the rest is the plain text body. A placeholder is written <c>{{name}}</c>. The kinds are found from the
/// files themselves, so a new kind needs no registration, and a kind that lacks a language or whose languages use different
/// placeholders is rejected when the set is loaded, which stops the start.
/// </summary>
internal sealed partial class EmailTemplates
{
    /// <summary>The languages of every email: the languages a person can choose at sign-up.</summary>
    public static readonly IReadOnlyList<string> Languages = ["en", "vi"];

    private const string ResourcePrefix = "Axis.ControlPlane.Emails.Templates.";
    private const string SubjectPrefix = "Subject:";

    private readonly Dictionary<string, Dictionary<string, Template>> _kinds;

    private EmailTemplates(Dictionary<string, Dictionary<string, Template>> kinds) => _kinds = kinds;

    /// <summary>The kinds of the set, ordered by name.</summary>
    public IReadOnlyList<string> Kinds => [.. _kinds.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Loads the templates embedded in the assembly.</summary>
    /// <exception cref="InvalidOperationException">The set is not valid, see <see cref="Parse"/>.</exception>
    public static EmailTemplates LoadEmbedded()
    {
        var assembly = typeof(EmailTemplates).Assembly;
        var files = new List<(string ResourceName, string Text)>();
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            files.Add((name, reader.ReadToEnd()));
        }

        return Parse(files);
    }

    /// <summary>
    /// Builds a set from files named <c>&lt;kind&gt;.&lt;language&gt;.txt</c>, optionally behind a resource prefix. Each kind needs
    /// exactly the texts of <see cref="Languages"/>, each with a subject, and the same placeholders in all of them.
    /// </summary>
    /// <exception cref="InvalidOperationException">The message names every kind that is not valid, and why.</exception>
    public static EmailTemplates Parse(IEnumerable<(string ResourceName, string Text)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var problems = new List<string>();
        var kinds = new Dictionary<string, Dictionary<string, Template>>(StringComparer.Ordinal);
        foreach (var (resourceName, text) in files)
        {
            var parts = resourceName.EndsWith(".txt", StringComparison.Ordinal) ? resourceName[..^4].Split('.') : [];
            if (parts.Length < 2)
            {
                problems.Add($"The file {resourceName} is not named <kind>.<language>.txt.");
                continue;
            }

            var (kind, language) = (parts[^2], parts[^1]);
            if (!kinds.TryGetValue(kind, out var languages))
            {
                kinds[kind] = languages = new Dictionary<string, Template>(StringComparer.Ordinal);
            }

            if (!Languages.Contains(language))
            {
                problems.Add($"The email template {kind} has a text in the language {language}, which is not one of {string.Join(", ", Languages)}.");
            }
            else if (TryReadTemplate(text) is not { } template)
            {
                problems.Add($"The email template {kind} has no 'Subject:' first line and a blank line before the body in {language}.");
            }
            else
            {
                languages[language] = template;
            }
        }

        if (kinds.Count == 0)
        {
            problems.Add("The set has no template.");
        }

        foreach (var (kind, languages) in kinds)
        {
            foreach (var language in Languages.Where(language => !languages.ContainsKey(language)))
            {
                problems.Add($"The email template {kind} has no text in the language {language}.");
            }

            if (languages.Values.Select(template => string.Join(',', template.Placeholders.Order(StringComparer.Ordinal))).Distinct().Count() > 1)
            {
                problems.Add($"The email template {kind} uses different placeholders in its languages.");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The email templates are invalid. {string.Join(" ", problems)}");
        }

        return new EmailTemplates(kinds);
    }

    /// <summary>Whether the set has a template of <paramref name="kind"/>.</summary>
    public bool Contains(string kind) => _kinds.ContainsKey(kind);

    /// <summary>The placeholders of <paramref name="kind"/>, the same in every language.</summary>
    /// <exception cref="ArgumentException">The set has no such kind.</exception>
    public IReadOnlySet<string> Placeholders(string kind) => Get(kind, Languages[0]).Placeholders;

    /// <summary>Fills in the template of <paramref name="kind"/> in <paramref name="language"/>.</summary>
    /// <exception cref="ArgumentException">The kind or the language is unknown, or a placeholder has no value.</exception>
    public (string Subject, string Body) Render(string kind, string language, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var template = Get(kind, language);
        var missing = template.Placeholders.Where(name => !values.ContainsKey(name)).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException($"The email template {kind} needs a value for {string.Join(", ", missing)}.", nameof(values));
        }

        // One pass, so a value that holds {{x}} is never filled in again. A subject is one line.
        string Fill(string text) => Placeholder().Replace(text, match => values[match.Groups[1].Value]);
        return (Fill(template.Subject).ReplaceLineEndings(" "), Fill(template.Body));
    }

    private Template Get(string kind, string language)
    {
        if (!_kinds.TryGetValue(kind ?? string.Empty, out var languages))
        {
            throw new ArgumentException($"The email template {kind} does not exist.", nameof(kind));
        }

        return languages.TryGetValue(language ?? string.Empty, out var template)
            ? template
            : throw new ArgumentException($"The email template {kind} has no text in the language {language}.", nameof(language));
    }

    private static Template? TryReadTemplate(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length < 3
            || !lines[0].StartsWith(SubjectPrefix, StringComparison.Ordinal)
            || lines[0][SubjectPrefix.Length..].Trim() is not { Length: > 0 } subject
            || lines[1].Length > 0)
        {
            return null;
        }

        var body = string.Join('\n', lines.Skip(2)).Trim();
        if (body.Length == 0)
        {
            return null;
        }

        var placeholders = Placeholder().Matches(subject + "\n" + body).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        return new Template(subject, body, placeholders);
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex Placeholder();

    private sealed record Template(string Subject, string Body, HashSet<string> Placeholders);
}
