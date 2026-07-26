using Axis.Rules.Application.Search;
using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence;
using Axis.Rules.Infrastructure.Search;
using Axis.Rules.Infrastructure.Tests.Fixtures;
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
            RuleScope.Field,
            RuleContextKey.Create("business_objects.field.text").Value,
            1,
            RuleOutcomeKind.Validation,
            Guid.NewGuid(),
            DateTime.UtcNow).Value;
        context.RuleDefinitions.Add(workspaceDefinition);
        await context.SaveChangesAsync();
        IRuleCatalogSearchProvider sut = new PostgresRuleCatalogSearchProvider(context);

        RuleCatalogSearchPage result = await sut.SearchAsync(
            workspaceId,
            [new RuleTextSearchDocument("field.customer", "Customer", "System customer rule.")],
            includeWorkspace: true,
            workspaceScope: null,
            workspaceStatus: null,
            skip: 0,
            take: 1,
            query: "customer");

        result.TotalCount.Should().Be(2);
        result.Items.Should().ContainSingle()
            .Which.Should().Be(new RuleCatalogSearchItem(RuleOrigin.System, "field.customer"));
    }
}
