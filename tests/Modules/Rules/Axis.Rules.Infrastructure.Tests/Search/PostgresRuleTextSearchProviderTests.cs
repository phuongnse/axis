using Axis.Rules.Application.Search;
using Axis.Rules.Infrastructure.Persistence;
using Axis.Rules.Infrastructure.Search;
using Axis.Rules.Infrastructure.Tests.Fixtures;
using FluentAssertions;

namespace Axis.Rules.Infrastructure.Tests.Search;

[Collection("RulesDb")]
public sealed class PostgresRuleTextSearchProviderTests(RulesDatabaseFixture db)
{
    [Fact]
    public async Task SearchAsync_WhenDocumentsVary_RanksAndMatchesSmartQueries()
    {
        await using RulesDbContext context = db.CreateContext();
        IRuleTextSearchProvider sut = new PostgresRuleTextSearchProvider(context);
        RuleTextSearchDocument[] documents =
        [
            new("exact", "Customer", "Exact title"),
            new("prefix", "Customer archive", "Prefix title"),
            new("accent", "Kiểm tra ưu tiên", "Tài liệu tiếng Việt"),
            new("typo", "Invoice", "Billing document"),
        ];

        IReadOnlyList<RuleTextSearchMatch> ranked =
            await sut.SearchAsync(
                documents,
                "customer",
                TestContext.Current.CancellationToken);
        IReadOnlyList<RuleTextSearchMatch> accentAndOrder =
            await sut.SearchAsync(
                documents,
                "uu tien kiem tra",
                TestContext.Current.CancellationToken);
        IReadOnlyList<RuleTextSearchMatch> typo =
            await sut.SearchAsync(
                documents,
                "inovice",
                TestContext.Current.CancellationToken);

        ranked.Select(match => match.Key).Take(2).Should().Equal("exact", "prefix");
        accentAndOrder.Should().ContainSingle(match => match.Key == "accent");
        typo.Should().ContainSingle(match => match.Key == "typo");
    }
}
