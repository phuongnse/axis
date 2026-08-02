using Axis.Rules.Application.Queries.ProjectRuleCondition;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Rules.Application.Tests.Queries;

public sealed class ProjectRuleConditionHandlerTests
{
    private readonly RuleDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task Project_WhenConditionUsesInputLabels_ReturnsCanonicalKeysAndReadableLabels()
    {
        ProjectRuleConditionHandler sut = new(
            _context.CurrentUser,
            new RuleConditionProjectionService());

        Result<RuleConditionProjectionDto> result = await sut.Handle(
            new ProjectRuleConditionQuery(new ProjectRuleConditionRequest(
                1,
                RuleDefinitionHandlerTestContext.DraftInputsDto(),
                RuleDefinitionHandlerTestContext.ConditionDto(),
                "en")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Condition.Left!.Reference.Should().NotBe("Value");
        result.Value.Condition.Right!.Reference.Should().NotBe("Threshold");
        result.Value.Display.Tokens.Select(token => token.Text)
            .Should().Contain(["Value", "is greater than", "Threshold"]);
    }

    [Theory]
    [InlineData("en", "is greater than or equal to", "is less than or equal to", "when", "is specified")]
    [InlineData("vi", "lớn hơn hoặc bằng", "nhỏ hơn hoặc bằng", "khi", "được chỉ định")]
    public async Task Project_WhenConditionHasOptionalBounds_RendersConditionalAssertions(
        string language,
        string minimumOperator,
        string maximumOperator,
        string when,
        string specified)
    {
        ProjectRuleConditionHandler sut = new(
            _context.CurrentUser,
            new RuleConditionProjectionService());

        Result<RuleConditionProjectionDto> result = await sut.Handle(
            new ProjectRuleConditionQuery(new ProjectRuleConditionRequest(
                1,
                OptionalRangeInputs(),
                OptionalRangeCondition(),
                language)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Display.Children.Should().HaveCount(2);
        result.Value.Display.Children.Should().OnlyContain(child => child.Children.Count == 0);
        result.Value.Display.Children[0].Tokens.Select(token => token.Text)
            .Should().Equal("Value", minimumOperator, "Minimum", when, "Minimum", specified);
        result.Value.Display.Children[1].Tokens.Select(token => token.Text)
            .Should().Equal("Value", maximumOperator, "Maximum", when, "Maximum", specified);
    }

    [Theory]
    [InlineData("en", "when")]
    [InlineData("vi", "khi")]
    public void Project_WhenOptionalPatternContainsNestedConditions_RendersOneConditionalExpression(
        string language,
        string when)
    {
        RuleConditionProjectionService sut = new();

        Result<RuleConditionProjectionDto> result = sut.Project(
            new ProjectRuleConditionRequest(
                1,
                OptionalRangeInputs(),
                NestedOptionalCondition(),
                language));

        result.IsSuccess.Should().BeTrue();
        result.Value.Display.Children.Should().HaveCount(0);
        result.Value.Display.Tokens.Select(token => token.Text)
            .Should().Equal(
                "(",
                "Value",
                language == "vi" ? "lớn hơn hoặc bằng" : "is greater than or equal to",
                "Minimum",
                language == "vi" ? "và" : "and",
                "Value",
                language == "vi" ? "nhỏ hơn hoặc bằng" : "is less than or equal to",
                "Maximum",
                ")",
                when,
                "Minimum",
                language == "vi" ? "được chỉ định" : "is specified");
    }

    [Theory]
    [InlineData("en", "and", "when", "are specified")]
    [InlineData("vi", "và", "khi", "được chỉ định")]
    public void Project_WhenOptionalPatternHasSeveralOptionalGuards_CombinesGuardList(
        string language,
        string and,
        string when,
        string specified)
    {
        RuleConditionProjectionService sut = new();

        Result<RuleConditionProjectionDto> result = sut.Project(
            new ProjectRuleConditionRequest(
                1,
                OptionalRangeInputs(),
                MultipleOptionalGuardsCondition(),
                language));

        result.IsSuccess.Should().BeTrue();
        result.Value.Display.Children.Should().BeEmpty();
        result.Value.Display.Tokens.Select(token => token.Text)
            .Should().Equal(
                "(",
                "Value",
                language == "vi" ? "lớn hơn hoặc bằng" : "is greater than or equal to",
                "Minimum",
                and,
                "Value",
                language == "vi" ? "nhỏ hơn hoặc bằng" : "is less than or equal to",
                "Maximum",
                ")",
                when,
                "Minimum",
                and,
                "Maximum",
                specified);
    }

    [Fact]
    public void Project_WhenOptionalPatternReferencesBoundThroughFunction_RendersConditionalExpression()
    {
        RuleConditionProjectionService sut = new();
        RuleOperandDto lengthOfValue = new(
            RuleOperandKind.Function,
            Reference: null,
            Literal: null,
            Function: RuleExpressionFunction.Length,
            Arguments: [new(RuleOperandKind.Input, "Value", Literal: null)]);
        RuleConditionNodeDto condition = new(
            "length-bound",
            LogicalOperator: RuleLogicalOperator.Any,
            PredicateOperator: null,
            Left: null,
            Right: null,
            Children:
            [
                new(
                    "limit-absent",
                    LogicalOperator: null,
                    PredicateOperator: RulePredicateOperator.IsNull,
                    Left: new(RuleOperandKind.Input, "Limit", Literal: null),
                    Right: null,
                    Children: []),
                new(
                    "length-satisfied",
                    LogicalOperator: null,
                    PredicateOperator: RulePredicateOperator.GreaterThanOrEqual,
                    Left: lengthOfValue,
                    Right: new(RuleOperandKind.Input, "Limit", Literal: null),
                    Children: []),
            ]);

        Result<RuleConditionProjectionDto> result = sut.Project(
            new ProjectRuleConditionRequest(
                1,
                [
                    new("Value", [RuleValueType.Text], true, false, []),
                    new("Limit", [RuleValueType.Integer], false, false, []),
                ],
                condition,
                "en"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Display.Tokens.Select(token => token.Text)
            .Should().Equal(
                "Length of",
                "Value",
                "is greater than or equal to",
                "Limit",
                "when",
                "Limit",
                "is specified");
    }

    [Fact]
    public void Project_WhenAnyGroupContainsSeveralUnrelatedBranches_PreservesStructuralFallback()
    {
        RuleConditionProjectionService sut = new();

        Result<RuleConditionProjectionDto> result = sut.Project(
            new ProjectRuleConditionRequest(
                1,
                OptionalRangeInputs(),
                UnsupportedOptionalCondition(),
                "en"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Display.Tokens.Select(token => token.Text)
            .Should().Equal("Any condition may match");
        result.Value.Display.Children.Should().HaveCount(3);
        result.Value.Display.Children.Should().OnlyContain(child => child.Children.Count == 0);
    }

    [Fact]
    public void Project_WhenOptionalPatternIsNegated_PreservesNotGroupAroundConditionalChild()
    {
        RuleConditionProjectionService sut = new();

        Result<RuleConditionProjectionDto> result = sut.Project(
            new ProjectRuleConditionRequest(
                1,
                OptionalRangeInputs(),
                new(
                    "negated-range",
                    LogicalOperator: RuleLogicalOperator.Not,
                    PredicateOperator: null,
                    Left: null,
                    Right: null,
                    Children: [OptionalBound("minimum-bound", "Minimum", RulePredicateOperator.GreaterThanOrEqual)]),
                "en"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Display.Tokens.Select(token => token.Text)
            .Should().Equal("This must not match");
        result.Value.Display.Children.Should().ContainSingle();
        result.Value.Display.Children[0].Children.Should().BeEmpty();
        result.Value.Display.Children[0].Tokens.Select(token => token.Text)
            .Should().Equal(
                "Value",
                "is greater than or equal to",
                "Minimum",
                "when",
                "Minimum",
                "is specified");
    }

    [Fact]
    public async Task Project_WhenConditionChecksMissingInput_PreservesNegativePresenceExpression()
    {
        ProjectRuleConditionHandler sut = new(
            _context.CurrentUser,
            new RuleConditionProjectionService());
        RuleConditionNodeDto condition = new(
            "value-missing",
            LogicalOperator: null,
            PredicateOperator: RulePredicateOperator.IsNull,
            Left: new(RuleOperandKind.Input, "Value", Literal: null),
            Right: null,
            Children: []);

        Result<RuleConditionProjectionDto> result = await sut.Handle(
            new ProjectRuleConditionQuery(new ProjectRuleConditionRequest(
                1,
                [new("Value", [RuleValueType.Date], false, false, [])],
                condition,
                "en")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Display.Tokens.Select(token => token.Text)
            .Should().Equal("Value", "is not provided");
    }

    [Fact]
    public async Task Project_WhenConditionReferencesUnknownLabel_ReturnsValidationProblem()
    {
        ProjectRuleConditionHandler sut = new(
            _context.CurrentUser,
            new RuleConditionProjectionService());
        RuleConditionNodeDto invalid = RuleDefinitionHandlerTestContext.ConditionDto() with
        {
            Right = new RuleOperandDto(RuleOperandKind.Input, "Limit", Literal: null),
        };

        Result<RuleConditionProjectionDto> result = await sut.Handle(
            new ProjectRuleConditionQuery(new ProjectRuleConditionRequest(
                1,
                RuleDefinitionHandlerTestContext.DraftInputsDto(),
                invalid,
                "en")),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ProblemCode.Should().Be(RulesProblemCodes.DefinitionInvalid);
    }

    private static IReadOnlyList<RuleDraftInputDefinitionDto> OptionalRangeInputs() =>
    [
        new("Value", [RuleValueType.Date], true, false, []),
        new("Minimum", [RuleValueType.Date], false, false, []),
        new("Maximum", [RuleValueType.Date], false, false, []),
    ];

    private static RuleConditionNodeDto OptionalRangeCondition() => new(
        "date-range",
        LogicalOperator: RuleLogicalOperator.All,
        PredicateOperator: null,
        Left: null,
        Right: null,
        Children:
        [
            OptionalBound(
                "minimum-bound",
                "Minimum",
                RulePredicateOperator.GreaterThanOrEqual),
            OptionalBound(
                "maximum-bound",
                "Maximum",
                RulePredicateOperator.LessThanOrEqual),
        ]);

    private static RuleConditionNodeDto OptionalBound(
        string nodeId,
        string bound,
        RulePredicateOperator comparison) => new(
        nodeId,
        LogicalOperator: RuleLogicalOperator.Any,
        PredicateOperator: null,
        Left: null,
        Right: null,
        Children:
        [
            new(
                $"{nodeId}-absent",
                LogicalOperator: null,
                PredicateOperator: RulePredicateOperator.IsNull,
                Left: new(RuleOperandKind.Input, bound, Literal: null),
                Right: null,
                Children: []),
            new(
                $"{nodeId}-satisfied",
                LogicalOperator: null,
                PredicateOperator: comparison,
                Left: new(RuleOperandKind.Input, "Value", Literal: null),
                Right: new(RuleOperandKind.Input, bound, Literal: null),
                Children: []),
        ]);

    private static RuleConditionNodeDto NestedOptionalCondition() => new(
        "nested-optional",
        LogicalOperator: RuleLogicalOperator.Any,
        PredicateOperator: null,
        Left: null,
        Right: null,
        Children:
        [
            new(
                "minimum-absent",
                LogicalOperator: null,
                PredicateOperator: RulePredicateOperator.IsNull,
                Left: new(RuleOperandKind.Input, "Minimum", Literal: null),
                Right: null,
                Children: []),
            new(
                "nested-comparisons",
                LogicalOperator: RuleLogicalOperator.All,
                PredicateOperator: null,
                Left: null,
                Right: null,
                Children:
                [
                    new(
                        "minimum-satisfied",
                        LogicalOperator: null,
                        PredicateOperator: RulePredicateOperator.GreaterThanOrEqual,
                        Left: new(RuleOperandKind.Input, "Value", Literal: null),
                        Right: new(RuleOperandKind.Input, "Minimum", Literal: null),
                        Children: []),
                    new(
                        "maximum-satisfied",
                        LogicalOperator: null,
                        PredicateOperator: RulePredicateOperator.LessThanOrEqual,
                        Left: new(RuleOperandKind.Input, "Value", Literal: null),
                        Right: new(RuleOperandKind.Input, "Maximum", Literal: null),
                        Children: []),
                ]),
        ]);

    private static RuleConditionNodeDto UnsupportedOptionalCondition() => new(
        "unsupported-optional",
        LogicalOperator: RuleLogicalOperator.Any,
        PredicateOperator: null,
        Left: null,
        Right: null,
        Children:
        [
            new(
                "minimum-absent",
                LogicalOperator: null,
                PredicateOperator: RulePredicateOperator.IsNull,
                Left: new(RuleOperandKind.Input, "Minimum", Literal: null),
                Right: null,
                Children: []),
            new(
                "minimum-satisfied",
                LogicalOperator: null,
                PredicateOperator: RulePredicateOperator.GreaterThanOrEqual,
                Left: new(RuleOperandKind.Input, "Value", Literal: null),
                Right: new(RuleOperandKind.Input, "Minimum", Literal: null),
                Children: []),
            new(
                "maximum-satisfied",
                LogicalOperator: null,
                PredicateOperator: RulePredicateOperator.LessThanOrEqual,
                Left: new(RuleOperandKind.Input, "Value", Literal: null),
                Right: new(RuleOperandKind.Input, "Maximum", Literal: null),
                Children: []),
        ]);

    private static RuleConditionNodeDto MultipleOptionalGuardsCondition() => new(
        "multiple-optional-guards",
        LogicalOperator: RuleLogicalOperator.Any,
        PredicateOperator: null,
        Left: null,
        Right: null,
        Children:
        [
            new(
                "minimum-absent",
                LogicalOperator: null,
                PredicateOperator: RulePredicateOperator.IsNull,
                Left: new(RuleOperandKind.Input, "Minimum", Literal: null),
                Right: null,
                Children: []),
            new(
                "maximum-absent",
                LogicalOperator: null,
                PredicateOperator: RulePredicateOperator.IsNull,
                Left: new(RuleOperandKind.Input, "Maximum", Literal: null),
                Right: null,
                Children: []),
            new(
                "bounded-range",
                LogicalOperator: RuleLogicalOperator.All,
                PredicateOperator: null,
                Left: null,
                Right: null,
                Children:
                [
                    new(
                        "minimum-satisfied",
                        LogicalOperator: null,
                        PredicateOperator: RulePredicateOperator.GreaterThanOrEqual,
                        Left: new(RuleOperandKind.Input, "Value", Literal: null),
                        Right: new(RuleOperandKind.Input, "Minimum", Literal: null),
                        Children: []),
                    new(
                        "maximum-satisfied",
                        LogicalOperator: null,
                        PredicateOperator: RulePredicateOperator.LessThanOrEqual,
                        Left: new(RuleOperandKind.Input, "Value", Literal: null),
                        Right: new(RuleOperandKind.Input, "Maximum", Literal: null),
                        Children: []),
                ]),
        ]);
}
