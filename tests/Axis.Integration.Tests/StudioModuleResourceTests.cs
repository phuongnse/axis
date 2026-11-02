using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// The Studio module resource route answers one file of the module version an application pins. It checks the
/// session and the author role, and answers 404 for an unknown application, module or resource. Its page metadata route
/// answers a module page the same way.
/// </summary>
public sealed class StudioModuleResourceTests : IAsyncLifetime
{
    private const string Tenant = "a";

    private const string Host = "a.example.test";

    private const string DepartmentId = "3a5633d1-fc29-4997-b93d-36a1a865c8e3";

    private const string ModulePageId = "5fc44a93-1087-417f-b29d-85ea3ce86be1";

    private readonly PostgreSqlFixture _database = new();

    private const string BudgetApprovalId = "94f1cc25-6a68-435f-9383-36fc43aae860";

    private const string BudgetLayerFile = "customizations/department-budgets/BudgetApproval.json";

    private readonly TemporaryFolder _leaveRequests = new();

    private readonly TemporaryFolder _purchaseRequests = new();

    private WebApplicationFactory<Program> _factory = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "leave-requests"), _leaveRequests.Path);
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), _purchaseRequests.Path);

        var connectionString = await _database.CreateDatabaseAsync("studio_module_resource", CancellationToken);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(connectionString);
            builder.UseSetting($"Tenants:{Tenant}:Hosts:0", Host);
            builder.UseSetting($"Tenants:{Tenant}:ConnectionString", PostgreSqlServer.RuntimeConnectionString(connectionString));
            builder.UseSetting($"Tenants:{Tenant}:SchemaConnectionString", connectionString);
            builder.UseTestOidc(Tenant);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Modules:Path", SampleModules.Path);
            builder.UseSetting("Studio:Folders:0", _leaveRequests.Path);
            builder.UseSetting("Studio:Folders:1", _purchaseRequests.Path);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestAuthentication(
                new TestIdentity("author", "Author User", "author"),
                new TestIdentity("requester", "Requester User", "employee"));
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        _leaveRequests.Dispose();
        _purchaseRequests.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task An_author_reads_a_module_entity_with_its_fields()
    {
        using var client = _factory.CreateClient().SignedInAs("author");
        using var response = await client.SendAsync(Request("LeaveRequests", "departments", DepartmentId), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("ETag"));
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)).RootElement;
        Assert.Equal(DepartmentId, body.GetProperty("id").GetString());
        Assert.Equal("entity", body.GetProperty("kind").GetString());
        Assert.Equal("Department", body.GetProperty("name").GetString());
        Assert.Equal("modules/departments/entities/department.json", body.GetProperty("file").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("version").GetString()));
        Assert.Equal(
            ["code", "name", "manager"],
            body.GetProperty("content").GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("layer").ValueKind);
        Assert.Equal(0, body.GetProperty("marks").GetArrayLength());
        Assert.Equal(0, body.GetProperty("removed").GetArrayLength());
    }

    [Fact]
    public async Task An_author_reads_a_module_process_with_the_application_layer_applied()
    {
        const string layer = """
            { "id": "6f1d9c2a-3b4e-4d58-9a7c-1e2f3a4b5c6d", "kind": "layer", "formatVersion": 1, "target": "department-budgets/BudgetApproval",
              "changes": { "steps": [
                { "name": "review", "dueIn": "P3D", "outcomes": [{ "name": "reject", "remove": true }] },
                { "name": "archive", "after": "markRejected", "type": "end" } ] } }
            """;
        var path = Path.Combine(_purchaseRequests.Path, BudgetLayerFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, layer, CancellationToken);
        var layerVersion = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, CancellationToken)));

        using var client = _factory.CreateClient().SignedInAs("author");
        using var response = await client.SendAsync(Request("PurchaseRequests", "department-budgets", BudgetApprovalId), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)).RootElement;
        Assert.Equal("modules/department-budgets/processes/budget-approval.json", body.GetProperty("file").GetString());
        var content = body.GetProperty("content");
        var steps = content.GetProperty("steps");
        Assert.Equal(
            ["review", "markApproved", "markRejected", "archive", "done"],
            steps.EnumerateArray().Select(step => step.GetProperty("name").GetString()));
        Assert.Equal("P3D", steps[0].GetProperty("dueIn").GetString());
        Assert.Equal(["approve"], steps[0].GetProperty("outcomes").EnumerateArray().Select(outcome => outcome.GetProperty("name").GetString()));

        var layerBody = body.GetProperty("layer");
        Assert.Equal("6f1d9c2a-3b4e-4d58-9a7c-1e2f3a4b5c6d", layerBody.GetProperty("id").GetString());
        Assert.Equal(BudgetLayerFile, layerBody.GetProperty("file").GetString());
        Assert.Equal(layerVersion, layerBody.GetProperty("version").GetString());

        Assert.Equal(
            [("/steps/0/dueIn", "added"), ("/steps/3", "added"), ("/steps/0", "changed")],
            body.GetProperty("marks").EnumerateArray().Select(mark => (mark.GetProperty("path").GetString(), mark.GetProperty("mark").GetString())));

        var removed = Assert.Single(body.GetProperty("removed").EnumerateArray());
        Assert.Equal("/steps/0/outcomes", removed.GetProperty("list").GetString());
        Assert.Equal("reject", removed.GetProperty("key").GetString());
        Assert.Equal("reject", removed.GetProperty("content").GetProperty("name").GetString());
        Assert.Equal("markRejected", removed.GetProperty("content").GetProperty("next").GetString());
    }

    [Fact]
    public async Task The_route_needs_a_session_and_the_author_role()
    {
        using var anonymous = _factory.CreateClient();
        using var anonymousResponse = await anonymous.SendAsync(Request("LeaveRequests", "departments", DepartmentId), CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var employee = _factory.CreateClient().SignedInAs("requester");
        using var employeeResponse = await employee.SendAsync(Request("LeaveRequests", "departments", DepartmentId), CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, employeeResponse.StatusCode);
    }

    [Theory]
    [InlineData("Unknown", "departments", DepartmentId)]
    [InlineData("LeaveRequests", "nope", DepartmentId)]
    [InlineData("LeaveRequests", "departments", "5b0f6e3c-2d8a-4c1e-9a77-0e4f1d2c3b4a")]
    [InlineData("LeaveRequests", "departments", "not-a-guid")]
    public async Task The_route_answers_404_for_an_unknown_application_module_or_resource(string application, string module, string id)
    {
        using var client = _factory.CreateClient().SignedInAs("author");
        using var response = await client.SendAsync(Request(application, module, id), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_author_reads_the_metadata_of_a_module_page()
    {
        using var client = _factory.CreateClient().SignedInAs("author");
        using var response = await client.SendAsync(PageRequest("LeaveRequests", "departments", ModulePageId), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)).RootElement;
        var page = body.GetProperty("page");
        Assert.Equal("departments/Departments", page.GetProperty("name").GetString());
        var widget = page.GetProperty("widgets")[0];
        Assert.Equal("table", widget.GetProperty("type").GetString());
        Assert.Equal("/widgets/0", widget.GetProperty("path").GetString());
        Assert.Equal(0, body.GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public async Task The_module_page_route_needs_a_session_and_the_author_role()
    {
        using var anonymous = _factory.CreateClient();
        using var anonymousResponse = await anonymous.SendAsync(PageRequest("LeaveRequests", "departments", ModulePageId), CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var employee = _factory.CreateClient().SignedInAs("requester");
        using var employeeResponse = await employee.SendAsync(PageRequest("LeaveRequests", "departments", ModulePageId), CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, employeeResponse.StatusCode);
    }

    [Theory]
    [InlineData("Unknown", "departments", ModulePageId)]
    [InlineData("LeaveRequests", "nope", ModulePageId)]
    [InlineData("LeaveRequests", "departments", DepartmentId)]
    [InlineData("LeaveRequests", "departments", "5b0f6e3c-2d8a-4c1e-9a77-0e4f1d2c3b4a")]
    [InlineData("LeaveRequests", "departments", "not-a-guid")]
    public async Task The_module_page_route_answers_404_for_an_unknown_application_module_or_resource_or_one_that_is_not_a_page(
        string application, string module, string id)
    {
        using var client = _factory.CreateClient().SignedInAs("author");
        using var response = await client.SendAsync(PageRequest(application, module, id), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static HttpRequestMessage PageRequest(string application, string module, string id) =>
        RecordApiFixture.Request($"/api/studio/apps/{application}/modules/{module}/resources/{id}/page-metadata", Host);

    private static HttpRequestMessage Request(string application, string module, string id) =>
        RecordApiFixture.Request($"/api/studio/apps/{application}/modules/{module}/resources/{id}", Host);

    private static void CopyDirectory(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
    }
}
