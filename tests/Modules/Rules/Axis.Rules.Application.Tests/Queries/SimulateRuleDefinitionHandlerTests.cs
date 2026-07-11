using Axis.Rules.Application.Queries.SimulateRuleDefinition;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests.Queries;

public sealed class SimulateRuleDefinitionHandlerTests
{
    private readonly RuleDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task Simulate_WhenDraftConditionMatches_ReturnsOutcome()
    {
        Axis.Rules.Domain.RuleDefinition definition = RuleDefinitionHandlerTestContext.DraftDefinition(configured: true);
        _context.Repository.GetByKeyForWorkspaceAsync(
                definition.Key,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        SimulateRuleDefinitionHandler sut = new(
            _context.CurrentUser,
            _context.ContextRegistry,
            _context.Repository);

        Result<RuleSimulationResultDto> result = await sut.Handle(
            new SimulateRuleDefinitionQuery(
                definition.Key.Value,
                DefinitionVersion: null,
                new Dictionary<string, RuleValueDto>(StringComparer.Ordinal)
                {
                    ["threshold"] = new(RuleValueType.Decimal, ["10"]),
                },
                new Dictionary<string, RuleValueDto>(StringComparer.Ordinal)
                {
                    ["field.value"] = new(RuleValueType.Decimal, ["15"]),
                },
                "simulation-test"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsMatch.Should().BeTrue();
        result.Value.Outcome.Should().NotBeNull();
    }
}
