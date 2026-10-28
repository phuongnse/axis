using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The sample's policies decide which purchase requests each role reaches. Employees reach their own
/// requests, department managers those of the departments they manage, and finance reviewers the
/// submitted requests and their outcomes. The test has the fixture's database to itself.
/// </summary>
public sealed class PurchaseRequestReachTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string App = "/api/apps/PurchaseRequests";

    private const string Records = $"{App}/entities/PurchaseRequest/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_role_reaches_exactly_the_requests_its_policy_allows()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var employee = fixture.CreateClient().SignedInAs("employee");
        using var manager2 = fixture.CreateClient().SignedInAs("manager-2");
        using var manager = fixture.CreateClient().SignedInAs("manager");
        using var financeReviewer = fixture.CreateClient().SignedInAs("finance-reviewer");

        // The requester: an Operations draft and a submitted Engineering request.
        var annaDraft = await CreateRequestAsync(requester, 500, Operations);
        var annaSubmitted = await CreateRequestAsync(requester, 500, Engineering);
        await StartAsync(requester, annaSubmitted);
        await fixture.RunUntilIdleAsync(CancellationToken);

        // Employee: an approved Operations request and an Engineering draft.
        var employeeApproved = await CreateRequestAsync(employee, 500, Operations);
        await StartAsync(employee, employeeApproved);
        await fixture.RunUntilIdleAsync(CancellationToken);
        await ApproveAsync(manager2, employeeApproved);
        await fixture.RunUntilIdleAsync(CancellationToken);
        var employeeDraft = await CreateRequestAsync(employee, 500, Engineering);

        Assert.Equal(new HashSet<Guid> { annaDraft, annaSubmitted }, await ListAsync(requester));
        Assert.Equal(new HashSet<Guid> { employeeApproved, employeeDraft }, await ListAsync(employee));
        Assert.Equal(new HashSet<Guid> { annaDraft, employeeApproved }, await ListAsync(manager2));
        Assert.Equal(new HashSet<Guid> { annaSubmitted, employeeDraft }, await ListAsync(manager));
        Assert.Equal(new HashSet<Guid> { annaSubmitted, employeeApproved }, await ListAsync(financeReviewer));

        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(manager2, $"{Records}/{annaSubmitted:D}"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(financeReviewer, $"{Records}/{annaDraft:D}"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(requester, $"{Records}/{employeeDraft:D}"));

        foreach (var client in new[] { requester, employee, manager2, manager, financeReviewer })
        {
            Assert.Equal(HttpStatusCode.OK, await StatusOfAsync(client, $"{App}/entities/Department/records"));
            Assert.Equal(HttpStatusCode.OK, await StatusOfAsync(client, $"{App}/entities/Supplier/records"));
        }

        Assert.Equal(HttpStatusCode.Forbidden, await StatusOfAsync(requester, $"{App}/data-sources/RequestsByDepartment/rows"));
        Assert.Equal(HttpStatusCode.OK, await StatusOfAsync(manager2, $"{App}/data-sources/RequestsByDepartment/rows"));
    }

    /// <summary>The ids of the purchase requests that <paramref name="client"/> lists.</summary>
    private static async Task<HashSet<Guid>> ListAsync(HttpClient client)
    {
        using var response = await SendAsync(client, HttpMethod.Get, $"{Records}?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        return [.. body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
    }

    /// <summary>Completes the open task of <paramref name="subject"/> with the approve outcome.</summary>
    private static async Task ApproveAsync(HttpClient client, Guid subject)
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

        using var completed = await CompleteAsync(client, task, """{ "outcome": "approve" }""");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }

    private static async Task<HttpStatusCode> StatusOfAsync(HttpClient client, string path)
    {
        using var response = await SendAsync(client, HttpMethod.Get, path);
        return response.StatusCode;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body = null)
    {
        using var request = RecordApiFixture.Request(method, path, Host, body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
}
