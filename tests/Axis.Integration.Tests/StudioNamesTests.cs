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

    private readonly TemporaryFolder _modules = new();

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

        // No shipped module holds a seed or a trigger, so the names test adds them to a copy of the modules.
        CopyDirectory(SampleModules.Path, _modules.Path);
        var budgets = Path.Combine(_modules.Path, "department-budgets", "1.2.0");
        AddBudgetSeed(Path.Combine(budgets, "seeds", "budgets.json"));
        AddBudgetTrigger(Path.Combine(budgets, "triggers", "review-new-budgets.json"));

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
            builder.UseSetting("Modules:Path", _modules.Path);
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
        _modules.Dispose();
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
        Assert.Equal("organization/Department", department.GetProperty("target").GetString());

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

        var entity = Named(names, "entities", "organization/Department");
        Assert.Equal("organization", entity.GetProperty("module").GetString());

        var rule = Named(names, "rules", "organization:IsDelegating");
        Assert.Equal("organization", rule.GetProperty("module").GetString());
        Assert.Equal(
            [("from", "date"), ("until", "date")],
            rule.GetProperty("parameters").EnumerateArray().Select(parameter => (parameter.GetProperty("name").GetString()!, parameter.GetProperty("type").GetString()!)));
        Assert.Equal("boolean", rule.GetProperty("resultType").GetString());

        Assert.Equal(JsonValueKind.Null, Named(names, "pages", "MyTasks").GetProperty("module").ValueKind);

        var policies = names.GetProperty("policies").EnumerateArray().ToList();
        Assert.Equal(
            [("DepartmentManagerAccess", null), ("EmployeeAccess", null), ("organization/AdminAccess", "organization"), ("organization/MemberAccess", "organization")],
            policies
                .Select(policy => (policy.GetProperty("name").GetString()!, policy.GetProperty("module").GetString()))
                .OrderBy(policy => policy.Item1, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Names_list_the_forms_texts_seeds_triggers_and_scenarios_of_a_module()
    {
        var names = await NamesAsync("PurchaseRequests");

        var scenario = JsonNode.Parse(File.ReadAllText(Path.Combine(
            SampleModules.Path, "department-budgets", "1.2.0", "scenarios", "approval-records-decision.json")))!["name"]!.GetValue<string>();
        foreach (var (list, name) in new[]
        {
            ("forms", "department-budgets/BudgetReview"),
            ("textResources", "department-budgets/TextsEn"),
            ("seeds", "department-budgets/Budgets"),
            ("triggers", "department-budgets/ReviewNewBudgets"),
            ("scenarios", $"department-budgets/{scenario}"),
        })
        {
            Assert.Equal("department-budgets", Named(names, list, name).GetProperty("module").GetString());
        }

        var ownForm = names.GetProperty("forms").EnumerateArray().First(form => !form.GetProperty("name").GetString()!.Contains('/'));
        Assert.Equal(JsonValueKind.Null, ownForm.GetProperty("module").ValueKind);
    }

    [Fact]
    public async Task Names_answer_what_resolved_when_a_process_file_has_errors()
    {
        var names = await NamesAsync("BrokenRequests");

        // Only the process of the department-budgets module resolves, the application's own process does not.
        var process = Assert.Single(names.GetProperty("processes").EnumerateArray());
        Assert.Equal("department-budgets/BudgetApproval", process.GetProperty("name").GetString());
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
    public async Task Each_listed_kind_has_a_label_key_that_is_a_string_or_null()
    {
        var names = await NamesAsync("PurchaseRequests");

        foreach (var list in _labelledLists)
        {
            foreach (var item in names.GetProperty(list).EnumerateArray())
            {
                AssertLabelKey(item, list);
            }
        }

        foreach (var entity in names.GetProperty("entities").EnumerateArray())
        {
            foreach (var field in entity.GetProperty("fields").EnumerateArray())
            {
                AssertLabelKey(field, "fields");
            }
        }

        foreach (var process in names.GetProperty("processes").EnumerateArray())
        {
            foreach (var step in process.GetProperty("steps").EnumerateArray())
            {
                AssertLabelKey(step, "steps");
            }
        }
    }

    [Fact]
    public async Task A_label_key_is_the_text_key_of_the_label_in_the_resource_file()
    {
        var names = await NamesAsync("PurchaseRequests");
        var entity = Named(names, "entities", "PurchaseRequest");
        var file = JsonNode.Parse(File.ReadAllText(Path.Combine(_purchaseRequests.Path, "entities", "purchase-request.json")))!;

        Assert.Equal(TextKeyOf(file), entity.GetProperty("labelKey").GetString());
        Assert.Equal(
            TextKeyOf(Item(file, "fields", "department")),
            Named(entity, "fields", "department").GetProperty("labelKey").GetString());

        var process = JsonNode.Parse(File.ReadAllText(Path.Combine(_purchaseRequests.Path, "processes", "purchase-request-approval.json")))!;
        Assert.Equal(
            TextKeyOf(Item(process, "steps", "managerApproval")),
            Named(Named(names, "processes", "PurchaseRequestApproval"), "steps", "managerApproval").GetProperty("labelKey").GetString());
        Assert.Equal(JsonValueKind.Null, Named(names, "processes", "PurchaseRequestApproval").GetProperty("labelKey").ValueKind);

        var role = JsonNode.Parse(File.ReadAllText(Path.Combine(_purchaseRequests.Path, "roles", "employee.json")))!;
        Assert.Equal(TextKeyOf(role), Named(names, "roles", "employee").GetProperty("labelKey").GetString());

        var page = JsonNode.Parse(File.ReadAllText(Path.Combine(_purchaseRequests.Path, "pages", "approvals.json")))!;
        Assert.Equal(TextKeyOf(page), Named(names, "pages", "Approvals").GetProperty("labelKey").GetString());
    }

    [Fact]
    public async Task A_module_label_key_is_module_prefixed_and_resolves_in_the_texts()
    {
        var names = await NamesAsync("PurchaseRequests");

        var budget = Named(names, "entities", "department-budgets/Budget");
        Assert.Equal("department-budgets.budget.label", budget.GetProperty("labelKey").GetString());
        var text = names.GetProperty("texts").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == "department-budgets.budget.label");
        Assert.Equal("Budget", text.GetProperty("texts").GetProperty("en").GetString());
    }

    [Fact]
    public async Task Columns_parameters_operations_and_event_fields_have_no_label_key()
    {
        var purchase = await NamesAsync("PurchaseRequests");
        var leave = await NamesAsync("LeaveRequests");

        // The column has a label in its file, and the answer still leaves the key out.
        AssertNoLabelKey(Named(Named(purchase, "dataSources", "PurchaseRequestList"), "columns", "status"));
        AssertNoLabelKey(Named(Named(purchase, "connectors", "PurchasingSystem"), "operations", "createPurchaseOrder"));
        AssertNoLabelKey(Named(Named(purchase, "events", "PaymentReceived"), "fields", "amount"));
        AssertNoLabelKey(Named(Named(leave, "rules", "organization:IsDelegating"), "parameters", "from"));
        AssertNoLabelKey(Named(Named(leave, "processes", "DailyReminder"), "parameters", "daysOverdue"));
    }

    [Fact]
    public async Task Every_entity_field_says_whether_it_is_unique()
    {
        var names = await NamesAsync("PurchaseRequests");

        var fields = names.GetProperty("entities").EnumerateArray()
            .SelectMany(entity => entity.GetProperty("fields").EnumerateArray())
            .ToList();
        Assert.All(fields, field => Assert.True(field.GetProperty("unique").ValueKind is (JsonValueKind.True or JsonValueKind.False)));

        var supplier = Named(names, "entities", "Supplier");
        Assert.True(Named(supplier, "fields", "code").GetProperty("unique").GetBoolean());
        Assert.False(Named(supplier, "fields", "name").GetProperty("unique").GetBoolean());
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

    private static readonly string[] _labelledLists =
    [
        "entities", "dataSources", "processes", "rules", "connectors", "events", "roles", "sequences", "forms", "pages",
        "policies", "contracts", "seeds", "triggers", "scenarios", "textResources",
    ];

    private static void AssertLabelKey(JsonElement item, string list) =>
        Assert.True(item.TryGetProperty("labelKey", out var key) && (key.ValueKind is JsonValueKind.String or JsonValueKind.Null), list);

    private static void AssertNoLabelKey(JsonElement item) => Assert.False(item.TryGetProperty("labelKey", out _));

    private static JsonNode Item(JsonNode file, string list, string name) =>
        file[list]!.AsArray().Single(item => item!["name"]!.GetValue<string>() == name)!;

    private static string? TextKeyOf(JsonNode node) => (node["label"] ?? node["title"])?["textKey"]?.GetValue<string>();

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

    private static void AddBudgetSeed(string file)
    {
        var seed = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["kind"] = "seed",
            ["name"] = "Budgets",
            ["formatVersion"] = 1,
            ["entity"] = "Budget",
            ["records"] = new JsonArray(new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString(),
                ["values"] = new JsonObject
                {
                    ["name"] = "Marketing 2026",
                    ["department"] = Guid.NewGuid().ToString(),
                    ["year"] = 2026,
                    ["amount"] = 1000,
                    ["status"] = "draft",
                },
            }),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, seed.ToJsonString());
    }

    private static void AddBudgetTrigger(string file)
    {
        var trigger = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["kind"] = "trigger",
            ["name"] = "ReviewNewBudgets",
            ["formatVersion"] = 1,
            ["type"] = "dataChange",
            ["entity"] = "Budget",
            ["on"] = new JsonArray("create"),
            ["process"] = "BudgetApproval",
        };
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, trigger.ToJsonString());
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
            ["widgets"] = new JsonArray(new JsonObject { ["type"] = "taskInbox", ["name"] = "taskInbox" }),
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
