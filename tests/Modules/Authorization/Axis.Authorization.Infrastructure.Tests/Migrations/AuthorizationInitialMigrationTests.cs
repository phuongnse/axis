using Axis.Authorization.Infrastructure.Persistence;
using Axis.Authorization.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Authorization.Infrastructure.Tests.Migrations;

[Collection("AuthorizationDb")]
public sealed class AuthorizationInitialMigrationTests(AuthorizationDatabaseFixture database)
{
    [Fact]
    public async Task FreshDatabase_WhenMigrated_HasOneInitialMigrationWithoutModelDrift()
    {
        await using AuthorizationDbContext context = database.CreateContext();

        (await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken))
            .Should().ContainSingle(migration => migration.EndsWith("_InitialAuthorization", StringComparison.Ordinal));
        context.Database.HasPendingModelChanges().Should().BeFalse();
    }
}
