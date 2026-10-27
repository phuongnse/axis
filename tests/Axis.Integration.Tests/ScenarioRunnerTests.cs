using System.Xml.Linq;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data.Storage;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Scenarios;
using Axis.Scenarios.Runner;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>The scenario runner against a real PostgreSQL server, one temporary database per scenario.</summary>
public sealed class ScenarioRunnerTests
{
    private const string Clerk = """{ "anna": ["clerk"] }""";
    private const string Reader = """{ "rita": ["reader"] }""";

    private static readonly Lazy<PackageCatalog> _packages = new(() => PackageCatalog.Load(SamplePackages.Path));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_scenario_that_creates_updates_and_expects_a_record_passes_and_drops_its_database()
    {
        using var folder = Application(Scenario("CreateAndUpdate", Clerk, """
            { "type": "createRecord", "entity": "Note", "as": "anna", "save": "note", "values": { "title": "Desk", "quantity": 3 } },
            { "type": "expectRecord", "record": "note", "values": { "title": "Desk", "quantity": 3 } },
            { "type": "updateRecord", "record": "note", "as": "anna", "values": { "title": "Desk", "quantity": 5 } },
            { "type": "expectRecord", "record": "note", "values": { "quantity": 5.0 } }
            """));

        var result = await RunAsync(folder, "CreateAndUpdate");

        Assert.True(result.Passed, result.Failure);
        Assert.StartsWith("axis_scenario_", result.Database);
        Assert.False(await DatabaseExistsAsync(result.Database));
    }

    [Fact]
    public async Task An_expectation_that_differs_fails_with_the_step_the_field_and_both_values_and_drops_the_database()
    {
        using var folder = Application(Scenario("Mismatch", Clerk, """
            { "type": "createRecord", "entity": "Note", "as": "anna", "save": "note", "values": { "title": "Desk", "quantity": 3 } },
            { "type": "updateRecord", "record": "note", "as": "anna", "values": { "title": "Desk", "quantity": 4 } },
            { "type": "expectRecord", "record": "note", "values": { "title": "Desk", "quantity": 3 } }
            """));

        var result = await RunAsync(folder, "Mismatch");

        Assert.False(result.Passed);
        Assert.Contains("Step 3 (expectRecord)", result.Failure);
        Assert.Contains("'quantity'", result.Failure);
        Assert.Contains("expected 3", result.Failure);
        Assert.Contains("but was 4", result.Failure);
        Assert.False(await DatabaseExistsAsync(result.Database));
    }

