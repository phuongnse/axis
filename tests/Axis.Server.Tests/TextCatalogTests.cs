using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Presentation.Texts;

namespace Axis.Server.Tests;

public sealed class TextCatalogTests
{
    private const string ReferenceLocale = "en";

    private static readonly Regex _placeholder = new(@"\{[A-Za-z0-9_]+\}", RegexOptions.CultureInvariant);
    private static readonly Regex _axcCode = new(@"AXC\d+", RegexOptions.CultureInvariant);
    private static readonly Regex _wordSeparator = new(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions _baselineOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly EmbeddedTextResourceProvider _provider = new();

    [Fact]
    public void The_platform_ships_english_and_vietnamese()
    {
        Assert.Equal(["en", "vi"], _provider.Locales);
    }

    [Fact]
    public void Every_locale_has_the_same_keys_as_english()
    {
        var reference = _provider.GetTexts(ReferenceLocale)!.Texts.Keys.ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var locale in _provider.Locales)
        {
            var keys = _provider.GetTexts(locale)!.Texts.Keys.ToHashSet(StringComparer.Ordinal);
            var missing = reference.Except(keys).Order(StringComparer.Ordinal).ToList();
            var extra = keys.Except(reference).Order(StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
            {
                problems.Add($"Locale '{locale}' is missing keys from '{ReferenceLocale}': {string.Join(", ", missing)}");
            }
            if (extra.Count > 0)
            {
                problems.Add($"Locale '{locale}' has keys that '{ReferenceLocale}' does not have: {string.Join(", ", extra)}");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_locale_keeps_the_placeholders_of_english()
    {
        var english = _provider.GetTexts(ReferenceLocale)!.Texts;
        var baseline = ReadBaseline();
        var problems = new List<string>();

        foreach (var locale in _provider.Locales.Where(candidate => candidate != ReferenceLocale))
        {
            var breaks = PlaceholderBreaks(english, _provider.GetTexts(locale)!.Texts);
            problems.AddRange(CompareWithBaseline("placeholders", locale, breaks, baseline.Placeholders.GetValueOrDefault(locale) ?? []));
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void No_locale_leaves_a_text_in_english()
    {
        var english = _provider.GetTexts(ReferenceLocale)!.Texts;
        var keepWords = ReadKeepWords();
        var baseline = ReadBaseline();
        var problems = new List<string>();

        foreach (var locale in _provider.Locales.Where(candidate => candidate != ReferenceLocale))
        {
            var breaks = UntranslatedKeys(english, _provider.GetTexts(locale)!.Texts, keepWords);
            problems.AddRange(CompareWithBaseline("untranslated", locale, breaks, baseline.Untranslated.GetValueOrDefault(locale) ?? []));
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void The_glossary_files_hold_each_term_by_its_status()
    {
        using var approved = ReadGlossary("ui-glossary.json");
        using var proposed = ReadGlossary("ui-glossary-proposed.json");

        var problems = GlossaryProblems(approved.RootElement, proposed.RootElement);

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void A_concept_in_both_glossary_files_is_reported()
    {
        using var approved = JsonDocument.Parse("""{"terms":[{"concept":"Job","status":"approved"}]}""");
        using var proposed = JsonDocument.Parse("""{"terms":[{"concept":"job","status":"proposed"}]}""");

        Assert.Equal(
            ["Concept 'job' is in both ui-glossary.json and ui-glossary-proposed.json"],
            GlossaryProblems(approved.RootElement, proposed.RootElement));
    }

    [Fact]
    public void A_term_that_is_not_approved_in_the_approved_glossary_is_reported()
    {
        using var approved = JsonDocument.Parse("""{"terms":[{"concept":"Job","status":"proposed"},{"concept":"Cell","status":"decide"}]}""");
        using var proposed = JsonDocument.Parse("""{"terms":[]}""");

        Assert.Equal(
        [
            "ui-glossary.json term 'Job' has the status 'proposed'. Move it to ui-glossary-proposed.json",
            "ui-glossary.json term 'Cell' has the status 'decide'. Move it to ui-glossary-proposed.json",
        ], GlossaryProblems(approved.RootElement, proposed.RootElement));
    }

    [Fact]
    public void An_approved_term_in_the_proposed_glossary_is_reported()
    {
        using var approved = JsonDocument.Parse("""{"terms":[]}""");
        using var proposed = JsonDocument.Parse("""{"terms":[{"concept":"Job","status":"approved"}]}""");

        Assert.Equal(
            ["ui-glossary-proposed.json term 'Job' has the status 'approved'. Move it to ui-glossary.json"],
            GlossaryProblems(approved.RootElement, proposed.RootElement));
    }

    [Fact]
    public void Glossary_files_that_hold_each_term_by_its_status_report_nothing()
    {
        using var approved = JsonDocument.Parse("""{"terms":[{"concept":"Role","status":"approved"}]}""");
        using var proposed = JsonDocument.Parse("""{"terms":[{"concept":"Job","status":"proposed"},{"concept":"Cell","status":"decide"}]}""");

        Assert.Empty(GlossaryProblems(approved.RootElement, proposed.RootElement));
    }

    [Fact]
    public void A_locale_that_loses_a_placeholder_of_english_is_reported()
    {
        var english = new Dictionary<string, string>(StringComparer.Ordinal) { ["count.label"] = "Delete {count} items", ["plain"] = "Plain" };
        var other = new Dictionary<string, string>(StringComparer.Ordinal) { ["count.label"] = "Xóa mục", ["plain"] = "Đơn giản" };

        Assert.Equal(["count.label"], PlaceholderBreaks(english, other));
    }

    [Fact]
    public void A_locale_that_adds_a_placeholder_english_lacks_is_reported()
    {
        var english = new Dictionary<string, string>(StringComparer.Ordinal) { ["count.label"] = "Items", ["plain"] = "Plain" };
        var other = new Dictionary<string, string>(StringComparer.Ordinal) { ["count.label"] = "Mục {count}", ["plain"] = "Đơn giản" };

        Assert.Equal(["count.label"], PlaceholderBreaks(english, other));
    }

    [Fact]
    public void An_untranslated_text_is_reported_and_text_of_kept_terms_passes()
    {
        var english = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["email"] = "Email",
            ["webhook"] = "Webhook",
            ["api.key"] = "API key",
            ["studio"] = "Studio",
            ["axis.codes"] = "{count} AXC0035 Axis",
        };
        var other = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["email"] = "Email",
            ["webhook"] = "Webhook",
            ["api.key"] = "API key",
            ["studio"] = "Studio",
            ["axis.codes"] = "{count} AXC0035 Axis",
        };

        Assert.Equal(["email"], UntranslatedKeys(english, other, ReadKeepWords()));
    }

    [Fact]
    public void A_translated_text_is_not_an_untranslated_break()
    {
        var english = new Dictionary<string, string>(StringComparer.Ordinal) { ["email"] = "Email" };
        var other = new Dictionary<string, string>(StringComparer.Ordinal) { ["email"] = "Thư điện tử" };

        Assert.Empty(UntranslatedKeys(english, other, ReadKeepWords()));
    }

    [Fact]
    public void The_baseline_reports_a_new_break_and_a_key_that_no_longer_breaks()
    {
        var problems = CompareWithBaseline("untranslated", "vi", ["a", "c"], ["a", "b"]);

        Assert.Equal(
        [
            "Locale 'vi' key 'c' breaks the untranslated rule and is not in the baseline",
            "Locale 'vi' key 'b' no longer breaks the untranslated rule. Remove it from tests/Axis.Server.Tests/TextCatalogBaseline.json",
        ], problems);
    }

    [Fact]
    public void The_baseline_reports_nothing_when_it_matches_the_breaks()
    {
        Assert.Empty(CompareWithBaseline("placeholders", "vi", ["a", "b"], ["a", "b"]));
    }

    internal static IReadOnlyList<string> PlaceholderBreaks(IReadOnlyDictionary<string, string> english, IReadOnlyDictionary<string, string> other)
    {
        return english.Keys
            .Where(key => other.ContainsKey(key) && !Placeholders(english[key]).SetEquals(Placeholders(other[key])))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    internal static IReadOnlyList<string> UntranslatedKeys(
        IReadOnlyDictionary<string, string> english,
        IReadOnlyDictionary<string, string> other,
        IReadOnlySet<string> keepWords)
    {
        return english.Keys
            .Where(key => other.ContainsKey(key)
                && string.Equals(other[key], english[key], StringComparison.Ordinal)
                && Words(other[key]).Any(word => !keepWords.Contains(word)))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    internal static IReadOnlyList<string> GlossaryProblems(JsonElement approved, JsonElement proposed)
    {
        const string approvedFile = "ui-glossary.json";
        const string proposedFile = "ui-glossary-proposed.json";
        var problems = new List<string>();
        var approvedConcepts = new List<string>();
        var proposedConcepts = new List<string>();

        foreach (var term in approved.GetProperty("terms").EnumerateArray())
        {
            var concept = term.GetProperty("concept").GetString()!;
            approvedConcepts.Add(concept);
            var status = term.GetProperty("status").GetString();
            if (status != "approved")
            {
                problems.Add($"{approvedFile} term '{concept}' has the status '{status}'. Move it to {proposedFile}");
            }
        }

        foreach (var term in proposed.GetProperty("terms").EnumerateArray())
        {
            var concept = term.GetProperty("concept").GetString()!;
            proposedConcepts.Add(concept);
            var status = term.GetProperty("status").GetString();
            if (status is not ("proposed" or "decide"))
            {
                problems.Add($"{proposedFile} term '{concept}' has the status '{status}'. Move it to {approvedFile}");
            }
        }

        foreach (var concept in proposedConcepts.Where(candidate => approvedConcepts.Contains(candidate, StringComparer.OrdinalIgnoreCase)))
        {
            problems.Add($"Concept '{concept}' is in both {approvedFile} and {proposedFile}");
        }

        return problems;
    }

    internal static IReadOnlySet<string> KeepWords(JsonElement glossary)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keep = glossary.GetProperty("keep");
        foreach (var section in new[] { "names", "integration" })
        {
            foreach (var entry in keep.GetProperty(section).EnumerateArray())
            {
                var parts = entry.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length is 1 or 2)
                {
                    words.UnionWith(parts);
                }
            }
        }

        return words;
    }

    internal static IReadOnlyList<string> CompareWithBaseline(string rule, string locale, IReadOnlyList<string> now, IReadOnlyList<string> baseline)
    {
        var problems = new List<string>();
        foreach (var key in now.Except(baseline, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            problems.Add($"Locale '{locale}' key '{key}' breaks the {rule} rule and is not in the baseline");
        }
        foreach (var key in baseline.Except(now, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            problems.Add($"Locale '{locale}' key '{key}' no longer breaks the {rule} rule. Remove it from tests/Axis.Server.Tests/TextCatalogBaseline.json");
        }

        return problems;
    }

    private static HashSet<string> Placeholders(string text) =>
        _placeholder.Matches(text).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> Words(string text)
    {
        var withoutCodes = _axcCode.Replace(_placeholder.Replace(text, " "), " ");
        return _wordSeparator.Split(withoutCodes).Where(word => word.Any(char.IsLetter));
    }

    private static IReadOnlySet<string> ReadKeepWords()
    {
        using var glossary = ReadGlossary("ui-glossary.json");
        return KeepWords(glossary.RootElement);
    }

    private static JsonDocument ReadGlossary(string fileName) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "reference", fileName)));

    private static TextCatalogBaseline ReadBaseline()
    {
        var path = Path.Combine(RepositoryRoot(), "tests", "Axis.Server.Tests", "TextCatalogBaseline.json");
        return JsonSerializer.Deserialize<TextCatalogBaseline>(File.ReadAllText(path), _baselineOptions)
            ?? throw new InvalidOperationException($"'{path}' is empty.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Axis.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Axis.slnx was not found above the test output folder.");
    }

    private sealed record TextCatalogBaseline(Dictionary<string, List<string>> Placeholders, Dictionary<string, List<string>> Untranslated);
}
