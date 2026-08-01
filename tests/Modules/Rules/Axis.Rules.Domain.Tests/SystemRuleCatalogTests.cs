using Axis.Rules.Domain;
using FluentAssertions;

namespace Axis.Rules.Domain.Tests;

public sealed class SystemRuleCatalogTests
{
    [Fact]
    public void Definitions_WhenRead_ReturnEnterpriseSystemRuleCatalog()
    {
        SystemRuleCatalog.Definitions.Select(definition => definition.Key.Value)
            .Should().Equal(
                "field.required",
                "field.numeric_range",
                "field.decimal_precision",
                "field.date_range",
                "field.datetime_range",
                "field.text_length",
                "field.text_pattern",
                "field.text_format",
                "field.choice_selection_count");
    }

    [Fact]
    public void Definitions_WhenRead_HaveVersionedNormalizedMetadata()
    {
        IReadOnlyList<RuleDefinition> definitions = SystemRuleCatalog.Definitions;

        definitions.Select(definition => (definition.Key.Value, definition.LatestPublishedVersion))
            .Should().OnlyHaveUniqueItems();
        definitions.Should().OnlyContain(definition =>
            definition.LatestPublishedVersion == 1 &&
            definition.Origin == RuleOrigin.System &&
            definition.Status == RuleLifecycleStatus.Published);
        definitions.Should().OnlyContain(definition =>
            definition.Inputs.Select(input => input.Key).Distinct(StringComparer.Ordinal).Count() ==
            definition.Inputs.Count);
        definitions.Should().OnlyContain(definition =>
            definition.Documentation!.Locales.Values.All(content =>
                content.Examples.All(example => example != definition.Key.Value)));

        RuleDefinition textFormat = definitions.Single(definition => definition.Key.Value == "field.text_format");
        RuleInputDefinition format = textFormat.Inputs.Single(input => input.Key == "format");
        format.Types.Should().Equal(RuleValueType.Text);
        format.AllowedValues.Should().Equal("Email", "Url", "Uuid");
    }

