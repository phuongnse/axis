using Axis.Rules.Application.Queries.GetRuleDefinition;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Rules.Application.Tests.Queries;

public sealed class GetRuleDefinitionHandlerTests
{
    private readonly RuleDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task Get_WhenDefinitionExists_ReturnsDetail()
    {
        Axis.Rules.Domain.RuleDefinition definition = RuleDefinitionHandlerTestContext.DraftDefinition();
        _context.Repository.GetByKeyForWorkspaceAsync(
                definition.Key,
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        GetRuleDefinitionHandler sut = new(_context.CurrentUser, _context.Repository);

        Result<RuleDefinitionDetailDto> result = await sut.Handle(
            new GetRuleDefinitionQuery(definition.Key.Value),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DefinitionKey.Should().Be(definition.Key.Value);
    }
}
