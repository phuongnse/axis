using Axis.BusinessObjects.Infrastructure.Persistence;
using Axis.BusinessObjects.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.BusinessObjects.Infrastructure.Tests.Migrations;

[Collection("BusinessObjectsDb")]
public sealed class BusinessObjectsInitialMigrationTests(BusinessObjectsDatabaseFixture database)
{
    [Fact]
    public async Task FreshDatabase_WhenMigrated_HasOneInitialMigrationWithoutModelDrift()
    {
        await using BusinessObjectsDbContext context = database.CreateContext();

        (await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken))
            .Should().ContainSingle(migration => migration.EndsWith("_InitialBusinessObjects", StringComparison.Ordinal));
        context.Database.HasPendingModelChanges().Should().BeFalse();
    }
}
