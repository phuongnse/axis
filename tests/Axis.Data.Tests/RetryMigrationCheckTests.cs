using Axis.Configuration.Storage;

namespace Axis.Data.Tests;

public sealed class RetryMigrationCheckTests
{
    private static readonly Guid _entityId = Guid.Parse("e8df48a6-6a9e-40a3-b5ae-6254334dbd47");

    private static readonly Guid _otherEntityId = Guid.Parse("0b7a2c1e-3d4f-4a5b-8c6d-7e8f9a0b1c2d");

    private static readonly Guid _migrationId = Guid.Parse("11111111-2222-4333-8444-555555555555");

    private static readonly Guid _heldMigrationId = Guid.Parse("66666666-7777-4888-8999-aaaaaaaaaaaa");

    private static IReadOnlyList<(Guid, IReadOnlyList<ChangedColumn>?)> Applied(Guid migrationId, IReadOnlyList<ChangedColumn>? changed) =>
        [(migrationId, changed)];

    [Fact]
    public void A_missing_pinned_release_blocks_when_an_applied_migration_changed_a_column()
    {
        Assert.True(RetryMigrationCheck.Blocks(Applied(_migrationId, [new ChangedColumn(_entityId, "note")]), pinned: null));
    }

    [Fact]
    public void A_missing_pinned_release_does_not_block_when_no_applied_migration_changed_a_column()
    {
        Assert.False(RetryMigrationCheck.Blocks(Applied(_migrationId, []), pinned: null));
        Assert.False(RetryMigrationCheck.Blocks([], pinned: null));
    }

    [Fact]
    public void A_dropped_field_that_the_pinned_release_has_blocks_ignoring_letter_case()
    {
        var pinned = Release(Entity(_entityId, "title", "Note"));

        Assert.True(RetryMigrationCheck.Blocks(Applied(_migrationId, [new ChangedColumn(_entityId, "note")]), pinned));
    }

    [Fact]
    public void A_dropped_field_that_the_pinned_release_does_not_have_does_not_block()
    {
        var pinned = Release(Entity(_entityId, "title"));

        Assert.False(RetryMigrationCheck.Blocks(Applied(_migrationId, [new ChangedColumn(_entityId, "note")]), pinned));
    }

    [Fact]
    public void A_column_of_an_entity_that_the_pinned_release_does_not_have_does_not_block()
    {
        var pinned = Release(Entity(_otherEntityId, "note"));

        Assert.False(RetryMigrationCheck.Blocks(Applied(_migrationId, [new ChangedColumn(_entityId, "note")]), pinned));
    }

    [Fact]
    public void A_dropped_table_blocks_when_the_pinned_release_has_the_entity()
    {
        Assert.True(RetryMigrationCheck.Blocks(Applied(_migrationId, [new ChangedColumn(_entityId, null)]), Release(Entity(_entityId, "title"))));
        Assert.False(RetryMigrationCheck.Blocks(Applied(_migrationId, [new ChangedColumn(_entityId, null)]), Release(Entity(_otherEntityId, "title"))));
    }

    [Fact]
    public void A_migration_applied_before_the_columns_were_recorded_blocks()
    {
        Assert.True(RetryMigrationCheck.Blocks(Applied(_migrationId, changed: null), Release(Entity(_entityId, "title"))));
    }

    [Fact]
    public void A_migration_that_changed_no_column_does_not_block()
    {
        Assert.False(RetryMigrationCheck.Blocks(Applied(_migrationId, []), Release(Entity(_entityId, "title"))));
    }

    [Fact]
    public void A_migration_whose_file_the_pinned_release_holds_does_not_block_even_when_the_release_has_the_field()
    {
        var pinned = Release(Entity(_entityId, "title", "amount"), Migration(_heldMigrationId));

        Assert.False(RetryMigrationCheck.Blocks(Applied(_heldMigrationId, [new ChangedColumn(_entityId, "amount")]), pinned));
        Assert.False(RetryMigrationCheck.Blocks(Applied(_heldMigrationId, changed: null), pinned));
        Assert.True(RetryMigrationCheck.Blocks(
            [(_heldMigrationId, [new ChangedColumn(_entityId, "amount")]), (_migrationId, [new ChangedColumn(_entityId, "amount")])],
            pinned));
    }

    private static Release Release(params string[] contents)
    {
        var id = Guid.NewGuid();
        return new Release
        {
            Id = id,
            ApplicationId = Guid.NewGuid(),
            ContentHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            Resources = contents
                .Select((content, index) => new ReleaseResource { ReleaseId = id, Path = $"resource-{index}.json", Content = content })
                .ToList(),
        };
    }

    private static string Entity(Guid id, params string[] fields) =>
        $$"""{"id":"{{id}}","kind":"entity","name":"E{{id:N}}","fields":[{{string.Join(",", fields.Select(field => $$"""{"name":"{{field}}","type":"text"}"""))}}]}""";

    private static string Migration(Guid id) =>
        $$"""{"id":"{{id}}","kind":"migration","name":"M{{id:N}}","entity":"Request","changes":[]}""";
}
