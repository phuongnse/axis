using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The commit of an import fires the data change triggers of every record it creates, as a record API create
/// does. <c>ReviewNewTickets</c> starts <c>Review</c> on every create of a <c>Ticket</c>, so a commit of 200 rows
/// starts 200 instances. A data change trigger starts its process in the write's transaction, so no event is
/// delivered and none can fail.
/// </summary>
public sealed class RecordImportTriggerTests(TriggerApiFixture fixture) : IClassFixture<TriggerApiFixture>
{
    private const string Tickets = "/api/apps/TriggerApp/entities/Ticket";
    private const int Rows = 200;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Committing_200_rows_starts_a_review_for_each_ticket_and_delivers_no_event()
    {
        var csv = new StringBuilder("title\n");
        for (var row = 1; row <= Rows; row++)
        {
            csv.Append(CultureInfo.InvariantCulture, $"Ticket {row}\n");
        }

        using var cara = fixture.CreateClient().SignedInAs("cara");
        using var upload = await SendAsync(cara, HttpMethod.Post, $"{Tickets}/imports", csv.ToString(), "text/csv");
        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        var id = JsonDocument.Parse(await upload.Content.ReadAsStringAsync(CancellationToken)).RootElement.GetProperty("id").GetGuid();
        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("checked", await StateAsync(cara, id));

        using var commit = await SendAsync(cara, HttpMethod.Post, $"{Tickets}/imports/{id:D}/commit", null);
        Assert.Equal(HttpStatusCode.Accepted, commit.StatusCode);
        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("committed", await StateAsync(cara, id));
        Assert.Equal(Rows, await fixture.CountAsync($"SELECT count(*) FROM {fixture.TicketTable}"));
        Assert.Equal(
            Rows,
            await fixture.CountAsync("SELECT count(*) FROM axis.process_instances WHERE process_id = @process", ("process", fixture.ProcessId("Review"))));
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM axis.delivery_failures"));
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM axis.dead_letters"));
    }

    private static async Task<string> StateAsync(HttpClient client, Guid id)
    {
        using var response = await SendAsync(client, HttpMethod.Get, $"{Tickets}/imports/{id:D}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)).RootElement.GetProperty("state").GetString()!;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body, string contentType = "application/json") =>
        client.SendAsync(Request(method, path, TriggerApiFixture.Host, body, contentType), CancellationToken);
}