    [Fact]
    public async Task A_scenario_of_the_purchase_request_sample_passes_from_files_held_in_memory_and_drops_its_database()
    {
        var files = ReadJsonFiles(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"));
        var scenario = Axis.Configuration.Loading.ApplicationLoader.Load(files).Scenarios.Single(candidate => candidate.Name == "ApprovalBelowThreshold");

        var result = await ScenarioRunner.RunAsync(
            files,
            scenario,
            await PostgreSqlServer.AdminConnectionStringAsync(),
            NullLoggerFactory.Instance,
            CancellationToken,
            packages: _packages.Value,
            modules: ModuleCatalog.Load(SampleModules.Path));

        Assert.True(result.Passed, result.Failure);
        Assert.Null(result.Step);
        Assert.StartsWith("axis_scenario_", result.Database);
        Assert.False(await DatabaseExistsAsync(result.Database));
    }

    [Fact]
    public async Task A_failing_third_step_reports_its_index_and_its_message_without_the_step_prefix()
    {
        using var folder = Application(Scenario("ThirdStep", Clerk, """
            { "type": "createRecord", "entity": "Note", "as": "anna", "save": "note", "values": { "title": "Desk", "quantity": 3 } },
            { "type": "updateRecord", "record": "note", "as": "anna", "values": { "title": "Desk", "quantity": 4 } },
            { "type": "expectRecord", "record": "note", "values": { "title": "Desk", "quantity": 3 } }
            """));

        var result = await RunAsync(folder, "ThirdStep");

        Assert.False(result.Passed);
        Assert.Equal(2, result.Step);
        Assert.Equal("expectRecord", result.StepType);
        Assert.Contains("'quantity'", result.Message);
        Assert.Contains("expected 3", result.Message);
        Assert.Contains("but was 4", result.Message);
        Assert.False(result.Message!.StartsWith("Step ", StringComparison.Ordinal));
        Assert.StartsWith("Step 3 (expectRecord): ", result.Failure);
        Assert.False(await DatabaseExistsAsync(result.Database));
    }

    [Fact]
    public async Task A_scenario_of_an_application_that_does_not_compile_reports_no_step()
    {
        using var folder = Application(Scenario("Broken", Clerk, """
            { "type": "createRecord", "entity": "Missing", "as": "anna", "values": { "title": "Desk" } }
            """));

        var result = await RunAsync(folder, "Broken");

        Assert.False(result.Passed);
        Assert.Null(result.Step);
        Assert.Null(result.StepType);
        Assert.StartsWith("The application could not be compiled", result.Message);
        Assert.False(await DatabaseExistsAsync(result.Database));
    }

    [Fact]
    public async Task A_create_by_a_user_without_a_create_grant_fails_with_the_denied_outcome()
    {
        using var folder = Application(Scenario("Denied", """{ "rita": ["reader"] }""", """
            { "type": "createRecord", "entity": "Note", "as": "rita", "values": { "title": "Desk" } }
            """));

        var result = await RunAsync(folder, "Denied");

        Assert.False(result.Passed);
        Assert.StartsWith("Step 1 (createRecord): ", result.Failure);
        Assert.Contains("Denied", result.Failure);
        Assert.False(await DatabaseExistsAsync(result.Database));
    }

    [Fact]
    public async Task Numbers_compare_by_value_date_times_as_instants_and_child_rows_by_the_fields_they_name()
    {
        using var folder = Application(Scenario("Rows", Clerk, """
            {
              "type": "createRecord", "entity": "Note", "as": "anna", "save": "note",
              "values": {
                "title": "Desk",
                "dueAt": "2026-10-01T09:00:00+02:00",
                "lines": [{ "text": "Leg", "price": 12.5 }, { "text": "Top", "price": 40 }]
              }
            },
            {
              "type": "expectRecord", "record": "note",
              "values": {
                "dueAt": "2026-10-01T07:00:00Z",
                "lines": [{ "price": 12.50 }, { "text": "Top", "price": 40.00 }]
              }
            }
            """));

        var result = await RunAsync(folder, "Rows");

        Assert.True(result.Passed, result.Failure);
    }

    [Fact]
    public async Task A_child_row_that_differs_is_named_by_its_collection_index_and_field()
    {
        using var folder = Application(
            Scenario("Row", Clerk, """
                { "type": "createRecord", "entity": "Note", "as": "anna", "save": "note", "values": { "title": "Desk", "lines": [{ "text": "Leg", "price": 12.5 }] } },
                { "type": "expectRecord", "record": "note", "values": { "lines": [{ "text": "Top" }] } }
                """),
            Scenario("Count", Clerk, """
                { "type": "createRecord", "entity": "Note", "as": "anna", "save": "note", "values": { "title": "Desk", "lines": [{ "text": "Leg" }] } },
                { "type": "expectRecord", "record": "note", "values": { "lines": [] } }
                """));

        var row = await RunAsync(folder, "Row");
        var count = await RunAsync(folder, "Count");

        Assert.Equal("Step 2 (expectRecord): field 'lines/0/text' expected \"Top\" but was \"Leg\"", row.Failure);
        Assert.Equal("Step 2 (expectRecord): field 'lines' expected 0 row(s) but was 1 row(s)", count.Failure);
    }

    [Fact]
    public async Task A_create_by_a_user_without_a_create_grant_passes_with_expect_denied()
    {
        using var folder = Application(Scenario("DeniedExpected", Reader, """
            { "type": "createRecord", "entity": "Note", "as": "rita", "values": { "title": "Desk" }, "expect": "denied" }
            """));

        var result = await RunAsync(folder, "DeniedExpected");

        Assert.True(result.Passed, result.Failure);
    }

    [Fact]
    public async Task A_create_without_a_required_field_passes_with_expect_invalid_at_that_path_and_fails_at_another()
    {
        using var folder = Application(
            Scenario("RightPath", Clerk, """
                { "type": "createRecord", "entity": "Note", "as": "anna", "values": { "quantity": 1 }, "expect": { "invalid": "title" } }
                """),
            Scenario("WrongPath", Clerk, """
                { "type": "createRecord", "entity": "Note", "as": "anna", "values": { "quantity": 1 }, "expect": { "invalid": "quantity" } }
                """));

        var right = await RunAsync(folder, "RightPath");
        var wrong = await RunAsync(folder, "WrongPath");

        Assert.True(right.Passed, right.Failure);
        Assert.False(wrong.Passed);
        Assert.Contains("Step 1 (createRecord)", wrong.Failure);
        Assert.Contains("Invalid", wrong.Failure);
        Assert.Contains("/values/title", wrong.Failure);
    }

    [Fact]
    public async Task A_second_create_with_the_same_unique_value_passes_with_expect_conflict()
    {
        using var folder = Application(Scenario("Conflict", Clerk, """
            { "type": "createRecord", "entity": "Note", "as": "anna", "values": { "title": "Desk", "code": "A-1" } },
            { "type": "createRecord", "entity": "Note", "as": "anna", "values": { "title": "Chair", "code": "A-1" }, "expect": "conflict" }
            """));

        var result = await RunAsync(folder, "Conflict");

        Assert.True(result.Passed, result.Failure);
    }

    [Fact]
    public async Task An_update_and_a_delete_of_a_deleted_record_pass_with_expect_not_found()
    {
        using var folder = Application(Scenario("Gone", Clerk, """
            { "type": "createRecord", "entity": "Note", "as": "anna", "save": "note", "values": { "title": "Desk" } },
            { "type": "deleteRecord", "record": "note", "as": "anna" },
            { "type": "updateRecord", "record": "note", "as": "anna", "values": { "title": "Chair" }, "expect": "notFound" },
            { "type": "deleteRecord", "record": "note", "as": "anna", "expect": "notFound" }
            """));

        var result = await RunAsync(folder, "Gone");

        Assert.True(result.Passed, result.Failure);
    }

    [Fact]
    public async Task An_update_of_a_deleted_record_without_expect_fails_with_the_not_found_outcome()
    {
        using var folder = Application(Scenario("GoneUnexpected", Clerk, """
            { "type": "createRecord", "entity": "Note", "as": "anna", "save": "note", "values": { "title": "Desk" } },
            { "type": "deleteRecord", "record": "note", "as": "anna" },
            { "type": "updateRecord", "record": "note", "as": "anna", "values": { "title": "Chair" } }
            """));

        var result = await RunAsync(folder, "GoneUnexpected");

        Assert.Equal("Step 3 (updateRecord): expected success but the outcome was NotFound", result.Failure);
    }

    [Fact]
    public async Task A_create_that_succeeds_despite_expect_denied_fails_with_the_expectation_and_the_outcome()
    {
        using var folder = Application(Scenario("Unexpected", Clerk, """
            { "type": "createRecord", "entity": "Note", "as": "anna", "values": { "title": "Desk" }, "expect": "denied" }
            """));

        var result = await RunAsync(folder, "Unexpected");

        Assert.Equal("Step 1 (createRecord): expected denied but the outcome was Written", result.Failure);
    }

    [Fact]
    public async Task A_folder_with_a_passing_and_a_failing_scenario_exits_with_1_and_writes_both_to_the_junit_file()
    {
        using var folder = Application(
            Scenario("Passing", Clerk, """
                { "type": "createRecord", "entity": "Note", "as": "anna", "save": "note", "values": { "title": "Desk" } },
                { "type": "expectRecord", "record": "note", "values": { "title": "Desk" } }
                """),
            Scenario("Failing", Clerk, """
                { "type": "createRecord", "entity": "Note", "as": "anna", "save": "note", "values": { "title": "Desk" } },
                { "type": "expectRecord", "record": "note", "values": { "title": "Chair" } }
                """));
        using var results = new TemporaryFolder();
        var output = new StringWriter();

        var exitCode = await ScenarioCommand.RunAsync(
            [folder.Path, "--server", await PostgreSqlServer.AdminConnectionStringAsync(), "--results", results.Path], output, CancellationToken);

        Assert.Equal(1, exitCode);
        var suite = XDocument.Load(Path.Combine(results.Path, "scenarios-ScenarioApp.xml")).Root!.Element("testsuite")!;
        Assert.Equal("2", suite.Attribute("tests")!.Value);
        Assert.Equal("1", suite.Attribute("failures")!.Value);
        var cases = suite.Elements("testcase").ToList();
        Assert.Equal(2, cases.Count);
        Assert.Equal(["Failing", "Passing"], cases.Select(testCase => testCase.Attribute("name")!.Value).Order());
        var failing = Assert.Single(cases, testCase => testCase.Element("failure") is not null);
        Assert.Equal("Failing", failing.Attribute("name")!.Value);
        Assert.Contains("Step 2 (expectRecord)", failing.Element("failure")!.Attribute("message")!.Value);
        Assert.EndsWith("scenarios/Failing.json", failing.Attribute("file")!.Value);
        Assert.Contains("PASS Passing", output.ToString());
        Assert.Contains("FAIL Failing", output.ToString());
    }

    [Fact]
    public async Task A_folder_that_passes_exits_with_0()
    {
        using var folder = Application(Scenario("Passing", Clerk, """
            { "type": "createRecord", "entity": "Note", "as": "anna", "values": { "title": "Desk" } }
            """));
        using var results = new TemporaryFolder();

        var exitCode = await ScenarioCommand.RunAsync(
            [folder.Path, "--server", await PostgreSqlServer.AdminConnectionStringAsync(), "--results", results.Path], new StringWriter(), CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(results.Path, "scenarios-ScenarioApp.xml")));
    }