    [Theory]
    [MemberData(nameof(SatisfiedAssertions))]
    public void Definition_WhenAssertionIsSatisfied_ReturnsMatch(
        string definitionKey,
        IReadOnlyDictionary<string, RuleValue> inputs)
    {
        RuleDefinition definition = SystemRuleCatalog.Find(definitionKey, 1)!;

        RuleConditionEvaluator.Evaluate(
                definition.Condition!,
                inputs)
            .Value.IsMatch.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(UnsatisfiedAssertions))]
    public void Definition_WhenAssertionIsNotSatisfied_ReturnsNonMatch(
        string definitionKey,
        IReadOnlyDictionary<string, RuleValue> inputs)
    {
        RuleDefinition definition = SystemRuleCatalog.Find(definitionKey, 1)!;

        RuleConditionEvaluator.Evaluate(
                definition.Condition!,
                inputs)
            .Value.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Required_WhenValueIsAbsent_ReturnsNonMatch()
    {
        RuleDefinition required = SystemRuleCatalog.Find("field.required", 1)!;

        required.Inputs.Single(input => input.Key == "value").IsRequired.Should().BeFalse();
        RuleConditionEvaluator.Evaluate(
                required.Condition!,
                new Dictionary<string, RuleValue>(StringComparer.Ordinal))
            .Value.IsMatch.Should().BeFalse();
    }

    [Theory]
    [InlineData("field.numeric_range")]
    [InlineData("field.date_range")]
    [InlineData("field.datetime_range")]
    [InlineData("field.text_length")]
    [InlineData("field.choice_selection_count")]
    public void RangeDefinition_WhenRead_UsesDirectPositiveBoundAssertions(string definitionKey)
    {
        RuleConditionGroup range = SystemRuleCatalog.Find(definitionKey, 1)!.Condition!
            .Should().BeOfType<RuleConditionGroup>().Subject;

        range.Operator.Should().Be(RuleLogicalOperator.All);
        range.Children.Should().HaveCount(2);
        AssertOptionalBound(
            range.Children[0],
            RulePredicateOperator.GreaterThanOrEqual);
        AssertOptionalBound(
            range.Children[1],
            RulePredicateOperator.LessThanOrEqual);
    }

    [Fact]
    public void DecimalPrecision_WhenRead_UsesDirectPositiveLimitAssertions()
    {
        RuleConditionGroup precision = SystemRuleCatalog.Find("field.decimal_precision", 1)!.Condition!
            .Should().BeOfType<RuleConditionGroup>().Subject;

        precision.Operator.Should().Be(RuleLogicalOperator.All);
        precision.Children.Should().HaveCount(2);
        precision.Children.Cast<RulePredicateCondition>()
            .Should().OnlyContain(predicate =>
                predicate.Operator == RulePredicateOperator.LessThanOrEqual);
    }

    [Fact]
    public void Find_WhenVersionIsUnknown_ReturnsNull() =>
        SystemRuleCatalog.Find("field.required", version: 2).Should().BeNull();

    [Fact]
    public void Definitions_WhenCastToMutableCollections_RejectMutation()
    {
        RuleDefinition definition = SystemRuleCatalog.Definitions[0];

        Action mutateCatalog = () => ((IList<RuleDefinition>)SystemRuleCatalog.Definitions).Clear();
        Action mutateInputs = () => ((IList<RuleInputDefinition>)definition.Inputs).Clear();

        mutateCatalog.Should().Throw<NotSupportedException>();
        mutateInputs.Should().Throw<NotSupportedException>();
    }

    public static TheoryData<string, IReadOnlyDictionary<string, RuleValue>> SatisfiedAssertions() =>
        new()
        {
            { "field.required", Inputs(("value", Value(RuleValueType.Text, "Axis"))) },
            { "field.numeric_range", Inputs(("value", Value(RuleValueType.Integer, "12")), ("min", Value(RuleValueType.Decimal, "0"))) },
            { "field.decimal_precision", Inputs(("value", Value(RuleValueType.Decimal, "123.45")), ("precision", Value(RuleValueType.Integer, "5")), ("scale", Value(RuleValueType.Integer, "2"))) },
            { "field.date_range", Inputs(("value", Value(RuleValueType.Date, "2026-06-15")), ("max", Value(RuleValueType.Date, "2026-12-31"))) },
            { "field.datetime_range", Inputs(("value", Value(RuleValueType.DateTime, "2026-06-15T10:00:00Z"))) },
            { "field.text_length", Inputs(("value", Value(RuleValueType.Text, "Axis")), ("min", Value(RuleValueType.Integer, "2")), ("max", Value(RuleValueType.Integer, "8"))) },
            { "field.text_pattern", Inputs(("value", Value(RuleValueType.Text, "AX-123")), ("pattern", Value(RuleValueType.Text, "^AX-[0-9]+$"))) },
            { "field.text_format", Inputs(("value", Value(RuleValueType.Text, "axis@example.com")), ("format", Value(RuleValueType.Text, "Email"))) },
            { "field.choice_selection_count", Inputs(("value", Values(RuleValueType.Text, "one", "two")), ("min", Value(RuleValueType.Integer, "1")), ("max", Value(RuleValueType.Integer, "3"))) },
        };

    public static TheoryData<string, IReadOnlyDictionary<string, RuleValue>> UnsatisfiedAssertions() =>
        new()
        {
            { "field.required", Inputs(("value", Value(RuleValueType.Text, "   "))) },
            { "field.numeric_range", Inputs(("value", Value(RuleValueType.Integer, "-1")), ("min", Value(RuleValueType.Decimal, "0"))) },
            { "field.decimal_precision", Inputs(("value", Value(RuleValueType.Decimal, "123.456")), ("precision", Value(RuleValueType.Integer, "5")), ("scale", Value(RuleValueType.Integer, "2"))) },
            { "field.date_range", Inputs(("value", Value(RuleValueType.Date, "2027-01-01")), ("max", Value(RuleValueType.Date, "2026-12-31"))) },
            { "field.datetime_range", Inputs(("value", Value(RuleValueType.DateTime, "2025-12-31T23:59:59Z")), ("min", Value(RuleValueType.DateTime, "2026-01-01T00:00:00Z"))) },
            { "field.text_length", Inputs(("value", Value(RuleValueType.Text, "Axis Rules")), ("max", Value(RuleValueType.Integer, "4"))) },
            { "field.text_pattern", Inputs(("value", Value(RuleValueType.Text, "wrong")), ("pattern", Value(RuleValueType.Text, "^AX-[0-9]+$"))) },
            { "field.text_format", Inputs(("value", Value(RuleValueType.Text, "not-an-email")), ("format", Value(RuleValueType.Text, "Email"))) },
            { "field.choice_selection_count", Inputs(("value", Values(RuleValueType.Text, "one", "two", "three", "four")), ("max", Value(RuleValueType.Integer, "3"))) },
        };

    private static IReadOnlyDictionary<string, RuleValue> Inputs(
        params (string Key, RuleValue Value)[] inputs) =>
        inputs.ToDictionary(input => input.Key, input => input.Value, StringComparer.Ordinal);

    private static RuleValue Value(RuleValueType type, string value) =>
        RuleValue.Create(type, [value]).Value;

    private static RuleValue Values(RuleValueType type, params string[] values) =>
        RuleValue.Create(type, values, allowMultiple: true).Value;

    private static void AssertOptionalBound(
        RuleConditionNode node,
        RulePredicateOperator satisfiedOperator)
    {
        RuleConditionGroup bound = node.Should().BeOfType<RuleConditionGroup>().Subject;
        bound.Operator.Should().Be(RuleLogicalOperator.Any);
        bound.Children.Should().HaveCount(2);
        bound.Children[0].Should().BeOfType<RulePredicateCondition>()
            .Which.Operator.Should().Be(RulePredicateOperator.IsNull);
        bound.Children[1].Should().BeOfType<RulePredicateCondition>()
            .Which.Operator.Should().Be(satisfiedOperator);
    }
}
