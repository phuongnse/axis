using Axis.Expressions.Functions;
using Axis.Presentation.Texts;
using Axis.Server.Studio;

namespace Axis.Server.Tests;

public sealed class ExpressionFunctionTextsTests
{
    private readonly EmbeddedTextResourceProvider _provider = new();

    [Fact]
    public void Every_function_and_aggregate_has_a_description_in_every_platform_locale()
    {
        var problems = new List<string>();

        foreach (var locale in _provider.Locales)
        {
            var texts = _provider.GetTexts(locale)!.Texts;
            foreach (var name in ExpressionFunctions.Names.Concat(ExpressionFunctions.AggregateNames))
            {
                var key = StudioExpressionEndpoints.FunctionDescriptionKey(name);
                if (!texts.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
                {
                    problems.Add($"Locale '{locale}' has no description for '{name}' under '{key}'");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
