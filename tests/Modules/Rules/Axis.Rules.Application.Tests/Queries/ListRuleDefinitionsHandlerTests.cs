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
            _context.CurrentSubject,
            _context.Authorization,
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
            _context.CurrentSubject,
            _context.Authorization,
            _context.Repository,
            _context.CatalogSearch);

        Result<PagedResult<RuleDefinitionSummaryDto>> result = await sut.Handle(
            new ListRuleDefinitionsQuery(Page: 1, PageSize: 20, Origin: RuleOrigin.BuiltIn),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().NotBeEmpty()
            .And.OnlyContain(definition => definition.Origin == RuleOrigin.BuiltIn);
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
                null,
                0,
                20,
                "required",
                Arg.Any<CancellationToken>())
            .Returns(new RuleCatalogSearchPage(
                [
                    new RuleCatalogSearchItem(Axis.Rules.Domain.RuleOrigin.Workspace, workspaceDefinition.Key.Value),
                    new RuleCatalogSearchItem(Axis.Rules.Domain.RuleOrigin.BuiltIn, "field.required"),
                ],
                2));
        _context.Repository.ListByKeysForWorkspaceAsync(
                Arg.Any<IReadOnlyList<DomainRuleDefinitionKey>>(),
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns([workspaceDefinition]);
        ListRuleDefinitionsHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
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
            RuleOrigin.BuiltIn);
    }

    [Fact]
    public async Task Search_WhenNameSortIsExplicit_PassesLocalizedNameSortToWholeCatalogProvider()
    {
        _context.CatalogSearch.SearchAsync(
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Is<IReadOnlyList<RuleTextSearchDocument>>(documents =>
                    documents.Any(document =>
                        document.Key == RuleDefinitionKeys.Required &&
                        document.Title == "Giá trị bắt buộc")),
                true,
                null,
                RuleDefinitionSortField.Name,
                CollectionSortDirection.Descending,
                20,
                20,
                "value",
                Arg.Any<CancellationToken>())
            .Returns(new RuleCatalogSearchPage(
                [new RuleCatalogSearchItem(Axis.Rules.Domain.RuleOrigin.BuiltIn, RuleDefinitionKeys.Required)],
                21));
        ListRuleDefinitionsHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Repository,
            _context.CatalogSearch);

        Result<PagedResult<RuleDefinitionSummaryDto>> result = await sut.Handle(
            new ListRuleDefinitionsQuery(
                Page: 2,
                PageSize: 20,
                SearchQuery: "value",
                Language: "vi",
                SortBy: RuleDefinitionSortField.Name,
                SortDirection: CollectionSortDirection.Descending),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(21);
        result.Value.Items.Should().ContainSingle()
            .Which.DefinitionKey.Should().Be(RuleDefinitionKeys.Required);
    }

    [Fact]
    public async Task List_WhenNameSortIsExplicit_SortsLocalizedBuiltInsAndWorkspaceBeforePaging()
    {
        DomainRuleDefinition workspaceDefinition = DomainRuleDefinition.CreateDraft(
            RuleDefinitionHandlerTestContext.WorkspaceId,
            DomainRuleDefinitionKey.Create("middle_workspace").Value,
            "Middle workspace",
            "Workspace definition sorted by its persisted name.",
            Axis.Rules.Domain.RuleSubjectReference.Human(RuleDefinitionHandlerTestContext.UserId),
            ActorSnapshot.User(RuleDefinitionHandlerTestContext.UserId, "Ada Lovelace"),
            DateTime.UtcNow).Value;
        _context.Repository.CountForWorkspaceAsync(
                RuleDefinitionHandlerTestContext.WorkspaceId,
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(1);
        _context.Repository.ListForWorkspaceAsync(
                RuleDefinitionHandlerTestContext.WorkspaceId,
                Arg.Any<int>(),
                Arg.Any<int>(),
                null,
                null,
                RuleDefinitionSortField.Name,
                CollectionSortDirection.Ascending,
                Arg.Any<CancellationToken>())
            .Returns([workspaceDefinition]);
        ListRuleDefinitionsHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Repository,
            _context.CatalogSearch);

        Result<PagedResult<RuleDefinitionSummaryDto>> result = await sut.Handle(
            new ListRuleDefinitionsQuery(
                Page: 1,
                PageSize: 6,
                Language: "vi",
                SortBy: RuleDefinitionSortField.Name,
                SortDirection: CollectionSortDirection.Ascending),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(10);
        result.Value.Items.Select(item => item.DefinitionKey).Should().Equal(
            RuleDefinitionKeys.Required,
            RuleDefinitionKeys.DateRange,
            RuleDefinitionKeys.DateTimeRange,
            RuleDefinitionKeys.NumericRange,
            "middle_workspace",
            RuleDefinitionKeys.TextPattern);
    }

    [Theory]
    [InlineData(RuleDefinitionSortField.Origin, CollectionSortDirection.Ascending, RuleOrigin.BuiltIn)]
    [InlineData(RuleDefinitionSortField.Origin, CollectionSortDirection.Descending, RuleOrigin.Workspace)]
    [InlineData(RuleDefinitionSortField.Status, CollectionSortDirection.Ascending, RuleOrigin.BuiltIn)]
    [InlineData(RuleDefinitionSortField.Status, CollectionSortDirection.Descending, RuleOrigin.Workspace)]
    public async Task List_WhenEnumSortIsExplicit_SortsMergedCatalogBeforePaging(
        RuleDefinitionSortField sortBy,
        CollectionSortDirection sortDirection,
        RuleOrigin expectedOrigin)
    {
        DomainRuleDefinition workspaceDefinition = RuleDefinitionHandlerTestContext.DraftDefinition();
        _context.Repository.CountForWorkspaceAsync(
                RuleDefinitionHandlerTestContext.WorkspaceId,
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(1);
        _context.Repository.ListForWorkspaceAsync(
                RuleDefinitionHandlerTestContext.WorkspaceId,
                0,
                1,
                null,
                null,
                sortBy,
                sortDirection,
                Arg.Any<CancellationToken>())
            .Returns([workspaceDefinition]);
        ListRuleDefinitionsHandler sut = new(
            _context.CurrentUser,
            _context.CurrentSubject,
            _context.Authorization,
            _context.Repository,
            _context.CatalogSearch);

        Result<PagedResult<RuleDefinitionSummaryDto>> result = await sut.Handle(
            new ListRuleDefinitionsQuery(
                Page: 1,
                PageSize: 1,
                Language: "en",
                SortBy: sortBy,
                SortDirection: sortDirection),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(10);
        result.Value.Items.Should().ContainSingle().Which.Origin.Should().Be(expectedOrigin);
    }
}
