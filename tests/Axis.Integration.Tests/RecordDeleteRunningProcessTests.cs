using System.Net;
using System.Text.Json;
using Axis.Configuration.Releases;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Server.Tests;
using static Axis.Integration.Tests.Polling;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A delete of a record that a running or waiting process instance works on is a 409 that names no
/// process, and it is ordered with a start of the same record by the start lock and the record's row lock.
/// </summary>
public sealed class RecordDeleteRunningProcessTests(ProcessStartFixture fixture) : IClassFixture<ProcessStartFixture>
{
    private const string Title = "A running process works on this record.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("running")]
    [InlineData("waiting")]
    public async Task A_record_with_an_open_instance_is_kept_with_a_409_that_names_no_process(string state)
    {
        var subject = await fixture.InsertRequestAsync(status: null);
        var instance = await fixture.InsertInstanceAsync(subject);
        await fixture.SetInstanceStateAsync(instance, state);
        using var client = fixture.CreateClient().SignedInAs("olga");

        using var response = await DeleteAsync(client, subject);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(CancellationToken);
        using var body = JsonDocument.Parse(raw);
        Assert.Equal(Title, body.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("Submit", raw, StringComparison.Ordinal);
        Assert.Equal(1L, await CountRecordAsync(subject));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("cancelled")]
    public async Task A_record_is_deleted_once_its_instance_has_ended(string state)
    {
        var subject = await fixture.InsertRequestAsync(status: null);
        var instance = await fixture.InsertInstanceAsync(subject);
        await fixture.SetInstanceStateAsync(instance, state);
        using var client = fixture.CreateClient().SignedInAs("olga");

        using var response = await DeleteAsync(client, subject);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0L, await CountRecordAsync(subject));
    }

    [Fact]
    public async Task A_user_without_a_delete_grant_gets_403_not_409()
    {
        var subject = await fixture.InsertRequestAsync(status: null);
        await fixture.InsertInstanceAsync(subject);
        using var client = fixture.CreateClient().SignedInAs("anna");

        using var response = await DeleteAsync(client, subject);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1L, await CountRecordAsync(subject));
    }

    [Fact]
    public async Task A_delete_that_waits_for_an_open_start_gets_409_once_the_start_commits()
    {
        var subject = await fixture.InsertRequestAsync(status: null);
        Assert.True(fixture.Model.TryGetProcess("Submit", out var process));
        Assert.True(fixture.Model.TryGetEntity(process.Entity!.Name, out var entity));
        using var client = fixture.CreateClient().SignedInAs("olga");

        // A start in its uncommitted transaction: the start lock, the subject's row lock and the instance.
        await using var holder = await fixture.OpenConnectionAsync();
        await using var transaction = await holder.BeginTransactionAsync(CancellationToken);
        await StartLock.TakeSharedAsync(transaction, fixture.Model.Manifest.Id, CancellationToken);
        Assert.True(await RecordCommands.LockAsync(holder, entity, subject, CancellationToken));
        Assert.True(await ProcessStarts.TryInsertInstanceAsync(
            transaction,
            new ProcessInstanceRow
            {
                Id = Guid.CreateVersion7(),
                ApplicationId = fixture.Model.Manifest.Id,
                ProcessId = process.Id,
                SubjectEntityId = entity.Id,
                SubjectId = subject,
                ReleaseId = fixture.FirstReleaseId,
                State = ProcessStarts.Running,
                Revision = 1,
                Step = process.Start,
            },
            CancellationToken));

        var deleting = DeleteAsync(client, subject);
        await WaitUntilAsync(
            async () => deleting.IsCompleted || await fixture.CountAsync("SELECT count(*) FROM pg_locks WHERE NOT granted") > 0,
            "The delete did not finish or wait on a lock.");
        Assert.False(deleting.IsCompleted);

        await transaction.CommitAsync(CancellationToken);
        using var response = await deleting.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1L, await CountRecordAsync(subject));
    }

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Guid id) =>
        client.SendAsync(
            Request(HttpMethod.Delete, $"/api/apps/ProcessApp/entities/Request/records/{id:D}", ProcessStartFixture.Host),
            CancellationToken);

    private Task<long> CountRecordAsync(Guid id)
    {
        Assert.True(fixture.Model.TryGetEntity("Request", out var request));
        return fixture.CountAsync(
            $"SELECT count(*) FROM {EntityNaming.QualifiedTable(EntityNaming.Table(request.Id))} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id",
            ("id", id));
    }
}
