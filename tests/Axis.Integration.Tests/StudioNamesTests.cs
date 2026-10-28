using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// The Studio names route answers every name an application defines from an in-memory compile, module
/// names included. It still answers when a file has errors, and it checks the session and the author role.
/// </summary>
public sealed class StudioNamesTests : IAsyncLifetime
{
    private const string Tenant = "a";

    private const string Host = "a.example.test";

    private readonly PostgreSqlFixture _database = new();

    private readonly TemporaryFolder _purchaseRequests = new();

    private readonly TemporaryFolder _leaveRequests = new();

    private readonly TemporaryFolder _broken = new();

    private WebApplicationFactory<Program> _factory = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        var samples = Path.Combine(AppContext.BaseDirectory, "Samples");
        CopyDirectory(Path.Combine(samples, "purchase-requests"), _purchaseRequests.Path);
        CopyDirectory(Path.Combine(samples, "leave-requests"), _leaveRequests.Path);
        AddProcessWithoutSubject(Path.Combine(_leaveRequests.Path, "processes", "daily-reminder.json"));
        AddEvent(Path.Combine(_purchaseRequests.Path, "events", "payment-received.json"));
        CopyDirectory(Path.Combine(samples, "purchase-requests"), _broken.Path);
        RenameApplication(Path.Combine(_broken.Path, "application.json"), "BrokenRequests");
        BreakProcess(Path.Combine(_broken.Path, "processes", "purchase-request-approval.json"));
        AddPageWithParameter(Path.Combine(_broken.Path, "pages", "request-detail.json"));
        AddGroupField(Path.Combine(_broken.Path, "data-sources", "requests-by-department.json"), "missingField");

        var connectionString = await _database.CreateDatabaseAsync("studio_names", CancellationToken);
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
            builder.UseSetting("Studio:Folders:0", _purchaseRequests.Path);
            builder.UseSetting("Studio:Folders:1", _leaveRequests.Path);
            builder.UseSetting("Studio:Folders:2", _broken.Path);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestAuthentication(
                new TestIdentity("author", "Author User", "author"),
                new TestIdentity("requester", "Requester User", "employee"));
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        _purchaseRequests.Dispose();
        _leaveRequests.Dispose();
        _broken.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task Names_list_the_entities_with_their_fields_and_the_rules_of_the_purchase_request_sample()
    {
        var names = await NamesAsync("PurchaseRequests");

        var entity = Named(names, "entities", "PurchaseRequest");
        Assert.Equal(JsonValueKind.Null, entity.GetProperty("module").ValueKind);
        var fields = entity.GetProperty("fields").EnumerateArray().ToList();
        Assert.All(fields, field => Assert.False(string.IsNullOrEmpty(field.GetProperty("type").GetString())));
        var department = fields.Single(field => field.GetProperty("name").GetString() == "department");
        Assert.Equal("Department", department.GetProperty("target").GetString());

        var rule = Named(names, "rules", "FinanceThreshold");
        Assert.Empty(rule.GetProperty("parameters").EnumerateArray());
        Assert.Equal("decimal", rule.GetProperty("resultType").GetString());
        Assert.Equal(JsonValueKind.Null, rule.GetProperty("module").ValueKind);
    }

    [Fact]
    public async Task Names_list_the_steps_and_outcomes_of_a_process()
    {
        var names = await NamesAsync("PurchaseRequests");

        var process = Named(names, "processes", "PurchaseRequestApproval");
        Assert.Equal("PurchaseRequest", process.GetProperty("entity").GetString());
        var step = Named(process, "steps", "managerApproval");
        Assert.Equal("task", step.GetProperty("type").GetString());
        Assert.Equal(["approve", "return", "reject"], step.GetProperty("outcomes").EnumerateArray().Select(outcome => outcome.GetString()));
        Assert.Empty(process.GetProperty("parameters").EnumerateArray());
    }

