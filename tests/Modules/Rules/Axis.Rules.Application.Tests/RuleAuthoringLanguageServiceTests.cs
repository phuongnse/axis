using Axis.Rules.Application;
using Axis.Rules.Contracts;
using FluentAssertions;

namespace Axis.Rules.Application.Tests;

public sealed class RuleAuthoringLanguageServiceTests
{
    private readonly RuleAuthoringLanguageService _sut = new();
    private static readonly RuleInputDefinitionDto[] Inputs = [new("amount", "Amount", [RuleValueType.Decimal], true, false, [])];

    [Fact]
    public void Project_ValidExpression_ParsesFormatsExplainsAndNormalizesNodeIds()
    {
        RuleAuthoringProjectionDto result = _sut.Project(new("all(greaterThan(input(\"amount\"), decimal(10.0)), not(isNull(input(\"amount\"))))"), Inputs, 1, "vi");
        result.IsValid.Should().BeTrue();
        result.FormattedDsl.Should().Be("all(greaterThan(input(\"amount\"), decimal(10.0)), not(isNull(input(\"amount\"))))");
        result.Condition!.NodeId.Should().Be("0");
        result.Condition.Children.Select(child => child.NodeId).Should().Equal("0.0", "0.1");
        result.Explanation!.Tokens.Single().Text.Should().Be("Tất cả điều kiện đều đúng");
    }

    [Fact]
    public void Project_UnknownInput_ReturnsDiagnostic()
    {
        RuleAuthoringProjectionDto result = _sut.Project(new("equal(input(\"missing\"), integer(1))"), Inputs, 1, "en");
        result.IsValid.Should().BeFalse();
        result.Diagnostics.Single().Code.Should().Be("authoring.semantic");
    }

    [Fact]
    public void Project_MalformedText_ReturnsNoAst()
    {
        RuleAuthoringProjectionDto result = _sut.Project(new("greaterThan(input(\"amount\"),"), Inputs, 1, "en");
        result.Condition.Should().BeNull();
        result.Diagnostics.Single().Code.Should().Be("authoring.syntax");
    }

    [Fact]
    public void Complete_CursorAwareRequest_ReturnsDeterministicBoundedSuggestions()
    {
        IReadOnlyList<RuleAuthoringCompletionDto> result = _sut.Complete("greater", 7, Inputs, 1);
        result.Should().ContainSingle(item => item.Label == "greaterThan" && item.Start == 0 && item.Length == 7);
        result.Should().OnlyContain(item => item.Label.StartsWith("greater", StringComparison.OrdinalIgnoreCase));
        result.Should().BeInAscendingOrder(item => item.Label, StringComparer.Ordinal);
        result.Count.Should().BeLessThanOrEqualTo(50);
    }

    [Fact]
    public void Project_UnsupportedLanguageVersion_ReturnsDiagnostic()
    {
        RuleAuthoringProjectionDto result = _sut.Project(new("isNull(input(\"amount\"))"), Inputs, 2, "en");
        result.Diagnostics.Single().Code.Should().Be("authoring.version");
        _sut.Complete("", 0, Inputs, 2).Should().BeEmpty();
    }

    [Fact]
    public void Project_TypedLiterals_FormatsAndEscapesJsonText()
    {
        RuleAuthoringProjectionDto result = _sut.Project(new(
            "all(isNull(text(\"line\\n\\\"quote\\\"\")), isNull(integer(\"1\")), isNull(decimal(1.20)), isNull(date(\"2026-01-02\")), isNull(datetime(\"2026-01-02T03:04:05Z\")), isNull(boolean(true)))"),
            Inputs,
            1,
            "en");

        result.IsValid.Should().BeTrue();
        result.FormattedDsl.Should().Be(
            "all(isNull(text(\"line\\n\\\"quote\\\"\")), isNull(integer(1)), isNull(decimal(1.20)), isNull(date(\"2026-01-02\")), isNull(datetime(\"2026-01-02T03:04:05.0000000+00:00\")), isNull(boolean(true)))");
    }

    [Fact]
    public void Project_OversizedSource_RejectsBeforeParsing()
    {
        RuleAuthoringProjectionDto result = _sut.Project(new(new string('x', (32 * 1024) + 1)), Inputs, 1, "en");
        result.Condition.Should().BeNull();
        result.Diagnostics.Single().Code.Should().Be("authoring.limit");
    }
}
