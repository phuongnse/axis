using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The built-in <c>approvals</c> module on the record API of ApprovalsApp. A request gets its number from the
/// sequence <c>RequestNumber</c>, and <c>requesterEmployee</c> is the employee whose <c>user</c> is the requester, or
/// empty when no employee matches. A request type keeps its approval steps as child rows in the order written, and a
/// member cannot create a request type.
/// </summary>
public sealed partial class ApprovalsModuleTests(ApprovalsAppFixture fixture) : IClassFixture<ApprovalsAppFixture>
{
    private const string Admin = "appr-admin";
    private const string Employees = "/api/apps/ApprovalsApp/entities/organization/Employee/records";
    private const string RequestTypes = "/api/apps/ApprovalsApp/entities/approvals/RequestType/records";
    private const string Requests = "/api/apps/ApprovalsApp/entities/approvals/Request/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"^RQ-\d{4}-\d{5}$")]
    private static partial Regex RequestNumber();

    [Fact]
    public async Task A_request_gets_its_number_and_the_employee_whose_user_is_the_requester()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var employee = await CreateAsync(admin, Employees, Employee("appr-ann"));
        var type = await CreateAsync(admin, RequestTypes, RequestType());
        using var member = fixture.CreateClient().SignedInAs("appr-ann");

        using var response = await SendAsync(member, HttpMethod.Post, Requests, $$"""{ "values": { "type": "{{type}}", "title": "Laptop" } }""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var values = body.RootElement.GetProperty("values");
        Assert.Matches(RequestNumber(), values.GetProperty("number").GetString());
        Assert.Equal("appr-ann", values.GetProperty("requester").GetString());
        Assert.Equal(employee, values.GetProperty("requesterEmployee").GetGuid());
        Assert.Equal("draft", values.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_request_of_a_user_that_no_employee_matches_has_an_empty_requester_employee()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var type = await CreateAsync(admin, RequestTypes, RequestType());
        using var member = fixture.CreateClient().SignedInAs("appr-bob");

        using var response = await SendAsync(member, HttpMethod.Post, Requests, $$"""{ "values": { "type": "{{type}}", "title": "Chair" } }""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var values = body.RootElement.GetProperty("values");
        Assert.Matches(RequestNumber(), values.GetProperty("number").GetString());
        Assert.Equal(JsonValueKind.Null, values.GetProperty("requesterEmployee").ValueKind);
    }

    [Fact]
    public async Task An_admin_creates_a_request_type_with_three_steps_that_are_read_back_in_order_and_a_member_cannot()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var employee = await CreateAsync(admin, Employees, Employee());
        var code = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

        var id = await CreateAsync(
            admin,
            RequestTypes,
            $$"""
            { "name": "Payment", "code": "{{code}}", "steps": [
                { "sequence": 1, "approver": "lineManager", "label": "Manager" },
                { "sequence": 2, "approver": "departmentHead" },
                { "sequence": 3, "approver": "employee", "employee": "{{employee}}", "minAmount": 1000, "label": "Finance" }
            ] }
            """);
        using var read = await SendAsync(admin, HttpMethod.Get, $"{RequestTypes}/{id}");
        using var denied = await fixture.CreateClient().SignedInAs("appr-carl")
            .SendAsync(Request(HttpMethod.Post, RequestTypes, ApprovalsAppFixture.Host, $$"""{ "values": {{RequestType()}} }"""), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync(CancellationToken));
        var steps = body.RootElement.GetProperty("values").GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal([1, 2, 3], steps.Select(step => step.GetProperty("sequence").GetInt32()));
        Assert.Equal(["lineManager", "departmentHead", "employee"], steps.Select(step => step.GetProperty("approver").GetString()));
        Assert.Equal("Manager", steps[0].GetProperty("label").GetString());
        Assert.Equal(employee, steps[2].GetProperty("employee").GetGuid());
        Assert.Equal(1000m, steps[2].GetProperty("minAmount").GetDecimal());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private static string RequestType() =>
        $$"""{ "name": "Type", "code": "{{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}}" }""";

    private static string Employee(string? user = null)
    {
        var name = Guid.NewGuid().ToString("N");
        var account = user is null ? "" : $$""", "user": "{{user}}" """;
        return $$"""{ "fullName": "Employee {{name}}", "workEmail": "{{name}}@example.com"{{account}} }""";
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string records, string values)
    {
        using var response = await SendAsync(client, HttpMethod.Post, records, $$"""{ "values": {{values}} }""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body = null) =>
        client.SendAsync(Request(method, path, ApprovalsAppFixture.Host, body), CancellationToken);
}
