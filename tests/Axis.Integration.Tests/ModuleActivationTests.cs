using System.Net;
using System.Text.Json;
using Axis.Configuration.Modules;
using Axis.Data.Naming;
using Axis.Server.Tests;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class ModuleActivationTests(ModuleAppFixture fixture) : IClassFixture<ModuleAppFixture>
{
    private const string AppA = "/api/apps/ModuleAppA";
    private const string AppB = "/api/apps/ModuleAppB";
    private const string Departments = "entities/departments/Department/records";
    private const string Budgets = "entities/budgets/Budget/records";
    private const string DepartmentsDataSource = "data-sources/departments/Departments/rows";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_application_gets_its_own_table_for_a_module_entity()
    {
        var expected = new Dictionary<Guid, Guid>
        {
            [ModuleAppFixture.AppAId] = ModuleResourceIds.Derive(ModuleAppFixture.AppAId, ModuleAppFixture.DepartmentEntityId),
            [ModuleAppFixture.AppBId] = ModuleResourceIds.Derive(ModuleAppFixture.AppBId, ModuleAppFixture.DepartmentEntityId),
        };

        var tables = new Dictionary<Guid, string>();
        foreach (var (applicationId, entityId) in expected)
        {
            await using var command = fixture.DataSource.CreateCommand(
                "SELECT table_name FROM axis.provisioned_entities WHERE application_id = @application AND entity_id = @entity");
            command.Parameters.AddWithValue("application", applicationId);
            command.Parameters.AddWithValue("entity", entityId);
            var table = Assert.IsType<string>(await command.ExecuteScalarAsync(CancellationToken));
            Assert.Equal(EntityNaming.Table(entityId), table);
            tables[applicationId] = table;
        }

        Assert.NotEqual(tables[ModuleAppFixture.AppAId], tables[ModuleAppFixture.AppBId]);
        Assert.NotEqual(expected[ModuleAppFixture.AppAId], expected[ModuleAppFixture.AppBId]);
    }

    [Fact]
    public async Task A_department_created_in_one_application_is_not_listed_in_the_other()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        var code = $"ISO{Guid.NewGuid():N}"[..12];

        await CreateDepartmentAsync(client, AppA, code, "Isolated");

        Assert.Contains(code, await ListCodesAsync(client, AppA));
        Assert.DoesNotContain(code, await ListCodesAsync(client, AppB));
    }

    [Fact]
    public async Task Each_application_holds_the_seed_rows_of_the_module_in_its_own_table()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");

        foreach (var app in new[] { AppA, AppB })
        {
            foreach (var (id, code, name) in new[] { (ModuleAppFixture.Finance, "FIN", "Finance"), (ModuleAppFixture.Engineering, "ENG", "Engineering") })
            {
                var values = await ReadDepartmentAsync(client, app, id);
                Assert.Equal(code, values.GetProperty("code").GetString());
                Assert.Equal(name, values.GetProperty("name").GetString());
            }
        }
    }

    [Fact]
    public async Task A_reference_to_a_seeded_module_department_is_stored_and_read_back()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");

        using var create = RecordApiFixture.Request(
            HttpMethod.Post,
            $"{AppA}/entities/Employee/records",
            ModuleAppFixture.Host,
            $$"""{ "values": { "name": "Eve", "department": "{{ModuleAppFixture.Finance:D}}" } }""");
        using var created = await client.SendAsync(create, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync(CancellationToken));
        var id = createdBody.RootElement.GetProperty("id").GetGuid();

        using var read = RecordApiFixture.Request(HttpMethod.Get, $"{AppA}/entities/Employee/records/{id:D}", ModuleAppFixture.Host);
        using var response = await client.SendAsync(read, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(ModuleAppFixture.Finance, body.RootElement.GetProperty("values").GetProperty("department").GetGuid());
    }

    [Fact]
    public async Task Each_application_numbers_its_departments_from_one_with_its_own_counter()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");

        foreach (var app in new[] { AppA, AppB })
        {
            var values = await ReadDepartmentAsync(client, app, ModuleAppFixture.Finance);
            Assert.Equal("D-001", values.GetProperty("number").GetString());
        }

        foreach (var applicationId in new[] { ModuleAppFixture.AppAId, ModuleAppFixture.AppBId })
        {
            var sequenceId = ModuleResourceIds.Derive(applicationId, ModuleAppFixture.DepartmentSequenceId);
            await using var command = fixture.DataSource.CreateCommand(
                "SELECT count(*) FROM axis.sequence_counters WHERE application_id = @application AND sequence_id = @sequence AND last_value >= 2");
            command.Parameters.AddWithValue("application", applicationId);
            command.Parameters.AddWithValue("sequence", sequenceId);
            Assert.Equal(1L, await command.ExecuteScalarAsync(CancellationToken));
        }

        await using var total = fixture.DataSource.CreateCommand(
            "SELECT count(DISTINCT sequence_id) FROM axis.sequence_counters WHERE application_id = ANY(@applications)");
        total.Parameters.AddWithValue("applications", new[] { ModuleAppFixture.AppAId, ModuleAppFixture.AppBId });
        Assert.Equal(2L, await total.ExecuteScalarAsync(CancellationToken));
    }

    [Fact]
    public async Task The_module_entity_url_has_two_segments_and_needs_a_grant()
    {
        using var granted = fixture.CreateClient().SignedInAs("requester");
        using var denied = fixture.CreateClient().SignedInAs("manager");

        using (var request = RecordApiFixture.Request(HttpMethod.Get, $"{AppA}/{Departments}", ModuleAppFixture.Host))
        {
            using var response = await granted.SendAsync(request, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var request = RecordApiFixture.Request(HttpMethod.Get, $"{AppA}/{Departments}", ModuleAppFixture.Host))
        {
            using var response = await denied.SendAsync(request, CancellationToken);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Forbidden);
        }

        using (var request = RecordApiFixture.Request(HttpMethod.Get, $"{AppA}/entities/Department/records", ModuleAppFixture.Host))
        {
            using var response = await granted.SendAsync(request, CancellationToken);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task The_module_data_source_returns_the_seeded_departments_to_a_user_with_a_grant()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");

        using var request = RecordApiFixture.Request(HttpMethod.Get, $"{AppA}/{DepartmentsDataSource}?pageSize=100", ModuleAppFixture.Host);
        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var codes = body.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("values").GetProperty("code").GetString()!)
            .ToList();
        Assert.Contains("FIN", codes);
        Assert.Contains("ENG", codes);
    }

    [Fact]
    public async Task The_module_data_source_needs_its_own_grant()
    {
        using var withoutGrant = fixture.CreateClient().SignedInAs("requester");
        using var withoutRole = fixture.CreateClient().SignedInAs("manager");

        // In ModuleAppB, requester reads the module entity but holds no grant on the data source.
        using (var request = RecordApiFixture.Request(HttpMethod.Get, $"{AppB}/{Departments}", ModuleAppFixture.Host))
        {
            using var response = await withoutGrant.SendAsync(request, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var request = RecordApiFixture.Request(HttpMethod.Get, $"{AppB}/{DepartmentsDataSource}", ModuleAppFixture.Host))
        {
            using var response = await withoutGrant.SendAsync(request, CancellationToken);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Forbidden);
        }

        using (var request = RecordApiFixture.Request(HttpMethod.Get, $"{AppA}/{DepartmentsDataSource}", ModuleAppFixture.Host))
        {
            using var response = await withoutRole.SendAsync(request, CancellationToken);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Forbidden);
        }
    }

    [Theory]
    [InlineData("data-sources/payroll/Departments/rows")]
    [InlineData("data-sources/departments/Payroll/rows")]
    [InlineData("data-sources/Departments/rows")]
    public async Task A_module_data_source_path_that_names_nothing_is_a_404(string path)
    {
        using var client = fixture.CreateClient().SignedInAs("requester");

        using var request = RecordApiFixture.Request(HttpMethod.Get, $"{AppA}/{path}", ModuleAppFixture.Host);
        using var response = await client.SendAsync(request, CancellationToken);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(AppA)]
    [InlineData(AppB)]
    public async Task A_department_whose_code_breaks_the_module_rule_is_refused_in_each_application(string app)
    {
        using var client = fixture.CreateClient().SignedInAs("requester");

        using var request = RecordApiFixture.Request(
            HttpMethod.Post, $"{app}/{Departments}", ModuleAppFixture.Host, """{ "values": { "code": "X", "name": "Short" } }""");
        using var response = await client.SendAsync(request, CancellationToken);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal("/values/code", error.Name);
        Assert.Equal("departments.department.shortCode", Assert.Single(error.Value.EnumerateArray()).GetString());
    }

    [Fact]
    public async Task A_budget_of_a_module_that_depends_on_departments_points_at_a_department_and_reads_back()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");

        using var create = RecordApiFixture.Request(
            HttpMethod.Post,
            $"{AppA}/{Budgets}",
            ModuleAppFixture.Host,
            $$"""{ "values": { "code": "BUD-1", "department": "{{ModuleAppFixture.Finance:D}}" } }""");
        using var created = await client.SendAsync(create, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync(CancellationToken));
        var id = createdBody.RootElement.GetProperty("id").GetGuid();

        using var read = RecordApiFixture.Request(HttpMethod.Get, $"{AppA}/{Budgets}/{id:D}", ModuleAppFixture.Host);
        using var response = await client.SendAsync(read, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(ModuleAppFixture.Finance, body.RootElement.GetProperty("values").GetProperty("department").GetGuid());
    }

    [Fact]
    public async Task A_budget_whose_code_breaks_the_rule_of_the_module_it_depends_on_is_refused()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");

        using var request = RecordApiFixture.Request(
            HttpMethod.Post, $"{AppA}/{Budgets}", ModuleAppFixture.Host, """{ "values": { "code": "X" } }""");
        using var response = await client.SendAsync(request, CancellationToken);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal("/values/code", error.Name);
        Assert.Equal("budgets.budget.shortCode", Assert.Single(error.Value.EnumerateArray()).GetString());
    }

    private static async Task CreateDepartmentAsync(HttpClient client, string app, string code, string name)
    {
        using var request = RecordApiFixture.Request(
            HttpMethod.Post,
            $"{app}/{Departments}",
            ModuleAppFixture.Host,
            $$"""{ "values": { "code": "{{code}}", "name": "{{name}}" } }""");
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.StartsWith($"{app}/{Departments}/", response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    private static async Task<List<string>> ListCodesAsync(HttpClient client, string app)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Get, $"{app}/{Departments}?pageSize=100", ModuleAppFixture.Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return [.. body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("values").GetProperty("code").GetString()!)];
    }

    private static async Task<JsonElement> ReadDepartmentAsync(HttpClient client, string app, Guid id)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Get, $"{app}/{Departments}/{id:D}", ModuleAppFixture.Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("values").Clone();
    }
}