    [Fact]
    public async Task Names_list_each_event_with_its_entity()
    {
        var names = await NamesAsync("PurchaseRequests");

        var @event = Named(names, "events", "PaymentReceived");
        Assert.Equal("PurchaseRequest", @event.GetProperty("entity").GetString());
        var field = Assert.Single(@event.GetProperty("fields").EnumerateArray());
        Assert.Equal(("amount", "decimal"), (field.GetProperty("name").GetString(), field.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task Names_list_the_parameters_of_a_process_without_a_subject()
    {
        var names = await NamesAsync("LeaveRequests");

        var process = Named(names, "processes", "DailyReminder");
        Assert.Equal(JsonValueKind.Null, process.GetProperty("entity").ValueKind);
        var parameter = Assert.Single(process.GetProperty("parameters").EnumerateArray());
        Assert.Equal("daysOverdue", parameter.GetProperty("name").GetString());
        Assert.Equal("integer", parameter.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Names_list_the_group_fields_and_measures_of_a_grouped_data_source()
    {
        var names = await NamesAsync("PurchaseRequests");

        var grouped = Named(names, "dataSources", "RequestsByDepartment");
        Assert.Equal(["department"], grouped.GetProperty("groupBy").EnumerateArray().Select(field => field.GetString()));
        var groupedColumns = grouped.GetProperty("columns").EnumerateArray().ToList();
        Assert.Equal(["department", "requests", "total"], groupedColumns.Select(column => column.GetProperty("name").GetString()));
        Assert.Equal("integer", groupedColumns[1].GetProperty("type").GetString());
        Assert.Equal("decimal", groupedColumns[2].GetProperty("type").GetString());

        var plain = Named(names, "dataSources", "PurchaseRequestList");
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("groupBy").ValueKind);
        Assert.Equal(
            ["title", "requester", "department", "supplier", "currency", "totalAmount", "status", "submittedAt"],
            plain.GetProperty("columns").EnumerateArray().Select(column => column.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Names_list_a_group_field_that_names_no_field_with_a_null_type()
    {
        var names = await NamesAsync("BrokenRequests");

        var grouped = Named(names, "dataSources", "RequestsByDepartment");
        Assert.Equal(["department", "missingField"], grouped.GetProperty("groupBy").EnumerateArray().Select(field => field.GetString()));
        var columns = grouped.GetProperty("columns").EnumerateArray().ToList();
        Assert.Equal(["department", "missingField", "requests", "total"], columns.Select(column => column.GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Null, columns[1].GetProperty("type").ValueKind);
    }

    [Fact]
    public async Task Names_mark_the_entities_and_rules_of_a_module()
    {
        var names = await NamesAsync("LeaveRequests");

        var entity = Named(names, "entities", "departments/Department");
        Assert.Equal("departments", entity.GetProperty("module").GetString());

        var rule = Named(names, "rules", "departments:IsManager");
        Assert.Equal("departments", rule.GetProperty("module").GetString());
        Assert.Equal(
            [("manager", "text"), ("user", "text")],
            rule.GetProperty("parameters").EnumerateArray().Select(parameter => (parameter.GetProperty("name").GetString()!, parameter.GetProperty("type").GetString()!)));
        Assert.Equal("boolean", rule.GetProperty("resultType").GetString());
    }

    [Fact]
    public async Task Names_answer_what_resolved_when_a_process_file_has_errors()
    {
        var names = await NamesAsync("BrokenRequests");

        Assert.Empty(names.GetProperty("processes").EnumerateArray());
        Named(names, "entities", "PurchaseRequest");
        Named(names, "roles", "employee");
    }

    [Fact]
    public async Task Names_list_the_parameters_of_each_page()
    {
        var names = await NamesAsync("BrokenRequests");

        var page = Named(names, "pages", "RequestDetail");
        var parameter = Assert.Single(page.GetProperty("parameters").EnumerateArray());
        Assert.Equal("request", parameter.GetProperty("name").GetString());
        Assert.Equal("reference", parameter.GetProperty("type").GetString());
        Assert.True(parameter.GetProperty("required").GetBoolean());
        Assert.Empty(Named(names, "pages", "Approvals").GetProperty("parameters").EnumerateArray());
    }

    [Fact]
    public async Task Names_list_the_locales_and_each_text_key_with_its_label_uses()
    {
        var names = await NamesAsync("PurchaseRequests");

        Assert.Equal(["en", "vi"], names.GetProperty("locales").EnumerateArray().Select(locale => locale.GetString()));
        var text = names.GetProperty("texts").EnumerateArray().Single(item => item.GetProperty("key").GetString() == "nav.departments");
        Assert.Equal("Departments", text.GetProperty("texts").GetProperty("en").GetString());
        Assert.Equal("Phòng ban", text.GetProperty("texts").GetProperty("vi").GetString());
        Assert.True(text.GetProperty("usedBy").GetInt32() >= 1);
    }

    [Fact]
    public async Task Names_need_a_session_the_author_role_and_a_known_application()
    {
        using var anonymous = _factory.CreateClient();
        using var anonymousResponse = await anonymous.SendAsync(RecordApiFixture.Request("/api/studio/apps/PurchaseRequests/names", Host), CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var employee = _factory.CreateClient().SignedInAs("requester");
        using var employeeResponse = await employee.SendAsync(RecordApiFixture.Request("/api/studio/apps/PurchaseRequests/names", Host), CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, employeeResponse.StatusCode);

        using var author = _factory.CreateClient().SignedInAs("author");
        using var unknownResponse = await author.SendAsync(RecordApiFixture.Request("/api/studio/apps/Unknown/names", Host), CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);
    }

    private async Task<JsonElement> NamesAsync(string application)
    {
        using var client = _factory.CreateClient().SignedInAs("author");
        using var request = RecordApiFixture.Request($"/api/studio/apps/{application}/names", Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)).RootElement.Clone();
    }

    private static JsonElement Named(JsonElement parent, string list, string name) =>
        parent.GetProperty(list).EnumerateArray().Single(item => item.GetProperty("name").GetString() == name);

    private static void RenameApplication(string file, string name)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        manifest["name"] = name;
        File.WriteAllText(file, manifest.ToJsonString());
    }

    private static void BreakProcess(string file)
    {
        var process = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        process["steps"] = "broken";
        File.WriteAllText(file, process.ToJsonString());
    }

    private static void AddProcessWithoutSubject(string file)
    {
        var process = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["kind"] = "process",
            ["name"] = "DailyReminder",
            ["formatVersion"] = 1,
            ["parameters"] = new JsonArray(new JsonObject { ["name"] = "daysOverdue", ["type"] = "integer" }),
            ["start"] = "done",
            ["steps"] = new JsonArray(new JsonObject { ["name"] = "done", ["type"] = "end" }),
        };
        File.WriteAllText(file, process.ToJsonString());
    }

    private static void AddEvent(string file)
    {
        var @event = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["kind"] = "event",
            ["name"] = "PaymentReceived",
            ["formatVersion"] = 1,
            ["entity"] = "PurchaseRequest",
            ["fields"] = new JsonArray(new JsonObject { ["name"] = "amount", ["type"] = "decimal" }),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, @event.ToJsonString());
    }

    private static void AddGroupField(string file, string field)
    {
        var dataSource = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        dataSource["aggregate"]!["groupBy"]!.AsArray().Add(field);
        File.WriteAllText(file, dataSource.ToJsonString());
    }

    private static void AddPageWithParameter(string file)
    {
        var page = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["kind"] = "page",
            ["name"] = "RequestDetail",
            ["formatVersion"] = 1,
            ["title"] = new JsonObject { ["textKey"] = "pages.approvals.title" },
            ["parameters"] = new JsonArray(new JsonObject
            {
                ["name"] = "request",
                ["type"] = "reference",
                ["target"] = "PurchaseRequest",
                ["required"] = true,
            }),
            ["widgets"] = new JsonArray(new JsonObject { ["type"] = "taskInbox" }),
        };
        File.WriteAllText(file, page.ToJsonString());
    }

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
