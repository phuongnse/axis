using System.Xml.Linq;
using Axis.Configuration.Tests;
using Axis.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>The scenario runner against a real PostgreSQL server, one temporary database per scenario.</summary>
public sealed class ScenarioRunnerTests
{
    private const string Clerk = """{ "anna": ["clerk"] }""";

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
    public async Task A_step_kind_that_is_not_supported_yet_fails_the_scenario()
    {
        using var folder = Application(Scenario("Unsupported", Clerk, """
            { "type": "createRecord", "entity": "Note", "as": "anna", "values": { "title": "Desk" }, "expect": "denied" }
            """));

        var result = await RunAsync(folder, "Unsupported");

        Assert.Equal("Step 1 (createRecord): expect is not supported yet", result.Failure);
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
    public async Task Wrong_arguments_exit_with_2_and_print_the_usage()
    {
        var output = new StringWriter();

        var exitCode = await ScenarioCommand.RunAsync(["somewhere"], output, CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage:", output.ToString());
    }

    private static async Task<ScenarioResult> RunAsync(TemporaryFolder folder, string name)
    {
        var scenario = Axis.Configuration.Loading.ApplicationLoader.Load(folder.Path).Scenarios.Single(candidate => candidate.Name == name);
        return await ScenarioRunner.RunAsync(
            folder.Path, scenario, await PostgreSqlServer.AdminConnectionStringAsync(), NullLoggerFactory.Instance, CancellationToken);
    }

    private static async Task<bool> DatabaseExistsAsync(string name)
    {
        await using var connection = new NpgsqlConnection(await PostgreSqlServer.AdminConnectionStringAsync());
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", connection);
        command.Parameters.AddWithValue("name", name);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))! > 0;
    }

    /// <summary>A copy of the ScenarioApp fixture with the given scenario files added under <c>scenarios/</c>.</summary>
    private static TemporaryFolder Application(params (string Name, string Json)[] scenarios)
    {
        var folder = new TemporaryFolder();
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ScenarioApp");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            folder.With(relative, File.ReadAllText(file));
        }

        foreach (var (name, json) in scenarios)
        {
            folder.With($"scenarios/{name}.json", json);
        }

        return folder;
    }

    private static TemporaryFolder Application((string Name, string Json) scenario) => Application([scenario]);

    private static (string Name, string Json) Scenario(string name, string users, string steps) => (name, $$"""
        {
          "id": "{{Guid.NewGuid()}}",
          "kind": "scenario",
          "name": "{{name}}",
          "formatVersion": 1,
          "users": {{users}},
          "steps": [{{steps}}]
        }
        """);
}