    [Fact]
    public async Task A_folder_that_does_not_compile_exits_with_2_and_writes_no_junit_file()
    {
        using var folder = Application(Scenario("Broken", Clerk, """
            { "type": "createRecord", "entity": "Missing", "as": "anna", "values": { "title": "Desk" } }
            """));
        using var results = new TemporaryFolder();
        var output = new StringWriter();

        var exitCode = await ScenarioCommand.RunAsync(
            [folder.Path, "--server", await PostgreSqlServer.AdminConnectionStringAsync(), "--results", results.Path], output, CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Empty(Directory.GetFiles(results.Path));
        Assert.Contains("does not compile", output.ToString());
    }

    [Fact]
    public async Task A_modules_folder_that_does_not_exist_exits_with_2_and_names_the_setting()
    {
        using var folder = Application(Scenario("Passing", Clerk, """
            { "type": "createRecord", "entity": "Note", "as": "anna", "values": { "title": "Desk" } }
            """));
        using var results = new TemporaryFolder();
        var output = new StringWriter();

        var exitCode = await ScenarioCommand.RunAsync(
            [folder.Path, "--server", await PostgreSqlServer.AdminConnectionStringAsync(), "--modules", Path.Combine(results.Path, "missing"), "--results", results.Path],
            output,
            CancellationToken);

        Assert.Equal(ScenarioCommand.SetupError, exitCode);
        Assert.Contains("Modules:Path", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(results.Path));
    }

    [Fact]
    public async Task Wrong_arguments_exit_with_2_and_print_the_usage()
    {
        var output = new StringWriter();

        var exitCode = await ScenarioCommand.RunAsync(["somewhere"], output, CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage:", output.ToString());
    }

    private const string Purchasing = """
        { "anna": ["employee"], "binh": ["department-manager"], "dung": ["department-manager"], "chau": ["finance-reviewer"] }
        """;

    private const string CreateRequest = """
        { "type": "createRecord", "entity": "PurchaseRequest", "as": "anna", "save": "request", "values": { "title": "Chairs", "manager": "binh", "amount": 5000 } },
        """;

    private const string StartApproval = """
        { "type": "startProcess", "process": "PurchaseRequestApproval", "record": "request", "as": "anna" },
        """;

    [Fact]
    public async Task A_request_below_the_threshold_is_approved_when_the_manager_completes_the_task()
    {
        using var folder = PurchaseApplication(Scenario("BelowThreshold", Purchasing, CreateRequest + StartApproval + """
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "waiting", "step": "managerApproval" },
            { "type": "expectTask", "record": "request", "step": "managerApproval", "state": "open" },
            { "type": "expectTask", "record": "request", "step": "managerApproval", "assignee": { "user": "binh" } },
            { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "binh" },
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "completed" },
            { "type": "expectTask", "record": "request", "step": "managerApproval", "state": "completed" },
            { "type": "expectRecord", "record": "request", "values": { "status": "approved" } }
            """));

        var result = await RunAsync(folder, "BelowThreshold");

        Assert.True(result.Passed, result.Failure);
    }

    [Fact]
    public async Task A_request_at_the_threshold_waits_for_the_finance_reviewer_after_the_manager()
    {
        using var folder = PurchaseApplication(Scenario("AtThreshold", Purchasing, """
            { "type": "createRecord", "entity": "PurchaseRequest", "as": "anna", "save": "request", "values": { "title": "Servers", "manager": "binh", "amount": 10000 } },
            """ + StartApproval + """
            { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "binh" },
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "waiting", "step": "financeApproval" },
            { "type": "expectTask", "record": "request", "step": "financeApproval", "assignee": { "role": "finance-reviewer" } },
            { "type": "completeTask", "record": "request", "step": "financeApproval", "outcome": "reject", "as": "chau" },
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "completed" },
            { "type": "expectRecord", "record": "request", "values": { "status": "rejected" } }
            """));

        var result = await RunAsync(folder, "AtThreshold");

        Assert.True(result.Passed, result.Failure);
    }

    [Fact]
    public async Task A_start_without_a_grant_a_completion_by_a_non_assignee_and_a_second_start_are_refused_as_expected()
    {
        using var folder = PurchaseApplication(Scenario("Refused", Purchasing, CreateRequest + """
            { "type": "startProcess", "process": "PurchaseRequestApproval", "record": "request", "as": "chau", "expect": "denied" },
            { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "binh", "expect": "notFound" },
            """ + StartApproval + """
            { "type": "startProcess", "process": "PurchaseRequestApproval", "record": "request", "as": "anna", "expect": "conflict" },
            { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "dung", "expect": "denied" },
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "waiting" },
            { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "binh" },
            { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "binh", "expect": "conflict" }
            """));

        var result = await RunAsync(folder, "Refused");

        Assert.True(result.Passed, result.Failure);
    }

    [Fact]
    public async Task A_completion_by_a_non_assignee_without_an_expectation_fails_with_the_denied_outcome()
    {
        using var folder = PurchaseApplication(Scenario("NotTheAssignee", Purchasing, CreateRequest + StartApproval + """
            { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "dung" }
            """));

        var result = await RunAsync(folder, "NotTheAssignee");

        Assert.False(result.Passed);
        Assert.StartsWith("Step 3 (completeTask): ", result.Failure);
        Assert.Contains("Denied", result.Failure);
    }

    [Fact]
    public async Task A_completion_writes_the_values_the_form_makes_editable_and_refuses_a_read_only_field()
    {
        using var written = PurchaseApplication(Scenario("EditableValue", Purchasing, CreateRequest + StartApproval + """
            { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "binh", "values": { "decisionComments": "ok" } },
            { "type": "expectRecord", "record": "request", "values": { "decisionComments": "ok", "status": "approved" } }
            """));
        using var refused = PurchaseApplication(Scenario("ReadOnlyValue", Purchasing, CreateRequest + StartApproval + """
            { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "binh", "values": { "amount": 5 }, "expect": { "invalid": "amount" } },
            { "type": "expectTask", "record": "request", "step": "managerApproval", "state": "open" },
            { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "binh", "values": { "amount": 5 } }
            """));

        var passed = await RunAsync(written, "EditableValue");
        var failed = await RunAsync(refused, "ReadOnlyValue");

        Assert.True(passed.Passed, passed.Failure);
        Assert.False(failed.Passed);
        Assert.StartsWith("Step 5 (completeTask): ", failed.Failure);
        Assert.Contains("/values/amount", failed.Failure);
    }

    [Fact]
    public async Task A_failed_task_or_instance_expectation_names_the_step_and_both_values()
    {
        using var task = PurchaseApplication(Scenario("WrongAssignee", Purchasing, CreateRequest + StartApproval + """
            { "type": "expectTask", "record": "request", "step": "managerApproval", "assignee": { "user": "dung" } }
            """));
        using var instance = PurchaseApplication(Scenario("WrongState", Purchasing, CreateRequest + StartApproval + """
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "completed" }
            """));
        using var noTask = PurchaseApplication(Scenario("NoTaskYet", Purchasing, CreateRequest + """
            { "type": "expectTask", "record": "request", "step": "financeApproval", "state": "open" }
            """));

        var wrongAssignee = await RunAsync(task, "WrongAssignee");
        var wrongState = await RunAsync(instance, "WrongState");
        var missing = await RunAsync(noTask, "NoTaskYet");

        Assert.Equal(
            "Step 3 (expectTask): task 'managerApproval' expected assignee user 'dung' but was user 'binh'", wrongAssignee.Failure);
        Assert.StartsWith("Step 3 (expectInstance): ", wrongState.Failure);
        Assert.Contains("expected state completed but was waiting", wrongState.Failure);
        Assert.Contains("managerApproval", wrongState.Failure);
        Assert.Equal("Step 2 (expectTask): no task of step 'financeApproval' exists for record 'request'", missing.Failure);
    }

    [Fact]
    public async Task The_scenario_time_decides_the_sequence_year_and_the_value_now_gives()
    {
        using var folder = PurchaseApplication(Scenario("FixedTime", Purchasing, CreateRequest + StartApproval + """
            { "type": "expectRecord", "record": "request", "values": { "number": "PR-2031-00001", "status": "submitted", "submittedAt": "2031-03-01T09:00:00Z" } }
            """, now: "2031-03-01T10:00:00+01:00"));

        var result = await RunAsync(folder, "FixedTime");

        Assert.True(result.Passed, result.Failure);
    }

    private const string ScheduleNow = "2026-10-01T09:00:00Z";

    [Fact]
    public async Task A_user_notified_in_app_and_by_email_passes_expectNotification_on_each_channel_and_with_no_channel()
    {
        using var folder = NotifyApplication(Scenario("NotifyUser", Purchasing, ApproveBelowThreshold + """
            { "type": "expectNotification", "recipient": { "user": "anna" }, "channel": "inApp", "values": { "title": "Chairs" } },
            { "type": "expectNotification", "recipient": { "user": "anna" }, "channel": "email", "values": { "title": "Chairs" } },
            { "type": "expectNotification", "recipient": { "user": "anna" }, "values": { "title": "Chairs" } },
            { "type": "expectNotification", "recipient": { "email": "ANNA@scenario.invalid" }, "channel": "email", "values": { "title": "Chairs" } },
            { "type": "expectNotification", "recipient": { "user": "anna" } }
            """));

        var result = await RunAsync(folder, "NotifyUser");

        Assert.True(result.Passed, result.Failure);
    }

    [Theory]
    [InlineData("inApp")]
    [InlineData("email")]
    public async Task A_different_expected_value_fails_expectNotification_naming_the_value_and_both_values(string channel)
    {
        using var folder = NotifyApplication(Scenario("NotifyMismatch", Purchasing, ApproveBelowThreshold + $$"""
            { "type": "expectNotification", "recipient": { "user": "anna" }, "channel": "{{channel}}", "values": { "title": "Desks" } }
            """));

        var result = await RunAsync(folder, "NotifyMismatch");

        Assert.False(result.Passed);
        Assert.Equal(3, result.Step);
        Assert.Equal("expectNotification", result.StepType);
        Assert.Contains("user 'anna'", result.Message);
        Assert.Contains("'title'", result.Message);
        Assert.Contains("\"Desks\"", result.Message);
        Assert.Contains("\"Chairs\"", result.Message);
    }

    [Fact]
    public async Task An_expected_value_the_notification_does_not_hold_fails_expectNotification_naming_the_value()
    {
        using var folder = NotifyApplication(Scenario("NotifyMissingValue", Purchasing, ApproveBelowThreshold + """
            { "type": "expectNotification", "recipient": { "user": "anna" }, "channel": "email", "values": { "amount": 5000 } }
            """));

        var result = await RunAsync(folder, "NotifyMissingValue");

        Assert.False(result.Passed);
        Assert.Contains("has no value 'amount'", result.Message);
    }

    [Theory]
    [InlineData("""{ "role": "finance-reviewer" }""", "inApp")]
    [InlineData("""{ "role": "employee" }""", "email")]
    [InlineData("""{ "user": "binh" }""", "inApp")]
    [InlineData("""{ "user": "binh" }""", "email")]
    [InlineData("""{ "email": "binh@scenario.invalid" }""", "email")]
    [InlineData("""{ "email": "anna@scenario.invalid" }""", "inApp")]
    public async Task A_recipient_that_was_not_notified_on_the_channel_fails_expectNotification_with_no_notification_found(string recipient, string channel)
    {
        using var folder = NotifyApplication(Scenario("NotifyNobody", Purchasing, ApproveBelowThreshold + $$"""
            { "type": "expectNotification", "recipient": {{recipient}}, "channel": "{{channel}}" }
            """));

        var result = await RunAsync(folder, "NotifyNobody");

        Assert.False(result.Passed);
        Assert.Equal("expectNotification", result.StepType);
        Assert.Contains($"no {channel} notification to", result.Message);
        Assert.Contains("was found", result.Message);
    }

    [Fact]
    public async Task An_email_still_passes_expectNotification_after_advance_time_runs_its_item_and_the_missing_mail_host_is_logged()
    {
        using var logs = new LogCollector();
        using var loggers = LoggerFactory.Create(builder => builder.AddProvider(logs));
        using var folder = NotifyApplication(Scenario("NotifyAfterTime", Purchasing, ApproveBelowThreshold + """
            { "type": "expectNotification", "recipient": { "user": "anna" }, "channel": "email", "values": { "title": "Chairs" } },
            { "type": "advanceTime", "by": "PT1H" },
            { "type": "expectNotification", "recipient": { "user": "anna" }, "channel": "email", "values": { "title": "Chairs" } }
            """, now: ScheduleNow));

        var result = await RunAsync(folder, "NotifyAfterTime", loggerFactory: loggers);

        Assert.True(result.Passed, result.Failure);
        Assert.Contains(logs.Entries, entry => entry.Contains("has no Email:Smtp:Host", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_daily_schedule_fires_once_after_advance_time_and_expect_instance_sees_it()
    {
        using var folder = ScheduleApplication(Scenario("DailySchedule", Purchasing, CreateRequest + """
            { "type": "expectRecord", "record": "request", "values": { "amount": 5000 } },
            { "type": "advanceTime", "by": "PT1H" },
            { "type": "expectRecord", "record": "request", "values": { "amount": 5000 } },
            { "type": "advanceTime", "by": "P1D" },
            { "type": "expectInstance", "process": "CountScheduleRuns", "record": "request", "state": "completed" },
            { "type": "expectRecord", "record": "request", "values": { "amount": 5001 } }
            """, now: ScheduleNow));

        var result = await RunAsync(folder, "DailySchedule");

        Assert.True(result.Passed, result.Failure);
    }

    [Fact]
    public async Task A_timer_waits_until_advance_time_passes_its_wait()
    {
        using var folder = TimerApplication(Scenario("CoolDown", Purchasing, CreateRequest + """
            { "type": "startProcess", "process": "CoolDown", "record": "request", "as": "anna" },
            { "type": "expectInstance", "process": "CoolDown", "record": "request", "state": "waiting" },
            { "type": "advanceTime", "by": "PT30M" },
            { "type": "expectInstance", "process": "CoolDown", "record": "request", "state": "waiting" },
            { "type": "advanceTime", "by": "PT1H" },
            { "type": "expectInstance", "process": "CoolDown", "record": "request", "state": "completed" },
            { "type": "expectRecord", "record": "request", "values": { "amount": 5001 } }
            """, now: ScheduleNow));

        var result = await RunAsync(folder, "CoolDown");

        Assert.True(result.Passed, result.Failure);
    }

    [Fact]
    public async Task A_task_deadline_moves_the_instance_on_only_after_advance_time_passes_it()
    {
        using var folder = DeadlineApplication(Scenario("TaskDeadline", Purchasing, CreateRequest + StartApproval + """
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "waiting", "step": "managerApproval" },
            { "type": "advanceTime", "by": "PT12H" },
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "waiting", "step": "managerApproval" },
            { "type": "advanceTime", "by": "P1D" },
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "waiting", "step": "financeApproval" }
            """, now: ScheduleNow));

        var result = await RunAsync(folder, "TaskDeadline");

        Assert.True(result.Passed, result.Failure);
    }

    [Fact]
    public async Task A_task_escalation_reassigns_the_task_after_advance_time_passes_it_and_the_deadline_comes_later()
    {
        using var folder = DeadlineApplication(Scenario("TaskEscalation", Purchasing, CreateRequest + StartApproval + """
            { "type": "expectTask", "record": "request", "step": "managerApproval", "assignee": { "user": "binh" } },
            { "type": "advanceTime", "by": "PT12H" },
            { "type": "expectTask", "record": "request", "step": "managerApproval", "assignee": { "role": "department-manager" } },
            { "type": "expectTask", "record": "request", "step": "managerApproval", "state": "open" },
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "waiting", "step": "managerApproval" },
            { "type": "advanceTime", "by": "P1D" },
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "waiting", "step": "financeApproval" }
            """, now: ScheduleNow));

        var result = await RunAsync(folder, "TaskEscalation");

        Assert.True(result.Passed, result.Failure);
    }

    [Fact]
    public async Task A_task_escalation_does_not_run_after_an_action()
    {
        using var folder = DeadlineApplication(Scenario("EarlyEscalation", Purchasing, CreateRequest + StartApproval + """
            { "type": "expectTask", "record": "request", "step": "managerApproval", "assignee": { "role": "department-manager" } }
            """, now: ScheduleNow));

        var result = await RunAsync(folder, "EarlyEscalation");

        Assert.False(result.Passed);
        Assert.Contains("Step 3 (expectTask)", result.Failure);
    }

    [Fact]
    public async Task A_task_deadline_does_not_run_after_an_action_and_a_completed_task_never_expires()
    {
        using var early = DeadlineApplication(Scenario("EarlyDeadline", Purchasing, CreateRequest + StartApproval + """
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "waiting", "step": "financeApproval" }
            """, now: ScheduleNow));
        using var completed = DeadlineApplication(Scenario("CompletedFirst", Purchasing, CreateRequest + StartApproval + """
            { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "binh" },
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "completed" },
            { "type": "advanceTime", "by": "P2D" },
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "completed" },
            { "type": "expectRecord", "record": "request", "values": { "status": "approved" } }
            """, now: ScheduleNow));

        var tooEarly = await RunAsync(early, "EarlyDeadline");
        var afterCompletion = await RunAsync(completed, "CompletedFirst");

        Assert.False(tooEarly.Passed);
        Assert.Contains("Step 3 (expectInstance)", tooEarly.Failure);
        Assert.True(afterCompletion.Passed, afterCompletion.Failure);
    }

    [Fact]
    public async Task No_schedule_instance_exists_before_advance_time()
    {
        using var folder = ScheduleApplication(Scenario("NoInstanceYet", Purchasing, CreateRequest + """
            { "type": "expectInstance", "process": "CountScheduleRuns", "record": "request", "state": "completed" }
            """, now: ScheduleNow));
        using var shortAdvance = ScheduleApplication(Scenario("ShortAdvance", Purchasing, CreateRequest + """
            { "type": "advanceTime", "by": "PT1H" },
            { "type": "expectInstance", "process": "CountScheduleRuns", "record": "request", "state": "completed" }
            """, now: ScheduleNow));

        var before = await RunAsync(folder, "NoInstanceYet");
        var after = await RunAsync(shortAdvance, "ShortAdvance");

        Assert.False(before.Passed);
        Assert.Contains("Step 2 (expectInstance)", before.Failure);
        Assert.False(after.Passed);
        Assert.Contains("Step 3 (expectInstance)", after.Failure);
    }

    [Fact]
    public async Task Three_days_in_one_step_fire_the_schedule_once_but_three_daily_steps_fire_it_three_times()
    {
        using var catchUp = ScheduleApplication(Scenario("CatchUp", Purchasing, CreateRequest + """
            { "type": "advanceTime", "by": "P3D" },
            { "type": "expectInstance", "process": "CountScheduleRuns", "record": "request", "state": "completed" },
            { "type": "expectRecord", "record": "request", "values": { "amount": 5001 } }
            """, now: ScheduleNow));
        using var daily = ScheduleApplication(Scenario("ThreeDays", Purchasing, CreateRequest + """
            { "type": "advanceTime", "by": "P1D" },
            { "type": "advanceTime", "by": "P1D" },
            { "type": "advanceTime", "by": "P1D" },
            { "type": "expectRecord", "record": "request", "values": { "amount": 5003 } }
            """, now: ScheduleNow));

        var caught = await RunAsync(catchUp, "CatchUp");
        var days = await RunAsync(daily, "ThreeDays");

        Assert.True(caught.Passed, caught.Failure);
        Assert.True(days.Passed, days.Failure);
    }

    [Fact]
    public async Task An_action_that_runs_more_work_items_than_the_limit_fails_at_that_step()
    {
        using var folder = PurchaseApplication(Scenario("TooManySteps", Purchasing, CreateRequest + StartApproval + """
            { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "binh" }
            """));

        var result = await RunAsync(folder, "TooManySteps", workItemLimit: 2);

        Assert.False(result.Passed);
        Assert.StartsWith("Step 3 (completeTask): ", result.Failure);
        Assert.Contains("more than 2 work items", result.Failure);
    }

    private const string ApproveBelowThreshold = CreateRequest + StartApproval + """
        { "type": "completeTask", "record": "request", "step": "managerApproval", "outcome": "approve", "as": "binh" },
        """;

    private const string OrderStub = """{ "connector": { "name": "PurchasingSystem", "operation": "createPurchaseOrder" }, "output": { "orderNumber": "PO-1001" } }""";

    private const string ReferenceStub = """{ "operation": "payment-references/creditorReference", "output": { "creditorReference": "RF00STUB" } }""";

    [Fact]
    public async Task An_async_event_trigger_starts_its_process_inside_the_scenario_and_expect_instance_sees_it()
    {
        using var folder = EventApplication(Scenario("EventDelivery", Purchasing, ApproveBelowThreshold + """
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "completed" },
            { "type": "expectInstance", "process": "ApprovalFollowUp", "record": "request", "state": "completed" },
            { "type": "expectRecord", "record": "request", "values": { "status": "approved", "decisionComments": "followed up" } }
            """));

        var result = await RunAsync(folder, "EventDelivery");

        Assert.True(result.Passed, result.Failure);
    }

    [Fact]
    public async Task Async_event_triggers_that_start_each_other_in_a_loop_fail_the_action_at_the_work_item_limit()
    {
        using var folder = EventApplication(Scenario("DeliveryLoop", Purchasing, ApproveBelowThreshold.TrimEnd().TrimEnd(',')));
        folder.With("events/follow-up-done.json", $$"""
            {
              "id": "{{Guid.NewGuid()}}",
              "kind": "event",
              "name": "FollowUpDone",
              "formatVersion": 1,
              "entity": "PurchaseRequest",
              "fields": [{ "name": "title", "type": "text" }]
            }
            """);
        folder.With("processes/approval-follow-up.json", $$"""
            {
              "id": "{{Guid.NewGuid()}}",
              "kind": "process",
              "name": "ApprovalFollowUp",
              "formatVersion": 1,
              "entity": "PurchaseRequest",
              "start": "announce",
              "steps": [
                { "name": "announce", "type": "operation", "operation": "raiseEvent", "event": "FollowUpDone", "fields": { "title": "title" }, "next": "done" },
                { "name": "done", "type": "end" }
              ]
            }
            """);
        folder.With("processes/follow-up-echo.json", $$"""
            {
              "id": "{{Guid.NewGuid()}}",
              "kind": "process",
              "name": "FollowUpEcho",
              "formatVersion": 1,
              "entity": "PurchaseRequest",
              "start": "announce",
              "steps": [
                { "name": "announce", "type": "operation", "operation": "raiseEvent", "event": "PurchaseRequestApproved", "fields": { "title": "title" }, "next": "done" },
                { "name": "done", "type": "end" }
              ]
            }
            """);
        folder.With("triggers/follow-up-done.json", $$"""
            {
              "id": "{{Guid.NewGuid()}}",
              "kind": "trigger",
              "name": "EchoFollowUp",
              "formatVersion": 1,
              "type": "event",
              "event": "FollowUpDone",
              "mode": "async",
              "process": "FollowUpEcho"
            }
            """);

        var result = await RunAsync(folder, "DeliveryLoop", workItemLimit: 20);

        Assert.False(result.Passed);
        Assert.StartsWith("Step 3 (completeTask): ", result.Failure);
        Assert.Contains("more than 20 work items", result.Failure);
    }

    [Fact]
    public async Task A_due_schedule_trigger_item_stays_queued_and_unclaimed_and_does_not_fail_the_work_loop()
    {
        var server = await PostgreSqlServer.AdminConnectionStringAsync();
        var database = $"axis_scenario_{Guid.NewGuid():N}";
        await ExecuteAsync(server, $"""CREATE DATABASE "{database}" """);
        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(server) { Database = database, Pooling = false }.ConnectionString;
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync(CancellationToken);
                await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options);
                await using var data = new DataDbContext(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(connection).Options);
                await using var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connection).Options);
                await configuration.Database.MigrateAsync(CancellationToken);
                await data.Database.MigrateAsync(CancellationToken);
                await processes.Database.MigrateAsync(CancellationToken);

                await using var insert = new NpgsqlCommand(
                    """
                    INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at, trigger_id)
                    VALUES (@id, @tenant, 'trigger.schedule', now() - interval '1 hour', @trigger)
                    """,
                    connection);
                insert.Parameters.AddWithValue("id", Guid.NewGuid());
                insert.Parameters.AddWithValue("tenant", ScenarioRunner.TenantId);
                insert.Parameters.AddWithValue("trigger", Guid.NewGuid());
                await insert.ExecuteNonQueryAsync(CancellationToken);
            }

            using (var work = new ScenarioWork(connectionString, NullLoggerFactory.Instance, 10, PackageCatalog.Empty, new ScenarioStubs([])))
            {
                await work.RunDueAsync(CancellationToken);
            }

            await using var check = new NpgsqlConnection(connectionString);
            await check.OpenAsync(CancellationToken);
            await using var query = new NpgsqlCommand("SELECT count(*) FROM axis.process_work_items WHERE kind = 'trigger.schedule' AND claim_token IS NULL", check);
            Assert.Equal(1L, await query.ExecuteScalarAsync(CancellationToken));
        }
        finally
        {
            await ExecuteAsync(server, $"""DROP DATABASE IF EXISTS "{database}" WITH (FORCE)""");
        }
    }

