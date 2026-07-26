using System.Collections.ObjectModel;

namespace Axis.Rules.Domain;

public sealed record RuleReferenceContent(
    string DisplayName,
    string Summary,
    string Usage,
    IReadOnlyList<string> Examples);

public sealed record RuleReferenceDocumentation
{
    public RuleReferenceDocumentation(
        IReadOnlyDictionary<string, RuleReferenceContent> locales)
    {
        Locales = new ReadOnlyDictionary<string, RuleReferenceContent>(
            locales.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, RuleReferenceContent> Locales { get; }

    public bool IsComplete(params string[] requiredLocales) =>
        requiredLocales.All(locale =>
            Locales.TryGetValue(locale, out RuleReferenceContent? content) &&
            !string.IsNullOrWhiteSpace(content.DisplayName) &&
            !string.IsNullOrWhiteSpace(content.Summary) &&
            !string.IsNullOrWhiteSpace(content.Usage) &&
            content.Examples.Count > 0 &&
            content.Examples.All(example => !string.IsNullOrWhiteSpace(example)));

    public static RuleReferenceDocumentation Bilingual(
        string englishName,
        string englishSummary,
        string englishUsage,
        string englishExample,
        string vietnameseName,
        string vietnameseSummary,
        string vietnameseUsage,
        string vietnameseExample) =>
        new(
            new Dictionary<string, RuleReferenceContent>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    englishName,
                    englishSummary,
                    englishUsage,
                    [englishExample]),
                ["vi"] = new(
                    vietnameseName,
                    vietnameseSummary,
                    vietnameseUsage,
                    [vietnameseExample]),
            });
}
