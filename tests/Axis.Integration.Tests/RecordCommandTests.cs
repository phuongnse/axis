using System.Text;
using Axis.Data.Audit;
using Axis.Processes.Records;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>The record command called directly, with no HTTP request or session.</summary>
public sealed class RecordCommandTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string TenantId = RecordApiFixture.TenantA;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static Task<byte[]?> Body(CancellationToken cancellationToken) =>
        Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("""{ "values": { "name": "Desk", "quantity": 3, "active": true } }"""));

    [Fact]
    public async Task Create_without_a_create_grant_is_denied_and_writes_nothing()
    {
        await fixture.ResetAsync();
        Assert.True(fixture.Model.TryGetEntity("Item", out var item));
        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantId));
        await connection.OpenAsync(CancellationToken);
        var auditBefore = await fixture.AuditRecordCountAsync(TenantId);

        var result = await RecordCommand.CreateAsync(
            connection,
            TenantId,
            fixture.Model,
            Guid.NewGuid(),
            item,
            new RecordActor("rita", "Rita Reader", ["reader"]),
            Body,
            null,
            NullLoggerFactory.Instance,
            CancellationToken);

        Assert.Equal(RecordCommandOutcome.Denied, result.Outcome);
        Assert.Null(result.Record);
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantId, "Item")).RowCount);
        Assert.Equal(auditBefore, await fixture.AuditRecordCountAsync(TenantId));
    }

    [Fact]
    public async Task Create_with_a_create_grant_writes_the_record_and_audits_the_actor()
    {
        await fixture.ResetAsync();
        Assert.True(fixture.Model.TryGetEntity("Item", out var item));
        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantId));
        await connection.OpenAsync(CancellationToken);

        var result = await RecordCommand.CreateAsync(
            connection,
            TenantId,
            fixture.Model,
            await ActiveReleaseIdAsync(connection),
            item,
            new RecordActor("cmd-actor", "Command Actor", ["employee"]),
            Body,
            null,
            NullLoggerFactory.Instance,
            CancellationToken);

        Assert.Equal(RecordCommandOutcome.Written, result.Outcome);
        var audit = Assert.Single(await fixture.AuditRecordsAsync(TenantId, result.Record!.Id));
        Assert.Equal("record.created", audit.Action);
        Assert.Equal("cmd-actor", audit.Actor);
        Assert.Equal("Command Actor", audit.ActorName);
    }

    [Fact]
    public async Task Create_given_a_release_that_is_not_the_active_one_writes_nothing_and_reports_the_change()
    {
        await fixture.ResetAsync();
        Assert.True(fixture.Model.TryGetEntity("Item", out var item));
        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantId));
        await connection.OpenAsync(CancellationToken);
        var auditBefore = await fixture.AuditRecordCountAsync(TenantId);

        var result = await RecordCommand.CreateAsync(
            connection,
            TenantId,
            fixture.Model,
            Guid.NewGuid(),
            item,
            new RecordActor("cmd-actor", "Command Actor", ["employee"]),
            Body,
            null,
            NullLoggerFactory.Instance,
            CancellationToken);

        Assert.Equal(RecordCommandOutcome.ReleaseChanged, result.Outcome);
        Assert.Null(result.Record);
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantId, "Item")).RowCount);
        Assert.Equal(auditBefore, await fixture.AuditRecordCountAsync(TenantId));
    }

    [Fact]
    public async Task Create_run_as_system_fills_the_fields_the_body_leaves_out_from_their_defaults()
    {
        await fixture.ResetAsync();
        Assert.True(fixture.Model.TryGetEntity("Reminder", out var reminder));
        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantId));
        await connection.OpenAsync(CancellationToken);

        var result = await RecordCommand.CreateAsync(
            connection,
            TenantId,
            fixture.Model,
            await ActiveReleaseIdAsync(connection),
            reminder,
            new RecordActor(AuditActors.System, "System", ["employee"]),
            _ => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("""{ "values": { "title": "Sys" } }""")),
            null,
            NullLoggerFactory.Instance,
            CancellationToken);

        Assert.Equal(RecordCommandOutcome.Written, result.Outcome);
        Assert.Equal("open", result.Record!.Values["state"]!.GetValue<string>());
        Assert.Equal(3, result.Record.Values["priority"]!.GetValue<long>());
    }

    private async Task<Guid> ActiveReleaseIdAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT release_id FROM axis.active_releases WHERE application_id = @id", connection);
        command.Parameters.AddWithValue("id", fixture.Model.Manifest.Id);
        return Assert.IsType<Guid>(await command.ExecuteScalarAsync(CancellationToken));
    }
}
