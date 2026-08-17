using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Tests.Migrations;

[Collection("IdentityDb")]
public sealed class IdentityInitialMigrationTests(IdentityDatabaseFixture database)
{
    [Fact]
    public async Task FreshDatabase_AppliesOneInitialMigrationWithNoModelDrift()
    {
        await using IdentityDbContext context = database.CreateContext();

        (await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken))
            .Should().ContainSingle(migration => migration.EndsWith("_InitialIdentity", StringComparison.Ordinal));
        context.Database.HasPendingModelChanges().Should().BeFalse();
    }
}
