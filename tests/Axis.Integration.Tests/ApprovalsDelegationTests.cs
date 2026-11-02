using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The built-in <c>approvals</c> process <c>RequestApproval</c> while the approver is away, in ApprovalsApp. The
/// approval task goes to the approver's delegate, and the <c>Decision</c> that the delegate's approval writes holds
/// the delegate in <c>decidedBy</c> and the approver in <c>onBehalfOf</c>. The test sets the absence dates around the
/// real date, so it does not depend on the application's time zone.
/// </summary>
public sealed class ApprovalsDelegationTests(ApprovalsAppFixture fixture) : IClassFixture<ApprovalsAppFixture>
{
    private const string App = "/api/apps/ApprovalsApp";
    private const string Employees = $"{App}/entities/organization/Employee/records";
    private const string RequestTypes = $"{App}/entities/approvals/RequestType/records";
    private const string Requests = $"{App}/entities/approvals/Request/records";
    private const string Decisions = $"{App}/entities/approvals/Decision/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_delegate_decides_while_the_approver_is_away_and_the_decision_records_whom_it_was_made_for()
    {
        using var admin = fixture.CreateClient().SignedInAs("appr-admin");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var carl = await CreateAsync(admin, Employees, Employee("appr-carl", "Carl"));
        var bob = await CreateAsync(
            admin,
            Employees,
            Employee("appr-bob", "Bob", $$""", "delegate": "{{carl}}", "delegateFrom": "{{today.AddDays(-1):yyyy-MM-dd}}", "delegateUntil": "{{today.AddDays(1):yyyy-MM-dd}}" """));
        await CreateAsync(admin, Employees, Employee("appr-ann", "Ann", $$""", "manager": "{{bob}}" """));
        var type = await CreateAsync(
            admin,
            RequestTypes,
            $$"""{ "name": "Type", "code": "DELEGATED", "steps": [{ "sequence": 1, "approver": "lineManager" }] }""");
        using var ann = fixture.CreateClient().SignedInAs("appr-ann");
        var request = await CreateAsync(ann, Requests, $$"""{ "type": "{{type}}", "title": "Desk" }""");

        var instance = await fixture.Steps.StartOnSubjectAsync("approvals/RequestApproval", request, fixture.Steps.FirstReleaseId, "appr-ann");
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        var task = Assert.Single(await fixture.Steps.TasksAsync(instance));
        Assert.Equal(("user", "appr-carl"), (task.AssigneeKind, task.Assignee));
        await fixture.Steps.CompleteTaskAsync(instance, "approve", "recordApproval", "appr-carl", CancellationToken);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("completed", (await fixture.Steps.InstanceAsync(instance)).State);
        using var response = await admin.SendAsync(Request(HttpMethod.Get, Decisions, ApprovalsAppFixture.Host), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var decision = Assert.Single(
            body.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("values").GetProperty("request").GetGuid() == request)
            .GetProperty("values");
        Assert.Equal("appr-carl", decision.GetProperty("decidedBy").GetString());
        Assert.Equal("appr-bob", decision.GetProperty("onBehalfOf").GetString());
        Assert.Equal("approved", decision.GetProperty("outcome").GetString());
    }

    private static string Employee(string user, string name, string extra = "") =>
        $$"""{ "fullName": "{{name}}", "workEmail": "{{user}}@example.com", "user": "{{user}}"{{extra}} }""";

    private static async Task<Guid> CreateAsync(HttpClient client, string records, string values)
    {
        using var response = await client.SendAsync(
            Request(HttpMethod.Post, records, ApprovalsAppFixture.Host, $$"""{ "values": {{values}} }"""),
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }
}
