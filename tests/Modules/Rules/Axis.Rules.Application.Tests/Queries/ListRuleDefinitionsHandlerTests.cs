using Axis.Rules.Application.Queries.ListRuleDefinitions;
using Axis.Rules.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Rules.Application.Tests.Queries;

public sealed class ListRuleDefinitionsHandlerTests
{
    private readonly RuleDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task List_WhenOriginIsSystem_ReturnsOnlySystemDefinitions()
    {
        ListRuleDefinitionsHandler sut = new(_context.CurrentUser, _context.Repository);

        Result<PagedResult<RuleDefinitionSummaryDto>> result = await sut.Handle(
            new ListRuleDefinitionsQuery(Page: 1, PageSize: 20, Origin: RuleOrigin.System),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().NotBeEmpty()
            .And.OnlyContain(definition => definition.Origin == RuleOrigin.System);
    }
}
