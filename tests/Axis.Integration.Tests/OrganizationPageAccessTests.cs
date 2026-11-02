using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The access the pages of the built-in <c>organization</c> module rely on, on OrganizationApp, which maps the slot
/// <c>admin</c> to <c>admin</c> and the slot <c>member</c> to <c>employee</c>. A member is refused the <c>Departments</c>
/// and <c>Employees</c> data sources, so those pages stay with administrators, and every write but their own absence.
/// The <c>OutOfOffice</c> data source holds only the employee whose <c>user</c> is the member. No member reads a
/// <c>phone</c>, and an administrator does.
/// </summary>
public sealed class OrganizationPageAccessTests(OrganizationAppFixture fixture) : IClassFixture<OrganizationAppFixture>
{
    private const string Admin = "org-admin";
    private const string Departments = "/api/apps/OrganizationApp/entities/organization/Department/records";
    private const string Employees = "/api/apps/OrganizationApp/entities/organization/Employee/records";
    private const string DataSources = "/api/apps/OrganizationApp/data-sources/organization";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_is_refused_other_employees_updates_department_writes_and_the_directory_data_sources()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        await CreateAsync(admin, Employees, Employee("org-ann6"));
        var other = await CreateAsync(admin, Employees, Employee());
        var department = await CreateAsync(admin, Departments, $$"""{ "code": "{{Code()}}", "name": "Sales" }""");
        using var member = fixture.CreateClient().SignedInAs("org-ann6");

        using var updateEmployee = await PatchAsync(member, Employees, other, """{ "delegateFrom": "2026-11-10" }""");
        using var createDepartment = await SendAsync(
            member, HttpMethod.Post, Departments, $$"""{ "values": { "code": "{{Code()}}", "name": "Mine" } }""");
        using var updateDepartment = await PatchAsync(member, Departments, department, """{ "name": "Renamed" }""");
        using var deleteDepartment = await SendAsync(member, HttpMethod.Delete, $"{Departments}/{department.Id}");
        using var departmentRows = await SendAsync(member, HttpMethod.Get, $"{DataSources}/Departments/rows");
        using var employeeRows = await SendAsync(member, HttpMethod.Get, $"{DataSources}/Employees/rows");

        foreach (var response in new[] { updateEmployee, createDepartment, updateDepartment, deleteDepartment, departmentRows, employeeRows })
        {
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
        }

        using var adminRows = await SendAsync(admin, HttpMethod.Get, $"{DataSources}/Employees/rows");
        Assert.Equal(HttpStatusCode.OK, adminRows.StatusCode);
    }

    [Fact]
    public async Task The_out_of_office_rows_of_a_member_hold_only_their_own_employee()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var own = await CreateAsync(admin, Employees, Employee("org-ann7", "Ann Seven"));
        await CreateAsync(admin, Employees, Employee());
        await CreateAsync(admin, Employees, Employee());
        using var member = fixture.CreateClient().SignedInAs("org-ann7");

        using var response = await SendAsync(member, HttpMethod.Get, $"{DataSources}/OutOfOffice/rows");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var item = Assert.Single(rows.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(own.Id, item.GetProperty("id").GetGuid());
        Assert.Equal("Ann Seven", item.GetProperty("values").GetProperty("fullName").GetString());
        Assert.Equal(1, rows.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task A_member_reads_no_phone_of_an_employee_and_an_administrator_does()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var own = await CreateAsync(admin, Employees, Employee("org-ann8", phone: "0901 000 333"));
        using var member = fixture.CreateClient().SignedInAs("org-ann8");

        using var asMember = await GetAsync(member, $"{Employees}/{own.Id}");
        using var asAdmin = await GetAsync(admin, $"{Employees}/{own.Id}");

        var memberValues = asMember.RootElement.GetProperty("values");
        Assert.False(memberValues.TryGetProperty("phone", out _));
        Assert.Equal("Employee", memberValues.GetProperty("fullName").GetString()![..8]);
        Assert.Equal("0901 000 333", asAdmin.RootElement.GetProperty("values").GetProperty("phone").GetString());
    }

    private static string Code() => Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private static string Employee(string? user = null, string? fullName = null, string? phone = null)
    {
        var name = Guid.NewGuid().ToString("N");
        var account = user is null ? "" : $$""", "user": "{{user}}" """;
        var number = phone is null ? "" : $$""", "phone": "{{phone}}" """;
        return $$"""{ "fullName": "{{fullName ?? $"Employee {name}"}}", "workEmail": "{{name}}@example.com"{{account}}{{number}} }""";
    }

    private static async Task<(Guid Id, long Version)> CreateAsync(HttpClient client, string records, string values)
    {
        using var response = await SendAsync(client, HttpMethod.Post, records, $$"""{ "values": {{values}} }""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return (body.RootElement.GetProperty("id").GetGuid(), body.RootElement.GetProperty("version").GetInt64());
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, string records, (Guid Id, long Version) record, string values) =>
        SendAsync(client, HttpMethod.Patch, $"{records}/{record.Id}", $$"""{ "version": {{record.Version}}, "values": {{values}} }""");

    private static async Task<JsonDocument> GetAsync(HttpClient client, string path)
    {
        using var response = await SendAsync(client, HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body = null) =>
        client.SendAsync(Request(method, path, OrganizationAppFixture.Host, body), CancellationToken);
}
