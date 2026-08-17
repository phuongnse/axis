using Axis.Rules.Application;
using Axis.Rules.Application.Search;
using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence;
using Axis.Rules.Infrastructure.Search;
using Axis.Rules.Infrastructure.Tests.Fixtures;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Rules.Infrastructure.Tests.Search;

[Collection("RulesDb")]
public sealed class PostgresRuleCatalogSearchProviderTests(RulesDatabaseFixture db)
{
    [Fact]
    public async Task SearchAsync_WhenOriginsAreMixed_RanksAndPagesThemAsOneCatalog()
    {
        Guid workspaceId = Guid.NewGuid();
        await using RulesDbContext context = db.CreateContext();
        RuleDefinition workspaceDefinition = RuleDefinition.CreateDraft(
            workspaceId,
            RuleDefinitionKey.Create($"customer_archive_{Guid.NewGuid():N}"[..26]).Value,
            "Customer archive",
            "Workspace customer retention rule.",
            RuleSubjectReference.Human(Guid.NewGuid()),
            ActorSnapshot.User(Guid.NewGuid(), "Ada Lovelace"),
            DateTime.UtcNow).Value;
        context.RuleDefinitions.Add(workspaceDefinition);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        IRuleCatalogSearchProvider sut = new PostgresRuleCatalogSearchProvider(context);

        RuleCatalogSearchPage result = await sut.SearchAsync(
            workspaceId,
            [new RuleTextSearchDocument("field.customer", "Customer", "Built-in customer rule.")],
            includeWorkspace: true,
            workspaceStatus: null,
            sortBy: null,
            sortDirection: null,
            skip: 0,
            take: 1,
            query: "customer",
            cancellationToken: TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(2);
        result.Items.Should().ContainSingle()
            .Which.Should().Be(new RuleCatalogSearchItem(RuleOrigin.BuiltIn, "field.customer"));
    }

    [Fact]
    public async Task SearchAsync_WhenNameSortIsExplicit_SortsWholeMatchSetBeforePaging()
    {
        Guid workspaceId = Guid.NewGuid();
        await using RulesDbContext context = db.CreateContext();
        RuleDefinition workspaceDefinition = RuleDefinition.CreateDraft(
            workspaceId,
            RuleDefinitionKey.Create($"alpha_customer_{Guid.NewGuid():N}"[..26]).Value,
            "Alpha customer archive",
            "Workspace customer retention rule.",
            RuleSubjectReference.Human(Guid.NewGuid()),
            ActorSnapshot.User(Guid.NewGuid(), "Ada Lovelace"),
            DateTime.UtcNow).Value;
        context.RuleDefinitions.Add(workspaceDefinition);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        IRuleCatalogSearchProvider sut = new PostgresRuleCatalogSearchProvider(context);

        RuleCatalogSearchPage result = await sut.SearchAsync(
            workspaceId,
            [new RuleTextSearchDocument("field.customer", "Customer", "Built-in customer rule.")],
            includeWorkspace: true,
            workspaceStatus: null,
            sortBy: RuleDefinitionSortField.Name,
            sortDirection: CollectionSortDirection.Ascending,
            skip: 0,
            take: 1,
            query: "customer",
            cancellationToken: TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(2);
        result.Items.Should().ContainSingle()
            .Which.Should().Be(new RuleCatalogSearchItem(RuleOrigin.Workspace, workspaceDefinition.Key.Value));
    }

    [Theory]
    [InlineData(RuleDefinitionSortField.Origin, CollectionSortDirection.Ascending, RuleOrigin.BuiltIn)]
    [InlineData(RuleDefinitionSortField.Origin, CollectionSortDirection.Descending, RuleOrigin.Workspace)]
    [InlineData(RuleDefinitionSortField.Status, CollectionSortDirection.Ascending, RuleOrigin.BuiltIn)]
    [InlineData(RuleDefinitionSortField.Status, CollectionSortDirection.Descending, RuleOrigin.Workspace)]
    [InlineData(RuleDefinitionSortField.ActiveVersion, CollectionSortDirection.Ascending, RuleOrigin.BuiltIn)]
    [InlineData(RuleDefinitionSortField.ActiveVersion, CollectionSortDirection.Descending, RuleOrigin.BuiltIn)]
    [InlineData(RuleDefinitionSortField.LatestVersion, CollectionSortDirection.Ascending, RuleOrigin.BuiltIn)]
    [InlineData(RuleDefinitionSortField.LatestVersion, CollectionSortDirection.Descending, RuleOrigin.BuiltIn)]
    [InlineData(RuleDefinitionSortField.Revision, CollectionSortDirection.Ascending, RuleOrigin.Workspace)]
    [InlineData(RuleDefinitionSortField.Revision, CollectionSortDirection.Descending, RuleOrigin.Workspace)]
    [InlineData(RuleDefinitionSortField.CreatedBy, CollectionSortDirection.Ascending, RuleOrigin.Workspace)]
    [InlineData(RuleDefinitionSortField.CreatedBy, CollectionSortDirection.Descending, RuleOrigin.BuiltIn)]
    [InlineData(RuleDefinitionSortField.CreatedAt, CollectionSortDirection.Ascending, RuleOrigin.Workspace)]
    [InlineData(RuleDefinitionSortField.CreatedAt, CollectionSortDirection.Descending, RuleOrigin.Workspace)]
    [InlineData(RuleDefinitionSortField.ModifiedBy, CollectionSortDirection.Ascending, RuleOrigin.Workspace)]
    [InlineData(RuleDefinitionSortField.ModifiedBy, CollectionSortDirection.Descending, RuleOrigin.BuiltIn)]
    [InlineData(RuleDefinitionSortField.ModifiedAt, CollectionSortDirection.Ascending, RuleOrigin.Workspace)]
    [InlineData(RuleDefinitionSortField.ModifiedAt, CollectionSortDirection.Descending, RuleOrigin.Workspace)]
    public async Task SearchAsync_WhenEnumSortIsExplicit_SortsWholeMatchSetBeforePaging(
        RuleDefinitionSortField sortBy,
        CollectionSortDirection sortDirection,
        RuleOrigin expectedOrigin)
    {
        Guid workspaceId = Guid.NewGuid();
        await using RulesDbContext context = db.CreateContext();
        RuleDefinition workspaceDefinition = RuleDefinition.CreateDraft(
            workspaceId,
            RuleDefinitionKey.Create($"customer_draft_{Guid.NewGuid():N}"[..26]).Value,
            "Customer draft",
            "Workspace customer rule.",
            RuleSubjectReference.Human(Guid.NewGuid()),
            ActorSnapshot.User(Guid.NewGuid(), "Ada Lovelace"),
            DateTime.UtcNow).Value;
        context.RuleDefinitions.Add(workspaceDefinition);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        IRuleCatalogSearchProvider sut = new PostgresRuleCatalogSearchProvider(context);

        RuleCatalogSearchPage result = await sut.SearchAsync(
            workspaceId,
            [new RuleTextSearchDocument("field.customer", "Customer", "Built-in customer rule.", "Active")],
            includeWorkspace: true,
            workspaceStatus: null,
            sortBy,
            sortDirection,
            skip: 0,
            take: 1,
            query: "customer",
            cancellationToken: TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(2);
        result.Items.Should().ContainSingle().Which.Origin.Should().Be(expectedOrigin);
    }
}
