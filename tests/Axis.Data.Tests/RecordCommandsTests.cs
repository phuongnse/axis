using Axis.Configuration.Model;
using Axis.Data.Records;
using Npgsql;
using static Axis.Data.Tests.Models;

namespace Axis.Data.Tests;

public sealed class RecordCommandsTests
{
    private static readonly EntityModel _entity = Entity(
        Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1a02"),
        "Order",
        "entities/order.json",
        Field("name", FieldType.Text));

    [Fact]
    public async Task Create_update_and_delete_reject_a_missing_entity_or_values_before_using_the_connection()
    {
        // The connection is never opened; the arguments are checked first.
        await using var connection = new NpgsqlConnection();

        await Assert.ThrowsAsync<ArgumentNullException>(() => RecordCommands.CreateAsync(connection, null!, [], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => RecordCommands.CreateAsync(connection, _entity, null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => RecordCommands.UpdateAsync(connection, null!, Guid.NewGuid(), 1, [], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => RecordCommands.UpdateAsync(connection, _entity, Guid.NewGuid(), 1, null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => RecordCommands.DeleteAsync(connection, null!, Guid.NewGuid(), TestContext.Current.CancellationToken));
    }
}
