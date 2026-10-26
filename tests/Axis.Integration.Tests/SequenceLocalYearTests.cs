using Axis.Configuration.Model;
using Axis.Data.Records;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A sequence's <c>{yyyy}</c> and its yearly restart follow the year in the application's time zone,
/// not the UTC year. Each test gives the Ticket number field a sequence of its own, so the counter
/// row is not shared with the other tests.
/// </summary>
public sealed class SequenceLocalYearTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_counter_restarts_with_the_year_in_the_application_time_zone()
    {
        var model = ModelWithOwnSequence("Asia/Ho_Chi_Minh", out var ticket);
        await fixture.ResetAsync();
        await using var connection = await OpenAsync();

        await SetTimeAsync(connection, "2026-12-31T10:00:00Z");
        var first = await CreateAsync(connection, model, ticket);
        var second = await CreateAsync(connection, model, ticket);
        await SetTimeAsync(connection, "2026-12-31T18:00:00Z");
        var local = await CreateAsync(connection, model, ticket);
        var next = await CreateAsync(connection, model, ticket);

        // 18:00 UTC is already 1 January in Asia/Ho_Chi_Minh, so the counter starts again.
        Assert.Equal(["T-2026-00001", "T-2026-00002", "T-2027-00001", "T-2027-00002"], [first, second, local, next]);
    }

    [Fact]
    public async Task Without_a_time_zone_the_year_is_the_utc_year()
    {
        var model = ModelWithOwnSequence("UTC", out var ticket);
        await fixture.ResetAsync();
        await using var connection = await OpenAsync();

        await SetTimeAsync(connection, "2026-12-31T18:00:00Z");
        var december = await CreateAsync(connection, model, ticket);
        await SetTimeAsync(connection, "2027-01-01T00:00:00Z");
        var january = await CreateAsync(connection, model, ticket);

        Assert.Equal(["T-2026-00001", "T-2027-00001"], [december, january]);
    }

    /// <summary>The fixture application in <paramref name="zoneId"/>, whose Ticket number field has a new sequence <c>T-{yyyy}-{n:5}</c>.</summary>
    private ApplicationModel ModelWithOwnSequence(string zoneId, out EntityModel ticket)
    {
        Assert.True(fixture.Model.TryGetEntity("Ticket", out var original));
        var sequence = new SequenceModel(Guid.NewGuid(), "Local", "T-{yyyy}-{n:5}");
        ticket = original with
        {
            Fields = [.. original.Fields.Select(field => field.Sequence is null ? field : field with { Sequence = sequence })],
        };
        return fixture.Model with { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(zoneId) };
    }

    /// <summary>A connection that is not pooled, because the test sets a session setting on it.</summary>
    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString(TenantA)) { Pooling = false }.ConnectionString;
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        return connection;
    }

    private static async Task SetTimeAsync(NpgsqlConnection connection, string instant)
    {
        await using var command = new NpgsqlCommand("SELECT set_config('axis.now', @instant, false)", connection);
        command.Parameters.AddWithValue("instant", instant);
        await command.ExecuteScalarAsync(CancellationToken);
    }

    private static async Task<string> CreateAsync(NpgsqlConnection connection, ApplicationModel model, EntityModel ticket)
    {
        Assert.True(ticket.TryGetField("title", out var title));
        var result = await RecordCommands.CreateAsync(
            connection, model, ticket, Guid.NewGuid(), [new RecordValue(title, "Ticket")], [], cancellationToken: CancellationToken);
        Assert.Equal(RecordWriteOutcome.Written, result.Outcome);
        return result.Record!.Values["number"]!.GetValue<string>();
    }
}
