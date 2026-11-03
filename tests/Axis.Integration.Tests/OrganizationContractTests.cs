using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The contracts of the built-in <c>organization</c> module, on the record API of OrganizationApp, which maps the slot
/// <c>admin</c> to <c>admin</c> and the slot <c>member</c> to <c>employee</c> and has no policy of its own. A department
/// is not its own parent, an employee is not their own manager or delegate, and a delegate needs both dates with the
/// end not before the start, each refused with a <c>400</c> at the field. A member reads both entities and updates only
/// <c>phone</c>, <c>delegate</c>, <c>delegateFrom</c> and <c>delegateUntil</c> of the employee whose <c>user</c> is the
/// member, and every other update is a <c>403</c>.
/// </summary>
public sealed class OrganizationContractTests(OrganizationAppFixture fixture) : IClassFixture<OrganizationAppFixture>
{
    private const string Admin = "org-admin";
    private const string Departments = "/api/apps/OrganizationApp/entities/organization/Department/records";
    private const string Employees = "/api/apps/OrganizationApp/entities/organization/Employee/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_admin_cannot_make_a_department_its_own_parent_but_can_give_it_another_parent()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var parent = await CreateAsync(admin, Departments, $$"""{ "code": "{{Code()}}", "name": "Parent" }""");
        var child = await CreateAsync(admin, Departments, $$"""{ "code": "{{Code()}}", "name": "Child" }""");

        using var own = await PatchAsync(admin, Departments, child, $$"""{ "parent": "{{child.Id}}" }""");
        using var other = await PatchAsync(admin, Departments, child, $$"""{ "parent": "{{parent.Id}}" }""");

        await AssertInvalidAsync(own, "/values/parent", "organization.department.notOwnParent");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task An_admin_cannot_make_an_employee_their_own_manager_but_can_give_them_another_manager()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var boss = await CreateAsync(admin, Employees, Employee());
        var employee = await CreateAsync(admin, Employees, Employee());

        using var own = await PatchAsync(admin, Employees, employee, $$"""{ "manager": "{{employee.Id}}" }""");
        using var other = await PatchAsync(admin, Employees, employee, $$"""{ "manager": "{{boss.Id}}" }""");

        await AssertInvalidAsync(own, "/values/manager", "organization.employee.notOwnManager");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task An_admin_cannot_make_an_employee_their_own_delegate_even_with_both_dates()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var employee = await CreateAsync(admin, Employees, Employee());

        using var response = await PatchAsync(
            admin,
            Employees,
            employee,
            $$"""{ "delegate": "{{employee.Id}}", "delegateFrom": "2026-11-10", "delegateUntil": "2026-11-12" }""");

        await AssertInvalidAsync(response, "/values/delegate", "organization.employee.notOwnDelegate");
    }

    [Fact]
    public async Task A_delegate_without_both_dates_is_refused_at_the_start_date_and_dates_without_a_delegate_are_allowed()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var delegateRecord = await CreateAsync(admin, Employees, Employee());
        var employee = await CreateAsync(admin, Employees, Employee());

        using var noDates = await PatchAsync(admin, Employees, employee, $$"""{ "delegate": "{{delegateRecord.Id}}" }""");
        using var onlyStart = await PatchAsync(
            admin, Employees, employee, $$"""{ "delegate": "{{delegateRecord.Id}}", "delegateFrom": "2026-11-10" }""");
        using var datesOnly = await PatchAsync(admin, Employees, employee, """{ "delegateFrom": "2026-11-10", "delegateUntil": "2026-11-12" }""");

        await AssertInvalidAsync(noDates, "/values/delegateFrom", "organization.employee.delegateHasDates");
        await AssertInvalidAsync(onlyStart, "/values/delegateFrom", "organization.employee.delegateHasDates");
        Assert.Equal(HttpStatusCode.OK, datesOnly.StatusCode);
    }

    [Fact]
    public async Task An_end_date_before_the_start_date_is_refused_at_the_end_date_and_the_same_day_is_allowed()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var delegateRecord = await CreateAsync(admin, Employees, Employee());
        var employee = await CreateAsync(admin, Employees, Employee());

        using var reversed = await PatchAsync(
            admin,
            Employees,
            employee,
            $$"""{ "delegate": "{{delegateRecord.Id}}", "delegateFrom": "2026-11-12", "delegateUntil": "2026-11-10" }""");
        using var sameDay = await PatchAsync(
            admin,
            Employees,
            employee,
            $$"""{ "delegate": "{{delegateRecord.Id}}", "delegateFrom": "2026-11-12", "delegateUntil": "2026-11-12" }""");

        await AssertInvalidAsync(reversed, "/values/delegateUntil", "organization.employee.delegateDatesInOrder");
        Assert.Equal(HttpStatusCode.OK, sameDay.StatusCode);
    }

    [Fact]
    public async Task A_member_updates_their_own_phone_delegate_and_dates()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var own = await CreateAsync(admin, Employees, Employee("org-ann"));
        var colleague = await CreateAsync(admin, Employees, Employee());
        using var member = fixture.CreateClient().SignedInAs("org-ann");

        using var phone = await PatchAsync(member, Employees, own, """{ "phone": "0901 000 111" }""");
        var next = (own.Id, await VersionAsync(phone));
        using var absence = await PatchAsync(
            member,
            Employees,
            next,
            $$"""{ "delegate": "{{colleague.Id}}", "delegateFrom": "2026-11-10", "delegateUntil": "2026-11-12" }""");

        Assert.Equal(HttpStatusCode.OK, phone.StatusCode);
        Assert.Equal(HttpStatusCode.OK, absence.StatusCode);
        using var stored = await GetAsync(admin, $"{Employees}/{own.Id}");
        var values = stored.RootElement.GetProperty("values");
        Assert.Equal("0901 000 111", values.GetProperty("phone").GetString());
        Assert.Equal(colleague.Id, values.GetProperty("delegate").GetGuid());
        Assert.Equal("2026-11-12", values.GetProperty("delegateUntil").GetString());
    }

    [Fact]
    public async Task A_member_cannot_update_their_own_job_title()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var own = await CreateAsync(admin, Employees, Employee("org-ann2"));
        using var member = fixture.CreateClient().SignedInAs("org-ann2");

        using var response = await PatchAsync(member, Employees, own, """{ "jobTitle": "Director" }""");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var stored = await GetAsync(admin, $"{Employees}/{own.Id}");
        Assert.Equal(JsonValueKind.Null, stored.RootElement.GetProperty("values").GetProperty("jobTitle").ValueKind);
    }

    [Fact]
    public async Task A_member_cannot_update_another_employees_phone()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        await CreateAsync(admin, Employees, Employee("org-ann3"));
        var other = await CreateAsync(admin, Employees, Employee());
        using var member = fixture.CreateClient().SignedInAs("org-ann3");

        using var response = await PatchAsync(member, Employees, other, """{ "phone": "0901 000 222" }""");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_member_reads_departments_and_employees_but_creates_and_deletes_neither()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var department = await CreateAsync(admin, Departments, $$"""{ "code": "{{Code()}}", "name": "Readable" }""");
        var employee = await CreateAsync(admin, Employees, Employee("org-ann4"));
        using var member = fixture.CreateClient().SignedInAs("org-ann4");

        using var readDepartment = await SendAsync(member, HttpMethod.Get, $"{Departments}/{department.Id}");
        using var readEmployee = await SendAsync(member, HttpMethod.Get, $"{Employees}/{employee.Id}");
        using var createDepartment = await SendAsync(
            member, HttpMethod.Post, Departments, $$"""{ "values": { "code": "{{Code()}}", "name": "Mine" } }""");
        using var updateDepartment = await PatchAsync(member, Departments, department, """{ "name": "Renamed" }""");
        using var deleteEmployee = await SendAsync(member, HttpMethod.Delete, $"{Employees}/{employee.Id}");

        Assert.Equal(HttpStatusCode.OK, readDepartment.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readEmployee.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createDepartment.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, updateDepartment.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deleteEmployee.StatusCode);
    }

    [Fact]
    public async Task A_member_still_meets_the_contract_validations_on_their_own_absence()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var own = await CreateAsync(admin, Employees, Employee("org-ann5"));
        using var member = fixture.CreateClient().SignedInAs("org-ann5");

        using var response = await PatchAsync(member, Employees, own, $$"""{ "delegate": "{{own.Id}}" }""");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Contains(problem.RootElement.GetProperty("errors").EnumerateObject(), error => error.Name == "/values/delegate");
    }

    private static string Code() => Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private static string Employee(string? user = null)
    {
        var name = Guid.NewGuid().ToString("N");
        var account = user is null ? "" : $$""", "user": "{{user}}" """;
        return $$"""{ "fullName": "Employee {{name}}", "workEmail": "{{name}}@example.com"{{account}} }""";
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

    private static async Task<long> VersionAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("version").GetInt64();
    }

    private static async Task<JsonDocument> GetAsync(HttpClient client, string path)
    {
        using var response = await SendAsync(client, HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static async Task AssertInvalidAsync(HttpResponseMessage response, string key, string message)
    {
        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal(key, error.Name);
        Assert.Equal([message], error.Value.EnumerateArray().Select(text => text.GetString()));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body = null) =>
        client.SendAsync(Request(method, path, OrganizationAppFixture.Host, body), CancellationToken);
}
