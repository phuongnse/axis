using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using static Axis.Integration.Tests.EndpointOutputFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// An inbound endpoint call with an <c>event</c> and no <c>process</c> creates the record, raises the event with
/// no instance and answers <c>201</c> with the record's id alone. EndpointApp's <c>AnnounceNote</c> raises
/// <c>NoteAnnounced</c>, which has one webhook, <c>AnnounceHook</c>, and no trigger.
/// </summary>
public sealed class EndpointAnnounceCallTests(EndpointOutputFixture fixture) : IClassFixture<EndpointOutputFixture>
{
    private const string Endpoint = "/api/apps/EndpointApp/endpoints/AnnounceNote";

    private const string HookPath = "/announce";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_call_answers_201_with_the_record_only_starts_no_instance_and_stores_an_occurrence_without_one()
    {
        const string title = "Announced once";
        var instances = await fixture.CountAsync("SELECT count(*) FROM axis.process_instances");

        using var response = await CallAsync(title);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        using var json = JsonDocument.Parse(body);
        var properties = json.RootElement.EnumerateObject().Select(property => property.Name).ToList();
        Assert.Equal(["recordId"], properties);
        var recordId = json.RootElement.GetProperty("recordId").GetGuid();
        Assert.Equal(1L, await NotesAsync(title));
        Assert.Equal(instances, await fixture.CountAsync("SELECT count(*) FROM axis.process_instances"));
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.events WHERE subject_id = @subject AND event = 'NoteAnnounced' AND process_instance_id IS NULL",
                ("subject", recordId)));
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM axis.events WHERE subject_id = @subject", ("subject", recordId)));
    }

    [Fact]
    public async Task A_repeat_with_the_same_idempotency_key_answers_the_same_record_and_stores_no_second_occurrence()
    {
        const string title = "Announced with a key";

        using var first = await CallAsync(title, "announce-once");
        var firstBody = await first.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(first.StatusCode == HttpStatusCode.Created, firstBody);
        using var repeat = await CallAsync(title, "announce-once");
        var repeatBody = await repeat.Content.ReadAsStringAsync(CancellationToken);

        Assert.True(repeat.StatusCode == HttpStatusCode.Created, repeatBody);
        using var firstJson = JsonDocument.Parse(firstBody);
        using var repeatJson = JsonDocument.Parse(repeatBody);
        Assert.Equal(["recordId"], repeatJson.RootElement.EnumerateObject().Select(property => property.Name));
        var recordId = firstJson.RootElement.GetProperty("recordId").GetGuid();
        Assert.Equal(recordId, repeatJson.RootElement.GetProperty("recordId").GetGuid());
        Assert.Equal(1L, await NotesAsync(title));
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM axis.events WHERE subject_id = @subject", ("subject", recordId)));
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.endpoint_receipts WHERE record_id = @record AND instance_id IS NULL",
                ("record", recordId)));
    }

    [Fact]
    public async Task The_status_location_of_an_endpoint_without_a_process_is_always_a_404()
    {
        using var client = fixture.CreateClient().CallingAs("importer-system");
        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri($"{Endpoint}/instances/{Guid.NewGuid():D}", UriKind.Relative));
        request.Headers.Host = Host;

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_webhook_of_the_event_receives_the_occurrence()
    {
        const string title = "Announced to a webhook";
        await fixture.RunUntilIdleAsync(CancellationToken);

        using var response = await CallAsync(title);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        using var json = JsonDocument.Parse(body);
        var recordId = json.RootElement.GetProperty("recordId").GetGuid();
        var eventId = await fixture.ScalarAsync<Guid>("SELECT id FROM axis.events WHERE subject_id = @subject", ("subject", recordId));

        await fixture.RunUntilIdleAsync(CancellationToken);

        var request = Assert.Single(
            fixture.Receiver.RequestsTo(HookPath),
            candidate => candidate.Headers["webhook-id"].StartsWith($"{eventId:D}_", StringComparison.Ordinal));
        using var sent = JsonDocument.Parse(request.Body);
        Assert.Equal("NoteAnnounced", sent.RootElement.GetProperty("type").GetString());
        Assert.Equal(recordId, sent.RootElement.GetProperty("data").GetProperty("subjectId").GetGuid());
    }

    [Fact]
    public async Task A_delivery_that_runs_out_of_attempts_leaves_a_dead_letter_without_a_process_that_no_process_lists()
    {
        const string title = "Announced to a failing webhook";
        await fixture.RunUntilIdleAsync(CancellationToken);
        fixture.Receiver.Respond(HookPath, 500, 500, 500, 500, 500);

        using var response = await CallAsync(title);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        using var json = JsonDocument.Parse(body);
        var recordId = json.RootElement.GetProperty("recordId").GetGuid();
        var eventId = await fixture.ScalarAsync<Guid>("SELECT id FROM axis.events WHERE subject_id = @subject", ("subject", recordId));

        // Each failed attempt reschedules the item with a backoff, so the test makes it due again.
        for (var round = 0; round < 7 && await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = 'webhook.deliver'") > 0; round++)
        {
            await fixture.MakeWebhookDeliveriesDueAsync();
            await fixture.RunUntilIdleAsync(CancellationToken);
        }

        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = 'webhook.deliver'"));
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.dead_letters WHERE kind = 'webhook.deliver' AND event_id = @event AND process_id IS NULL AND process_instance_id IS NULL AND state = 'open'",
                ("event", eventId)));
        Assert.Equal(
            5,
            fixture.Receiver.RequestsTo(HookPath).Count(request => request.Headers["webhook-id"].StartsWith($"{eventId:D}_", StringComparison.Ordinal)));

        await using var connection = await fixture.OpenConnectionAsync();
        foreach (var process in fixture.Model.Processes)
        {
            var page = await DeadLetters.ListAsync(connection, fixture.Model.Manifest.Id, process.Id, null, 1, 100, CancellationToken);
            Assert.DoesNotContain(page.Items, letter => letter.EventId == eventId);
        }
    }

    [Fact]
    public async Task A_caller_without_the_invoke_grant_gets_403_and_nothing_is_written()
    {
        const string title = "Announced by a reader";
        var notes = await NotesAsync(title);
        var occurrences = await fixture.CountAsync("SELECT count(*) FROM axis.events");
        var receipts = await fixture.CountAsync("SELECT count(*) FROM axis.endpoint_receipts");

        using var response = await CallAsync(title, "reader-key", "reader-system");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(notes, await NotesAsync(title));
        Assert.Equal(occurrences, await fixture.CountAsync("SELECT count(*) FROM axis.events"));
        Assert.Equal(receipts, await fixture.CountAsync("SELECT count(*) FROM axis.endpoint_receipts"));
    }

    private Task<long> NotesAsync(string title)
    {
        Assert.True(fixture.Model.TryGetEntity("Note", out var note));
        return fixture.CountAsync(
            $"SELECT count(*) FROM {EntityNaming.QualifiedTable(EntityNaming.Table(note.Id))} WHERE {EntityNaming.Quote(EntityNaming.Column("title"))} = @title",
            ("title", title));
    }

    private async Task<HttpResponseMessage> CallAsync(string title, string? key = null, string caller = "importer-system")
    {
        using var client = fixture.CreateClient().CallingAs(caller);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint, UriKind.Relative))
        {
            Content = new StringContent($$"""{ "input": { "title": "{{title}}" } }""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Host = Host;
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return await client.SendAsync(request, CancellationToken);
    }
}
