using Axis.Rules.Application.Queries.ListRuleDefinitions;
using Axis.Rules.Application.Search;
using Axis.Rules.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using DomainRuleDefinition = Axis.Rules.Domain.RuleDefinition;
using DomainRuleDefinitionKey = Axis.Rules.Domain.RuleDefinitionKey;

namespace Axis.Rules.Application.Tests.Queries;

public sealed class ListRuleDefinitionsHandlerTests
{
    private readonly RuleDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task List_WhenWorkspaceIsMissing_ReturnsFailure()
    {
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.workspaceId.Returns((Guid?)null);
        ListRuleDefinitionsHandler sut = new(
            currentUser,
            _context.Repository,
            _context.CatalogSearch);

        Result<PagedResult<RuleDefinitionSummaryDto>> result = await sut.Handle(
            new ListRuleDefinitionsQuery(Page: 1, PageSize: 20),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task List_WhenOriginIsSystem_ReturnsOnlySystemDefinitions()
    {
        ListRuleDefinitionsHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.CatalogSearch);

        Result<PagedResult<RuleDefinitionSummaryDto>> result = await sut.Handle(
            new ListRuleDefinitionsQuery(Page: 1, PageSize: 20, Origin: RuleOrigin.System),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().NotBeEmpty()
            .And.OnlyContain(definition => definition.Origin == RuleOrigin.System);
    }

    [Fact]
    public async Task Search_WhenOriginsAreMixed_PreservesProviderRankingAndPaging()
    {
        DomainRuleDefinition workspaceDefinition = RuleDefinitionHandlerTestContext.DraftDefinition();
        _context.CatalogSearch.SearchAsync(
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<IReadOnlyList<RuleTextSearchDocument>>(),
                true,
                null,
                null,
                0,
                20,
                "required",
                Arg.Any<CancellationToken>())
            .Returns(new RuleCatalogSearchPage(
                [
                    new RuleCatalogSearchItem(Axis.Rules.Domain.RuleOrigin.Workspace, workspaceDefinition.Key.Value),
                    new RuleCatalogSearchItem(Axis.Rules.Domain.RuleOrigin.System, "field.required"),
                ],
                2));
        _context.Repository.ListByKeysForWorkspaceAsync(
                Arg.Any<IReadOnlyList<DomainRuleDefinitionKey>>(),
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns([workspaceDefinition]);
        ListRuleDefinitionsHandler sut = new(
            _context.CurrentUser,
            _context.Repository,
            _context.CatalogSearch);

        Result<PagedResult<RuleDefinitionSummaryDto>> result = await sut.Handle(
            new ListRuleDefinitionsQuery(
                Page: 1,
                PageSize: 20,
                SearchQuery: "required",
                Language: "en"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Select(item => item.Origin).Should().Equal(
            RuleOrigin.Workspace,
            RuleOrigin.System);
    }
}
