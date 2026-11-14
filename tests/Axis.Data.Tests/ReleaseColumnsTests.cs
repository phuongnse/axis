using Axis.Configuration.Storage;

namespace Axis.Data.Tests;

public sealed class ReleaseColumnsTests
{
    private static readonly Guid _entityId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid _fixtureEntityId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid _fixtureMigrationId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public void The_files_of_an_application_package_are_not_read_as_resources()
    {
        var release = new Release
        {
            Id = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            ContentHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            Resources =
            [
                Resource("entities/order.json", $$"""{ "id": "{{_entityId}}", "kind": "entity", "name": "Order", "fields": [ { "name": "amount" } ] }"""),
                Resource("packages/tools/adapter.py", "import sys\n"),
                Resource("packages/tools/requirements.txt", "requests==2.32.0\n"),
                Resource("packages/tools/tests/fixture.json", $$"""{ "id": "{{_fixtureEntityId}}", "kind": "entity", "name": "Fixture", "fields": [ { "name": "x" } ] }"""),
                Resource("packages/tools/tests/migration.json", $$"""{ "id": "{{_fixtureMigrationId}}", "kind": "migration" }"""),
            ],
        };

        var fields = ReleaseColumns.FieldsByEntityId(release);
        var migrations = ReleaseColumns.MigrationIds(release);

        Assert.Equal([_entityId], fields.Keys);
        Assert.Equal(["amount"], fields[_entityId]);
        Assert.Empty(migrations);
    }

    private static ReleaseResource Resource(string path, string content) => new() { ReleaseId = Guid.Empty, Path = path, Content = content };
}
