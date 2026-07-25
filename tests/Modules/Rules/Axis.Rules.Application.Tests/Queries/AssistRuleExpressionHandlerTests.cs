using Axis.Rules.Application.Queries.AssistRuleExpression;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Rules.Application.Tests.Queries;

public sealed class AssistRuleExpressionHandlerTests
{
    private readonly RuleDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task Assist_WhenSyntaxIsValid_ReturnsCanonicalConditionVietnameseDisplayAndCompletions()
    {
        RuleExpressionAuthoringService service = new(_context.ContextRegistry);
        AssistRuleExpressionHandler sut = new(_context.CurrentUser, service);

        Result<RuleExpressionAuthoringDto> result = await sut.Handle(
            new AssistRuleExpressionQuery(
                new AssistRuleExpressionRequest(
                    1,
                    RuleDefinitionHandlerTestContext.Schema.ContextKey,
                    RuleDefinitionHandlerTestContext.Schema.Version,
                    [new("threshold", RuleValueType.Decimal, true, false, [])],
                    "@context.field.value GreaterThan @parameters.threshold",
                    Condition: null,
                    CursorOffset: 5,
                    Language: "vi")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Diagnostics.Should().BeEmpty();
        result.Value.Condition!.PredicateOperator.Should().Be(RulePredicateOperator.GreaterThan);
        result.Value.Syntax.Should().Be("@context.field.value GreaterThan @parameters.threshold");
        result.Value.Display!.Tokens.Select(token => token.Text)
            .Should().Contain(["Field value", "lớn hơn", "threshold"]);
        result.Value.Display.Tokens.Single(
                token => token.ReferenceKind == RuleExpressionReferenceKind.PredicateOperator)
            .ReferenceKey.Should().Be(RulePredicateOperator.GreaterThan.ToString());
        result.Value.Condition.Left!.Kind.Should().Be(RuleOperandKind.Context);
        result.Value.Condition.Right!.Kind.Should().Be(RuleOperandKind.Parameter);
        result.Value.Completions.Should().Contain(completion =>
            completion.ReferenceKind == RuleExpressionReferenceKind.Context &&
            completion.InsertText == "@context.field.value");
    }

    [Fact]
    public async Task Assist_WhenSyntaxIsInvalid_PreservesTextAndReturnsSourceRange()
    {
        RuleExpressionAuthoringService service = new(_context.ContextRegistry);
        AssistRuleExpressionHandler sut = new(_context.CurrentUser, service);
        const string syntax = "@context.field.value Nope @parameters.threshold";

        Result<RuleExpressionAuthoringDto> result = await sut.Handle(
            new AssistRuleExpressionQuery(
                new AssistRuleExpressionRequest(
                    1,
                    RuleDefinitionHandlerTestContext.Schema.ContextKey,
                    RuleDefinitionHandlerTestContext.Schema.Version,
                    [new("threshold", RuleValueType.Decimal, true, false, [])],
                    syntax,
                    Condition: null,
                    CursorOffset: syntax.Length,
                    Language: "en")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Syntax.Should().Be(syntax);
        result.Value.Condition.Should().BeNull();
        result.Value.Diagnostics.Should().ContainSingle();
        result.Value.Diagnostics[0].Start.Should().Be(syntax.IndexOf("Nope", StringComparison.Ordinal));
        result.Value.Diagnostics[0].Length.Should().Be(4);
    }

    [Fact]
    public async Task Assist_WhenReferenceHasNoNamespace_ReturnsNamespaceExpectedDiagnostic()
    {
        RuleExpressionAuthoringService service = new(_context.ContextRegistry);
        AssistRuleExpressionHandler sut = new(_context.CurrentUser, service);
        const string syntax = "@field.value GreaterThan @parameters.threshold";

        Result<RuleExpressionAuthoringDto> result = await sut.Handle(
            new AssistRuleExpressionQuery(
                new AssistRuleExpressionRequest(
                    1,
                    RuleDefinitionHandlerTestContext.Schema.ContextKey,
                    RuleDefinitionHandlerTestContext.Schema.Version,
                    [new("threshold", RuleValueType.Decimal, true, false, [])],
                    syntax,
                    Condition: null,
                    CursorOffset: syntax.Length,
                    Language: "en")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Condition.Should().BeNull();
        result.Value.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be("rules.expression.reference_namespace_expected");
    }

    [Fact]
    public async Task Assist_WhenReferenceUsesWrongNamespace_ReturnsNamespaceSpecificDiagnostic()
    {
        RuleExpressionAuthoringService service = new(_context.ContextRegistry);
        AssistRuleExpressionHandler sut = new(_context.CurrentUser, service);
        const string syntax = "@context.threshold GreaterThan Decimal(\"1\")";

        Result<RuleExpressionAuthoringDto> result = await sut.Handle(
            new AssistRuleExpressionQuery(
                new AssistRuleExpressionRequest(
                    1,
                    RuleDefinitionHandlerTestContext.Schema.ContextKey,
                    RuleDefinitionHandlerTestContext.Schema.Version,
                    [new("threshold", RuleValueType.Decimal, true, false, [])],
                    syntax,
                    Condition: null,
                    CursorOffset: syntax.Length,
                    Language: "en")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Condition.Should().BeNull();
        result.Value.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be("rules.expression.context_unknown");
    }

    [Fact]
    public async Task Assist_WhenStringEndsWithEscapedQuote_ReturnsUnterminatedDiagnostic()
    {
        RuleExpressionAuthoringService service = new(_context.ContextRegistry);
        AssistRuleExpressionHandler sut = new(_context.CurrentUser, service);
        const string syntax = """@context.field.value Equal Text("abc\")""";

        Result<RuleExpressionAuthoringDto> result = await sut.Handle(
            new AssistRuleExpressionQuery(
                new AssistRuleExpressionRequest(
                    1,
                    RuleDefinitionHandlerTestContext.Schema.ContextKey,
                    RuleDefinitionHandlerTestContext.Schema.Version,
                    Parameters: [],
                    syntax,
                    Condition: null,
                    CursorOffset: syntax.Length,
                    Language: "en")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Condition.Should().BeNull();
        result.Value.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be("rules.expression.string_unterminated");
    }

    [Fact]
    public async Task Assist_WhenConditionIsCanonical_FormatsSyntaxAndLocalizedDisplay()
    {
        RuleExpressionAuthoringService service = new(_context.ContextRegistry);
        AssistRuleExpressionHandler sut = new(_context.CurrentUser, service);

        Result<RuleExpressionAuthoringDto> result = await sut.Handle(
            new AssistRuleExpressionQuery(
                new AssistRuleExpressionRequest(
                    1,
                    RuleDefinitionHandlerTestContext.Schema.ContextKey,
                    RuleDefinitionHandlerTestContext.Schema.Version,
                    [new("threshold", RuleValueType.Decimal, true, false, [])],
                    Syntax: null,
                    Condition: RuleDefinitionHandlerTestContext.ConditionDto(),
                    CursorOffset: 0,
                    Language: "en")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Syntax.Should().Be("@context.field.value GreaterThan @parameters.threshold");
        result.Value.Condition.Should().NotBeNull();
        result.Value.Display.Should().NotBeNull();
    }

    [Fact]
    public async Task Assist_WhenConditionComparesBooleanFunction_ProjectsNaturalLanguage()
    {
        RuleExpressionAuthoringService service = new(_context.ContextRegistry);
        AssistRuleExpressionHandler sut = new(_context.CurrentUser, service);
        RuleConditionNodeDto condition = new(
            "required",
            LogicalOperator: null,
            RulePredicateOperator.Equal,
            new RuleOperandDto(
                RuleOperandKind.Function,
                Reference: null,
                Literal: null,
                RuleExpressionFunction.IsBlank,
                [new RuleOperandDto(RuleOperandKind.Context, "field.value", Literal: null)]),
            new RuleOperandDto(
                RuleOperandKind.Literal,
                Reference: null,
                new RuleValueDto(RuleValueType.Boolean, ["true"])),
            []);

        Result<RuleExpressionAuthoringDto> result = await sut.Handle(
            new AssistRuleExpressionQuery(
                new AssistRuleExpressionRequest(
                    1,
                    ContextKey: null,
                    ContextSchemaVersion: null,
                    Parameters: [],
                    Syntax: null,
                    condition,
                    CursorOffset: 0,
                    Language: "en")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Display!.Tokens.Select(token => token.Text)
            .Should().Equal("Field value", "is blank");
        result.Value.Syntax.Should().Be("IsBlank(@context.field.value) Equal Boolean(\"true\")");
    }

    [Fact]
    public async Task Assist_WhenConditionContainsEvaluatorStructure_ProjectsEveryLogicalGroup()
    {
        RuleExpressionAuthoringService service = new(_context.ContextRegistry);
        AssistRuleExpressionHandler sut = new(_context.CurrentUser, service);
        RuleOperandDto minimum = new(RuleOperandKind.Parameter, "min", Literal: null);
        RuleOperandDto maximum = new(RuleOperandKind.Parameter, "max", Literal: null);
        RuleOperandDto value = new(
            RuleOperandKind.Function,
            Reference: null,
            Literal: null,
            RuleExpressionFunction.ToDecimal,
            [new RuleOperandDto(RuleOperandKind.Context, "field.value", Literal: null)]);
        RuleConditionNodeDto condition = new(
            "range",
            RuleLogicalOperator.Any,
            PredicateOperator: null,
            Left: null,
            Right: null,
            [
                new RuleConditionNodeDto(
                    "minimum",
                    RuleLogicalOperator.All,
                    PredicateOperator: null,
                    Left: null,
                    Right: null,
                    [
                        new RuleConditionNodeDto(
                            "minimum-set",
                            LogicalOperator: null,
                            RulePredicateOperator.IsNotNull,
                            minimum,
                            Right: null,
                            []),
                        new RuleConditionNodeDto(
                            "below-minimum",
                            LogicalOperator: null,
                            RulePredicateOperator.LessThan,
                            value,
                            minimum,
                            []),
                    ]),
                new RuleConditionNodeDto(
                    "maximum",
                    RuleLogicalOperator.All,
                    PredicateOperator: null,
                    Left: null,
                    Right: null,
                    [
                        new RuleConditionNodeDto(
                            "maximum-set",
                            LogicalOperator: null,
                            RulePredicateOperator.IsNotNull,
                            maximum,
                            Right: null,
                            []),
                        new RuleConditionNodeDto(
                            "above-maximum",
                            LogicalOperator: null,
                            RulePredicateOperator.GreaterThan,
                            value,
                            maximum,
                            []),
                    ]),
            ]);

        Result<RuleExpressionAuthoringDto> result = await sut.Handle(
            new AssistRuleExpressionQuery(
                new AssistRuleExpressionRequest(
                    1,
                    ContextKey: null,
                    ContextSchemaVersion: null,
                    [
                        new("min", RuleValueType.Decimal, false, false, []),
                        new("max", RuleValueType.Decimal, false, false, []),
                    ],
                    Syntax: null,
                    condition,
                    CursorOffset: 0,
                    Language: "en")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Display!.Tokens.Select(token => token.Text)
            .Should().Equal("or");
        result.Value.Display.Children.Should().HaveCount(2);
        result.Value.Display.Children.SelectMany(child => child.Tokens.Select(token => token.Text))
            .Should().Equal("and", "and");
        result.Value.Display.Children.Should().OnlyContain(child => child.Children.Count == 2);
        result.Value.Display.Children[0].Children[0].Tokens.Select(token => token.Text).Should().Equal(
            "min",
            "is provided");
        result.Value.Display.Children[0].Children[1].Tokens.Select(token => token.Text).Should().Equal(
            "Field value",
            "is less than",
            "min");
        result.Value.Display.Children[1].Children[0].Tokens.Select(token => token.Text).Should().Equal(
            "max",
            "is provided");
        result.Value.Display.Children[1].Children[1].Tokens.Select(token => token.Text).Should().Equal(
            "Field value",
            "is greater than",
            "max");
        result.Value.Display.Children[0].Children[0].Tokens.Single(token => token.Text == "is provided")
            .ReferenceKey.Should().Be(RulePredicateOperator.IsNotNull.ToString());
        result.Value.Display.Children[0].Children
            .SelectMany(child => child.Tokens)
            .Where(token => token.Text == "min")
            .Should().OnlyContain(token => token.ReferenceKind == RuleExpressionReferenceKind.Parameter);
        result.Value.Display.Children
            .SelectMany(child => child.Children)
            .SelectMany(child => child.Tokens)
            .Should().NotContain(token =>
                token.ReferenceKey == RuleExpressionFunction.ToDecimal.ToString());
    }

    [Fact]
    public async Task Assist_WhenConditionIsNegated_ProjectsInversionGroup()
    {
        RuleExpressionAuthoringService service = new(_context.ContextRegistry);
        AssistRuleExpressionHandler sut = new(_context.CurrentUser, service);
        RuleConditionNodeDto condition = new(
            "not-blank",
            RuleLogicalOperator.Not,
            PredicateOperator: null,
            Left: null,
            Right: null,
            [
                new RuleConditionNodeDto(
                    "blank",
                    LogicalOperator: null,
                    RulePredicateOperator.IsNull,
                    new RuleOperandDto(RuleOperandKind.Context, "field.value", Literal: null),
                    Right: null,
                    []),
            ]);

        Result<RuleExpressionAuthoringDto> result = await sut.Handle(
            new AssistRuleExpressionQuery(
                new AssistRuleExpressionRequest(
                    1,
                    ContextKey: null,
                    ContextSchemaVersion: null,
                    Parameters: [],
                    Syntax: null,
                    condition,
                    CursorOffset: 0,
                    Language: "en")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Display!.Tokens.Should().ContainSingle()
            .Which.ReferenceKey.Should().Be(RuleLogicalOperator.Not.ToString());
        result.Value.Display.Children.Should().ContainSingle();
        result.Value.Display.Children[0].Tokens.Select(token => token.Text)
            .Should().Equal("Field value", "has no value");
    }
}
