using System.Net;
using System.Text.Json;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

public sealed class PurchaseRequestResubmitTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string App = "/api/apps/PurchaseRequests";

    private const string Records = $"{App}/entities/PurchaseRequest/records";

    private const string Instances = $"{App}/processes/PurchaseRequestApproval/instances";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_returned_request_keeps_its_ended_instance_and_a_resubmission_starts_a_new_one_on_the_active_release()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        using var chau = fixture.CreateClient().SignedInAs("chau");
        var subject = await CreateRequestAsync(anna, 500);
        using (var started = await StartAsync(anna, subject))
        {
            Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        }

        await fixture.RunUntilIdleAsync(CancellationToken);
        await DecideAsync(chau, subject, "return");

        Assert.Equal("returned", await StatusAsync(anna, subject));
        var first = Assert.Single(await fixture.InstancesAsync(subject));
        Assert.Equal(ProcessStarts.Completed, first.State);

        using (var read = await SendAsync(anna, HttpMethod.Get, $"{Records}/{subject:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var version = (await ReadJsonAsync(read)).RootElement.GetProperty("version").GetInt64();
            using var edited = await SendAsync(
                anna,
                HttpMethod.Patch,
                $"{Records}/{subject:D}",
                $$"""{ "version": {{version}}, "values": { "lineItems": [{ "description": "Desk", "quantity": 2, "unitPrice": 500 }] } }""");
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        }

        using (var resubmitted = await StartAsync(anna, subject))
        {
            Assert.Equal(HttpStatusCode.Created, resubmitted.StatusCode);
        }

        var instances = await fixture.InstancesAsync(subject);
        Assert.Equal(2, instances.Count);
        Assert.Equal(first, instances[0]);
        var second = instances[1];
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(ProcessStarts.Running, second.State);
        Assert.Equal(await fixture.ActiveReleaseIdAsync(CancellationToken), second.ReleaseId);

        using var history = await SendAsync(anna, HttpMethod.Get, $"{Records}/{subject:D}/history?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var items = (await ReadJsonAsync(history)).RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(
            [first.Id, second.Id],
            InstancesOf(items, "process.started").Order());
        Assert.Equal([first.Id], InstancesOf(items, "process.completed"));
    }

    [Fact]
    public async Task A_rejected_request_fails_the_start_condition_and_no_instance_is_created()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        using var chau = fixture.CreateClient().SignedInAs("chau");
        var subject = await CreateRequestAsync(anna, 500);
        using (var started = await StartAsync(anna, subject))
        {
            Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        }

        await fixture.RunUntilIdleAsync(CancellationToken);
        await DecideAsync(chau, subject, "reject");
        Assert.Equal("rejected", await StatusAsync(anna, subject));

        using var again = await StartAsync(anna, subject);

        using var problem = await RecordApiFixture.ReadProblemAsync(again, HttpStatusCode.BadRequest);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["/subjectId"], errors.EnumerateObject().Select(error => error.Name));
        Assert.Equal(["purchaseRequest.cannotSubmit"], errors.GetProperty("/subjectId").EnumerateArray().Select(error => error.GetString()));
        var instance = Assert.Single(await fixture.InstancesAsync(subject));
        Assert.Equal(ProcessStarts.Completed, instance.State);
    }

    /// <summary>Completes the one open task of <paramref name="subject"/> with <paramref name="outcome"/>, then runs the worker.</summary>
    private async Task DecideAsync(HttpClient client, Guid subject, string outcome)
    {
        Guid task;
        using (var list = await SendAsync(client, HttpMethod.Get, $"{App}/tasks?pageSize=100"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            task = Assert.Single(
                (await ReadJsonAsync(list)).RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("subject").GetProperty("id").GetGuid() == subject).GetProperty("id").GetGuid();
        }

        using var completed = await SendAsync(client, HttpMethod.Post, $"{App}/tasks/{task:D}/complete", $$"""{ "outcome": "{{outcome}}" }""");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        await fixture.RunUntilIdleAsync(CancellationToken);
    }

    private static async Task<string?> StatusAsync(HttpClient client, Guid subject)
    {
        using var response = await SendAsync(client, HttpMethod.Get, $"{Records}/{subject:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).RootElement.GetProperty("values").GetProperty("status").GetString();
    }

    private static List<Guid> InstancesOf(IEnumerable<JsonElement> items, string action) =>
        items
            .Where(item => item.GetProperty("action").GetString() == action)
            .Select(item => item.GetProperty("processInstanceId").GetGuid())
            .ToList();

    private static Task<HttpResponseMessage> StartAsync(HttpClient client, Guid subject) =>
        SendAsync(client, HttpMethod.Post, Instances, $$"""{ "subjectId": "{{subject:D}}" }""");

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body = null)
    {
        using var request = RecordApiFixture.Request(method, path, Host, body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
}
