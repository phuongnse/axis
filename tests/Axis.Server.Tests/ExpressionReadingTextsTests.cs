using Axis.Expressions.Functions;
using Axis.Expressions.Reading;
using Axis.Presentation.Texts;
using Axis.Server.Studio;

namespace Axis.Server.Tests;

public sealed class ExpressionReadingTextsTests
{
    private readonly EmbeddedTextResourceProvider _provider = new();

    [Fact]
    public void Every_word_of_the_reading_has_a_text_in_every_platform_locale()
    {
        var problems = new List<string>();

        foreach (var locale in _provider.Locales)
        {
            var texts = _provider.GetTexts(locale)!.Texts;
            foreach (var words in ExpressionReader.TextKeys)
            {
                var key = StudioExpressionEndpoints.ReadsKey(words);
                if (!texts.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
                {
                    problems.Add($"Locale '{locale}' has no text for '{words}' under '{key}'");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_function_has_reading_words_in_every_platform_locale()
    {
        var words = ExpressionFunctions.Names.Concat(ExpressionFunctions.AggregateNames)
            .Select(ExpressionReader.FunctionWords)
            .Append(ExpressionReader.CountWhereWords)
            .ToList();
        var problems = new List<string>();

        foreach (var locale in _provider.Locales)
        {
            var texts = _provider.GetTexts(locale)!.Texts;
            foreach (var word in words)
            {
                var key = StudioExpressionEndpoints.ReadsKey(word);
                if (!texts.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
                {
                    problems.Add($"Locale '{locale}' has no text for '{word}' under '{key}'");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_function_has_a_slot_count_for_its_words()
    {
        var missing = ExpressionFunctions.Names.Concat(ExpressionFunctions.AggregateNames)
            .Select(ExpressionReader.FunctionWords)
            .Where(words => !ExpressionReader.FunctionSlots.ContainsKey(words))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void The_texts_of_a_function_have_the_slots_the_reader_fills()
    {
        var problems = new List<string>();

        foreach (var locale in _provider.Locales)
        {
            var texts = _provider.GetTexts(locale)!.Texts;
            foreach (var (words, slots) in ExpressionReader.FunctionSlots)
            {
                var text = texts[StudioExpressionEndpoints.ReadsKey(words)];
                for (var slot = 0; slot < slots; slot++)
                {
                    if (!text.Contains($"{{{slot}}}", StringComparison.Ordinal))
                    {
                        problems.Add($"Locale '{locale}' text for '{words}' lacks '{{{slot}}}'");
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void The_texts_of_a_phrase_have_the_slots_the_reader_fills()
    {
        var problems = new List<string>();

        foreach (var locale in _provider.Locales)
        {
            var texts = _provider.GetTexts(locale)!.Texts;
            foreach (var words in ExpressionReader.OperatorWords.Where(words => words != "negate" && words is not ("is-true" or "is-false" or "not" or "is-null" or "is-not-null")))
            {
                var text = texts[StudioExpressionEndpoints.ReadsKey(words)];
                if (!text.Contains("{0}", StringComparison.Ordinal) || !text.Contains("{1}", StringComparison.Ordinal))
                {
                    problems.Add($"Locale '{locale}' text for '{words}' lacks '{{0}}' or '{{1}}'");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
