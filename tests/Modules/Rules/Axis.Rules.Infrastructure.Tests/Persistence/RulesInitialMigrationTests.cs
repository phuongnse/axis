using Axis.Rules.Infrastructure.Persistence;
using Axis.Rules.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Rules.Infrastructure.Tests.Persistence;

[Collection("RulesDb")]
public sealed class RulesInitialMigrationTests(RulesDatabaseFixture database)
{
    [Fact]
    public async Task FreshDatabase_WhenMigrated_HasOneInitialMigrationWithoutModelDrift()
    {
        await using RulesDbContext context = database.CreateContext();

        (await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken))
            .Should().ContainSingle(migration => migration.EndsWith("_InitialRules", StringComparison.Ordinal));
        context.Database.HasPendingModelChanges().Should().BeFalse();
    }
}
