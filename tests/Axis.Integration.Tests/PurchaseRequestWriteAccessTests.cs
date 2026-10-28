using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The sample's employee policy: the requester is the signed-in user, and an employee updates their
/// request only while it is draft or returned.
/// </summary>
public sealed class PurchaseRequestWriteAccessTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string App = "/api/apps/PurchaseRequests";

    private const string Records = $"{App}/entities/PurchaseRequest/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creating_a_request_sets_the_requester_to_the_signed_in_user_and_a_client_value_is_400()
    {
        using var requesterClient = fixture.CreateClient().SignedInAs("requester");

        var subject = await CreateRequestAsync(requesterClient, 500);

        var (requester, _) = await ReadAsync(requesterClient, subject);
        Assert.Equal("requester", requester);
        using var create = await SendAsync(
            requesterClient,
            HttpMethod.Post,
            Records,
            $$"""{ "values": { "title": "Set", "status": "draft", "department": "{{Operations:D}}", "requester": "x" } }""");
        using var problem = await RecordApiFixture.ReadProblemAsync(create, HttpStatusCode.BadRequest);
        Assert.Equal([("/values/requester", "Cannot be set.")], Errors(problem));
    }

    [Fact]
    public async Task An_employee_cannot_update_their_submitted_request()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var subject = await CreateRequestAsync(requester, 500);
        await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, await EditTitleAsync(requester, subject));
    }

    [Fact]
    public async Task An_employee_may_update_their_returned_request()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var chau = fixture.CreateClient().SignedInAs("chau");
        var subject = await CreateRequestAsync(requester, 500);
        await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        await ReturnAsync(chau, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, await EditTitleAsync(requester, subject));
    }

    /// <summary>Reads the request and changes its title at the version it read, and returns the status code.</summary>
    private static async Task<HttpStatusCode> EditTitleAsync(HttpClient client, Guid subject)
    {
        var (_, version) = await ReadAsync(client, subject);
        using var edited = await SendAsync(
            client,
            HttpMethod.Patch,
            $"{Records}/{subject:D}",
            $$"""{ "version": {{version}}, "values": { "title": "Edited" } }""");
        return edited.StatusCode;
    }

    /// <summary>Completes the open task of <paramref name="subject"/> with the return outcome.</summary>
    private static async Task ReturnAsync(HttpClient client, Guid subject)
    {
        Guid task;
        using (var list = await SendAsync(client, HttpMethod.Get, $"{App}/tasks?pageSize=100"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var body = await ReadJsonAsync(list);
            task = Assert.Single(
                body.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("subject").GetProperty("id").GetGuid() == subject).GetProperty("id").GetGuid();
        }

        using var completed = await CompleteAsync(client, task, """{ "outcome": "return" }""");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }

    /// <summary>The request's requester and version.</summary>
    private static async Task<(string? Requester, long Version)> ReadAsync(HttpClient client, Guid subject)
    {
        using var response = await SendAsync(client, HttpMethod.Get, $"{Records}/{subject:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        return (
            body.RootElement.GetProperty("values").GetProperty("requester").GetString(),
            body.RootElement.GetProperty("version").GetInt64());
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body = null)
    {
        using var request = RecordApiFixture.Request(method, path, Host, body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

    /// <summary>Each error key with its single message, in the problem's order.</summary>
    private static List<(string Key, string Message)> Errors(JsonDocument problem) =>
        problem.RootElement.GetProperty("errors").EnumerateObject()
            .Select(property => (property.Name, Assert.Single(property.Value.EnumerateArray()).GetString()!))
            .ToList();
}
