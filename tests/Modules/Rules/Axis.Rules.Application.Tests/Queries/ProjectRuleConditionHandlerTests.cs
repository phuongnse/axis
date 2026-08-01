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
}