    [Fact]
    public async Task The_scenario_runner_claims_ready_work_items_in_queue_order_even_when_a_later_one_is_due_earlier()
    {
        var server = await PostgreSqlServer.AdminConnectionStringAsync();
        var database = $"axis_scenario_{Guid.NewGuid():N}";
        await ExecuteAsync(server, $"""CREATE DATABASE "{database}" """);
        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(server) { Database = database, Pooling = false }.ConnectionString;
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(CancellationToken);
            await using (var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options))
            await using (var data = new DataDbContext(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(connection).Options))
            await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connection).Options))
            {
                await configuration.Database.MigrateAsync(CancellationToken);
                await data.Database.MigrateAsync(CancellationToken);
                await processes.Database.MigrateAsync(CancellationToken);
            }

            // The item queued second is due earlier, as when the clock stepped back between the two.
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            foreach (var (id, age) in new[] { (first, "1 second"), (second, "2 seconds") })
            {
                await using var insert = new NpgsqlCommand(
                    $"INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at) VALUES (@id, @tenant, 'event.deliver', now() - interval '{age}')",
                    connection);
                insert.Parameters.AddWithValue("id", id);
                insert.Parameters.AddWithValue("tenant", ScenarioRunner.TenantId);
                await insert.ExecuteNonQueryAsync(CancellationToken);
            }

            string[] kinds = ["event.deliver"];
            var inQueueOrder = await WorkItemQueue.ClaimAsync(connection, ScenarioRunner.TenantId, kinds, TimeSpan.FromMinutes(5), CancellationToken, inQueueOrder: true);
            await using (var release = new NpgsqlCommand("UPDATE axis.process_work_items SET claim_token = NULL, lease_expires_at = NULL", connection))
            {
                await release.ExecuteNonQueryAsync(CancellationToken);
            }

            var earliestDue = await WorkItemQueue.ClaimAsync(connection, ScenarioRunner.TenantId, kinds, TimeSpan.FromMinutes(5), CancellationToken);

            Assert.Equal(first, inQueueOrder?.Id);
            Assert.Equal(second, earliestDue?.Id);
        }
        finally
        {
            await ExecuteAsync(server, $"""DROP DATABASE IF EXISTS "{database}" WITH (FORCE)""");
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    [Fact]
    public async Task A_connector_stub_answers_the_purchase_order_call_and_the_process_completes_with_its_order_number()
    {
        using var folder = CallApplication(Scenario("ConnectorStub", Purchasing, ApproveBelowThreshold + """
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "completed" },
            { "type": "expectRecord", "record": "request", "values": { "status": "approved", "purchaseOrderNumber": "PO-1001" } }
            """, stubs: $"{OrderStub}, {ReferenceStub}"));

        var result = await RunAsync(folder, "ConnectorStub", packages: _packages.Value);

        Assert.True(result.Passed, result.Failure);
    }

    [Fact]
    public async Task A_package_operation_stub_writes_a_value_the_real_package_cannot_produce_to_the_subject_field()
    {
        using var folder = CallApplication(Scenario("PackageStub", Purchasing, ApproveBelowThreshold + """
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "completed" },
            { "type": "expectRecord", "record": "request", "values": { "paymentReference": "RF00STUB" } }
            """, stubs: $"{OrderStub}, {ReferenceStub}"));

        var result = await RunAsync(folder, "PackageStub", packages: _packages.Value);

        Assert.True(result.Passed, result.Failure);
    }

    [Fact]
    public async Task A_connector_call_with_no_stub_fails_the_action_and_names_the_step_and_the_operation()
    {
        using var folder = CallApplication(Scenario("NoConnectorStub", Purchasing, ApproveBelowThreshold + """
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "completed" }
            """, stubs: ReferenceStub));

        var result = await RunAsync(folder, "NoConnectorStub", packages: _packages.Value);

        Assert.False(result.Passed);
        Assert.StartsWith("Step 3 (completeTask): ", result.Failure);
        Assert.Contains("'createPurchaseOrder'", result.Failure);
        Assert.Contains("PurchasingSystem/createPurchaseOrder", result.Failure);
        Assert.False(await DatabaseExistsAsync(result.Database));
    }

    [Fact]
    public async Task A_package_call_with_no_stub_fails_the_action_and_names_the_step_and_the_operation()
    {
        using var folder = CallApplication(Scenario("NoPackageStub", Purchasing, ApproveBelowThreshold + """
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "completed" }
            """, stubs: OrderStub));

        var result = await RunAsync(folder, "NoPackageStub", packages: _packages.Value);

        Assert.False(result.Passed);
        Assert.StartsWith("Step 3 (completeTask): ", result.Failure);
        Assert.Contains("'assignPaymentReference'", result.Failure);
        Assert.Contains("payment-references/creditorReference", result.Failure);
    }

    [Fact]
    public async Task A_connector_stub_with_outcome_failed_leaves_the_instance_failed_without_retries()
    {
        using var folder = CallApplication(Scenario("FailedConnector", Purchasing, ApproveBelowThreshold + """
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "failed" },
            { "type": "expectRecord", "record": "request", "values": { "status": "approved" } }
            """, stubs: """{ "connector": { "name": "PurchasingSystem", "operation": "createPurchaseOrder" }, "outcome": "failed" }"""));

        var result = await RunAsync(folder, "FailedConnector", packages: _packages.Value);

        Assert.True(result.Passed, result.Failure);
    }

    [Fact]
    public async Task A_package_stub_with_outcome_failed_leaves_the_instance_failed()
    {
        using var folder = CallApplication(Scenario("FailedPackage", Purchasing, ApproveBelowThreshold + """
            { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "failed" },
            { "type": "expectRecord", "record": "request", "values": { "purchaseOrderNumber": "PO-1001" } }
            """, stubs: $$"""{{OrderStub}}, { "operation": "payment-references/creditorReference", "outcome": "failed" }"""));

        var result = await RunAsync(folder, "FailedPackage", packages: _packages.Value);

        Assert.True(result.Passed, result.Failure);
    }

    public static TheoryData<string> SampleApplications()
    {
        var data = new TheoryData<string>();
        foreach (var manifest in Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "samples", "apps"), "application.json", SearchOption.AllDirectories))
        {
            data.Add(Path.GetFileName(Path.GetDirectoryName(manifest))!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SampleApplications))]
    public async Task The_scenarios_of_every_sample_application_pass(string application)
    {
        var root = RepositoryRoot();
        var output = new StringWriter();

        var exitCode = await ScenarioCommand.RunAsync(
            [
                Path.Combine(root, "samples", "apps", application),
                "--server", await PostgreSqlServer.AdminConnectionStringAsync(),
                "--extensions", SamplePackages.Path,
                "--modules", SampleModules.Path,
                "--results", Path.Combine(root, "artifacts", "test-results"),
            ],
            output,
            CancellationToken);

        Assert.True(exitCode == 0, output.ToString());
        var suites = new Dictionary<string, (string Name, string Tests)>
        {
            ["purchase-requests"] = ("PurchaseRequests", "5"),
            ["leave-requests"] = ("LeaveRequests", "5"),
            ["service-desk"] = ("ServiceDesk", "10"),
            ["asset-management"] = ("AssetManagement", "5"),
        };
        if (suites.TryGetValue(application, out var expected))
        {
            var suite = XDocument.Load(Path.Combine(root, "artifacts", "test-results", $"scenarios-{expected.Name}.xml")).Root!.Element("testsuite")!;
            Assert.Equal(expected.Tests, suite.Attribute("tests")!.Value);
            Assert.Equal("0", suite.Attribute("failures")!.Value);
        }
    }

    [Fact]
    public async Task A_raised_finance_threshold_fails_the_finance_scenario_at_the_step_that_no_longer_holds()
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            folder.With(Path.GetRelativePath(source, file), File.ReadAllText(file));
        }

        const string rule = "rules/finance-threshold.json";
        var raised = File.ReadAllText(Path.Combine(folder.Path, rule)).Replace("\"expression\": \"10000\"", "\"expression\": \"20000\"");
        Assert.Contains("20000", raised);
        folder.With(rule, raised);
        using var results = new TemporaryFolder();

        var exitCode = await ScenarioCommand.RunAsync(
            [folder.Path, "--server", await PostgreSqlServer.AdminConnectionStringAsync(), "--extensions", SamplePackages.Path, "--results", results.Path],
            new StringWriter(),
            CancellationToken);

        Assert.Equal(1, exitCode);
        var cases = XDocument.Load(Path.Combine(results.Path, "scenarios-PurchaseRequests.xml")).Descendants("testcase").ToList();
        var failing = Assert.Single(cases, testCase => testCase.Attribute("name")!.Value == "FinanceApprovalAboveThreshold");
        Assert.StartsWith("Step ", Assert.Single(failing.Elements("failure")).Attribute("message")!.Value);
    }

    [Fact]
    public async Task An_extensions_folder_that_does_not_exist_exits_with_2_and_writes_no_junit_file()
    {
        using var results = new TemporaryFolder();
        var output = new StringWriter();

        var exitCode = await ScenarioCommand.RunAsync(
            [
                Path.Combine(RepositoryRoot(), "samples", "apps", "purchase-requests"),
                "--server", await PostgreSqlServer.AdminConnectionStringAsync(),
                "--extensions", Path.Combine(results.Path, "missing"),
                "--results", results.Path,
            ],
            output,
            CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Empty(Directory.GetFileSystemEntries(results.Path));
        Assert.Contains("does not exist", output.ToString());
    }

    /// <summary>The repository folder, found by walking up from the test output to <c>Axis.slnx</c>.</summary>
    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Axis.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName
            ?? throw new InvalidOperationException($"Axis.slnx was not found in any folder above '{AppContext.BaseDirectory}'.");
    }

    private static async Task<ScenarioResult> RunAsync(
        TemporaryFolder folder,
        string name,
        int workItemLimit = ScenarioRunner.WorkItemLimit,
        PackageCatalog? packages = null,
        ILoggerFactory? loggerFactory = null)
    {
        var scenario = Axis.Configuration.Loading.ApplicationLoader.Load(folder.Path).Scenarios.Single(candidate => candidate.Name == name);
        return await ScenarioRunner.RunAsync(
            folder.Path, scenario, await PostgreSqlServer.AdminConnectionStringAsync(), loggerFactory ?? NullLoggerFactory.Instance, CancellationToken, workItemLimit, packages);
    }

    /// <summary>The <c>*.json</c> files of a folder as resources, with relative paths that use <c>/</c>.</summary>
    private static List<ResourceContent> ReadJsonFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories)
            .Select(file => new ResourceContent(Path.GetRelativePath(folder, file).Replace('\\', '/'), File.ReadAllText(file)))
            .ToList();

    private static async Task<bool> DatabaseExistsAsync(string name)
    {
        await using var connection = new NpgsqlConnection(await PostgreSqlServer.AdminConnectionStringAsync());
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", connection);
        command.Parameters.AddWithValue("name", name);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))! > 0;
    }

    /// <summary>A copy of the ScenarioApp fixture with the given scenario files added under <c>scenarios/</c>.</summary>
    private static TemporaryFolder Application(params (string Name, string Json)[] scenarios) =>
        Application("ScenarioApp", scenarios);

    private static TemporaryFolder Application((string Name, string Json) scenario) => Application([scenario]);

    /// <summary>A copy of the ScenarioPurchaseApp fixture, a purchase request with an approval process, with the scenario added.</summary>
    private static TemporaryFolder PurchaseApplication((string Name, string Json) scenario) => Application("ScenarioPurchaseApp", [scenario]);

    /// <summary>
    /// A copy of the ScenarioPurchaseApp fixture with the ScenarioCallApp files over it: after approval the process
    /// calls the purchasing system connector and the payment references package. Its scenarios need the sample package.
    /// </summary>
    private static TemporaryFolder CallApplication((string Name, string Json) scenario) =>
        Application("ScenarioPurchaseApp", [scenario], overlay: "ScenarioCallApp");

    /// <summary>
    /// A copy of the ScenarioPurchaseApp fixture with the ScenarioEventApp files over it: after approval the process raises
    /// an event, and an <c>async</c> trigger starts a follow-up process that writes the decision comments.
    /// </summary>
    private static TemporaryFolder EventApplication((string Name, string Json) scenario) =>
        Application("ScenarioPurchaseApp", [scenario], overlay: "ScenarioEventApp");

    /// <summary>
    /// A copy of the ScenarioPurchaseApp fixture with the ScenarioScheduleApp files over it: a daily schedule trigger at 18:00
    /// local time (11:00Z) starts a process on every purchase request that adds 1 to its amount.
    /// </summary>
    private static TemporaryFolder ScheduleApplication((string Name, string Json) scenario) =>
        Application("ScenarioPurchaseApp", [scenario], overlay: "ScenarioScheduleApp");

    /// <summary>
    /// A copy of the ScenarioPurchaseApp fixture with the ScenarioTimerApp files over it: the process <c>CoolDown</c> waits
    /// an hour on a timer, then adds 1 to the amount of the purchase request.
    /// </summary>
    private static TemporaryFolder TimerApplication((string Name, string Json) scenario) =>
        Application("ScenarioPurchaseApp", [scenario], overlay: "ScenarioTimerApp");

    /// <summary>
    /// A copy of the ScenarioPurchaseApp fixture with the ScenarioDeadlineApp files over it: the manager's task expires
    /// after one day and the process moves on to the finance approval.
    /// </summary>
    private static TemporaryFolder DeadlineApplication((string Name, string Json) scenario) =>
        Application("ScenarioPurchaseApp", [scenario], overlay: "ScenarioDeadlineApp");

    /// <summary>
    /// A copy of the ScenarioPurchaseApp fixture with the ScenarioNotifyApp files over it: after approval the process notifies
    /// the requester in-app and by email.
    /// </summary>
    private static TemporaryFolder NotifyApplication((string Name, string Json) scenario) =>
        Application("ScenarioPurchaseApp", [scenario], overlay: "ScenarioNotifyApp");

    private static TemporaryFolder Application(string fixture, (string Name, string Json)[] scenarios, string? overlay = null)
    {
        var folder = new TemporaryFolder();
        foreach (var name in overlay is null ? [fixture] : new[] { fixture, overlay })
        {
            var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                folder.With(relative, File.ReadAllText(file));
            }
        }

        foreach (var (name, json) in scenarios)
        {
            folder.With($"scenarios/{name}.json", json);
        }

        return folder;
    }

    private static (string Name, string Json) Scenario(string name, string users, string steps, string? now = null, string? stubs = null) => (name, $$"""
        {
          "id": "{{Guid.NewGuid()}}",
          "kind": "scenario",
          "name": "{{name}}",
          "formatVersion": 1,
          {{(now is null ? "" : $"\"now\": \"{now}\",")}}
          "users": {{users}},
          {{(stubs is null ? "" : $"\"stubs\": [{stubs}],")}}
          "steps": [{{steps}}]
        }
        """);
}
