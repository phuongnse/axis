using Axis.Solutions.Infrastructure.Persistence;
using Axis.Solutions.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Solutions.Infrastructure.Tests.Migrations;

[Collection("SolutionsDb")]
public sealed class SolutionsInitialMigrationTests(SolutionsDatabaseFixture database)
{
    [Fact]
    public async Task FreshDatabase_AppliesOneInitialMigrationWithNoModelDrift()
    {
        await using SolutionsDbContext context = database.CreateContext();

        (await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken))
            .Should().ContainSingle(migration => migration.EndsWith("_InitialSolutions", StringComparison.Ordinal));
        context.Database.HasPendingModelChanges().Should().BeFalse();
    }
}
