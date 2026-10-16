using System.Text.RegularExpressions;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class ScenarioCompilerTests
{
    private const string ScenarioFile = "scenarios/check.json";
    private const string ScenarioId = "5f0d3a1c-52b2-4f0b-8b1e-9a516c0f7a1d";

    [Fact]
    public void The_scenario_in_the_configuration_docs_compiles_in_the_purchase_request_sample()
    {
        using var folder = SampleCopy().With("scenarios/finance-approval.json", DocsScenario());

        var result = ApplicationCompiler.Compile(folder.Path, SamplePackages.Versions);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void Every_problem_of_a_scenario_gets_its_own_code_at_its_path_in_one_compile()
    {
        using var folder = SampleCopy().With(ScenarioFile, Scenario("""
            "users": { "anna": ["employee", "ghost-role"], "binh": ["department-manager"] },
            "stubs": [
              { "connector": { "name": "Missing", "operation": "createPurchaseOrder" }, "output": {} },
              { "connector": { "name": "purchasingsystem", "operation": "nope" }, "output": {} },
              { "connector": { "name": "PurchasingSystem", "operation": "CREATEPURCHASEORDER" }, "output": { "orderNumber": "PO-1", "bogus": 1 } },
              { "operation": "payment-references/nothing", "output": {} },
              { "operation": "other-package/creditorReference", "output": {} },
              { "operation": "payment-references/CreditorReference", "output": { "creditorReference": "RF18", "extra": 1 } }
            ],
            "steps": [
              { "type": "createRecord", "entity": "PurchaseRequest", "as": "anna", "save": "request",
                "values": {
                  "title": "Laptops",
                  "nonexistent": 1,
                  "department": "not-a-uuid",
                  "currency": "GBP",
                  "lineItems": [{ "quantity": "three", "bogus": 1 }, 5]
                } },
              { "type": "createRecord", "entity": "Nope", "as": "anna", "save": "other", "values": {} },
              { "type": "createRecord", "entity": "LineItem", "as": "anna", "values": {} },
              { "type": "createRecord", "entity": "Department", "as": "anna", "save": "department", "values": {} },
              { "type": "createRecord", "entity": "PurchaseRequest", "as": "anna", "save": "request", "values": {} },
              { "type": "startProcess", "process": "Unknown", "record": "request", "as": "anna" },
              { "type": "startProcess", "process": "PurchaseRequestApproval", "record": "missing", "as": "nobody" },
              { "type": "startProcess", "process": "PurchaseRequestApproval", "record": "department", "as": "anna" },
              { "type": "completeTask", "record": "request", "step": "nothing", "outcome": "approve", "as": "binh" },
              { "type": "completeTask", "record": "request", "step": "MANAGERAPPROVAL", "outcome": "nope", "as": "binh" },
              { "type": "expectTask", "record": "request", "step": "managerApproval", "assignee": { "role": "ghost-role" } },
              { "type": "expectTask", "record": "request", "step": "managerApproval", "assignee": { "user": "zed" } },
              { "type": "expectInstance", "process": "PurchaseRequestApproval", "record": "request", "state": "running", "step": "nowhere" }
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path, SamplePackages.Versions);

        Assert.Null(result.Model);
        Assert.Equal(
            new[]
            {
                ("AXC0180", "/users/anna/1"),
                ("AXC0190", "/stubs/0/connector/name"),
                ("AXC0190", "/stubs/1/connector/operation"),
                ("AXC0191", "/stubs/2/output/bogus"),
                ("AXC0190", "/stubs/3/operation"),
                ("AXC0190", "/stubs/4/operation"),
                ("AXC0191", "/stubs/5/output/extra"),
                ("AXC0183", "/steps/0/values/nonexistent"),
                ("AXC0189", "/steps/0/values/department"),
                ("AXC0189", "/steps/0/values/currency"),
                ("AXC0189", "/steps/0/values/lineItems/0/quantity"),
                ("AXC0183", "/steps/0/values/lineItems/0/bogus"),
                ("AXC0189", "/steps/0/values/lineItems/1"),
                ("AXC0182", "/steps/1/entity"),
                ("AXC0182", "/steps/2/entity"),
                ("AXC0188", "/steps/4/save"),
                ("AXC0184", "/steps/5/process"),
                ("AXC0187", "/steps/6/record"),
                ("AXC0181", "/steps/6/as"),
                ("AXC0184", "/steps/7/process"),
                ("AXC0185", "/steps/8/step"),
                ("AXC0186", "/steps/9/outcome"),
                ("AXC0180", "/steps/10/assignee/role"),
                ("AXC0181", "/steps/11/assignee/user"),
                ("AXC0185", "/steps/12/step"),
            }.Order().ToArray(),
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)).Order().ToArray());
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((ScenarioFile, Guid.Parse(ScenarioId)), (diagnostic.File, diagnostic.ResourceId)));
        Assert.Equal(
            Enumerable.Range(180, 12).Select(code => $"AXC0{code}").ToArray(),
            result.Diagnostics.Select(diagnostic => diagnostic.Code).Distinct().Order().ToArray());
    }

    [Theory]
    [InlineData("""{ "type": "teleport", "record": "request" }""")]
    [InlineData("""{ "type": "createRecord", "entity": "Department", "as": "anna", "save": "dept", "values": {}, "expect": "denied" }""")]
    [InlineData("""{ "type": "expectRecord", "record": "request", "values": {}, "expect": "denied" }""")]
    [InlineData("""{ "type": "deleteRecord", "record": "request" }""")]
    [InlineData("""{ "type": "expectTask", "record": "request", "step": "managerApproval" }""")]
    public void A_malformed_step_is_a_schema_violation(string step)
    {
        using var folder = SampleCopy().With(ScenarioFile, Scenario($$"""
            "users": { "anna": ["employee"] },
            "steps": [{{step}}]
            """));

        var result = ApplicationCompiler.Compile(folder.Path, SamplePackages.Versions);

        Assert.Contains(result.Diagnostics, diagnostic => (diagnostic.Code, diagnostic.File) == (DiagnosticCodes.SchemaViolation, ScenarioFile));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_stub_with_outcome_failed_and_no_output_compiles()
    {
        using var folder = SampleCopy().With(ScenarioFile, Scenario("""
            "users": { "anna": ["employee"] },
            "stubs": [
              { "connector": { "name": "PurchasingSystem", "operation": "createPurchaseOrder" }, "outcome": "failed" },
              { "operation": "payment-references/creditorReference", "outcome": "failed" }
            ],
            "steps": [{ "type": "createRecord", "entity": "PurchaseRequest", "as": "anna", "values": {} }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path, SamplePackages.Versions);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Theory]
    [InlineData("""{ "connector": { "name": "PurchasingSystem", "operation": "createPurchaseOrder" }, "output": {}, "outcome": "failed" }""")]
    [InlineData("""{ "connector": { "name": "PurchasingSystem", "operation": "createPurchaseOrder" } }""")]
    [InlineData("""{ "connector": { "name": "PurchasingSystem", "operation": "createPurchaseOrder" }, "outcome": "succeeded" }""")]
    public void A_stub_needs_exactly_one_of_output_and_a_failed_outcome(string stub)
    {
        using var folder = SampleCopy().With(ScenarioFile, Scenario($$"""
            "users": { "anna": ["employee"] },
            "stubs": [{{stub}}],
            "steps": [{ "type": "createRecord", "entity": "PurchaseRequest", "as": "anna", "values": {} }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path, SamplePackages.Versions);

        Assert.Contains(result.Diagnostics, diagnostic => (diagnostic.Code, diagnostic.File) == (DiagnosticCodes.SchemaViolation, ScenarioFile));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("2026-99-99T99:99:99Z")]
    [InlineData("2026-02-30T09:00:00Z")]
    [InlineData("2026-02-29T09:00:00Z")]
    [InlineData("2026-10-01T24:00:00Z")]
    [InlineData("2026-10-01T09:60:00Z")]
    [InlineData("2026-10-01T09:00:00+25:00")]
    [InlineData("2026-10-01T09:00:00")]
    public void A_now_that_is_not_a_real_timestamp_is_a_schema_violation(string now)
    {
        using var folder = SampleCopy().With(ScenarioFile, Scenario($$"""
            "now": "{{now}}",
            "users": { "anna": ["employee"] },
            "steps": [{ "type": "createRecord", "entity": "PurchaseRequest", "as": "anna", "values": {} }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path, SamplePackages.Versions);

        Assert.Contains(result.Diagnostics, diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path) == (DiagnosticCodes.SchemaViolation, ScenarioFile, "/now"));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("2026-10-01T09:00:00Z")]
    [InlineData("2028-02-29T23:59:59.123+07:00")]
    public void A_now_that_is_a_real_timestamp_compiles(string now)
    {
        using var folder = SampleCopy().With(ScenarioFile, Scenario($$"""
            "now": "{{now}}",
            "users": { "anna": ["employee"] },
            "steps": [{ "type": "createRecord", "entity": "PurchaseRequest", "as": "anna", "values": {} }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path, SamplePackages.Versions);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void A_path_escapes_a_slash_and_a_tilde_in_a_user_id_a_value_key_and_an_output_key()
    {
        using var folder = SampleCopy().With(ScenarioFile, Scenario("""
            "users": { "a/b": ["ghost-role"], "c~d": ["employee"] },
            "stubs": [
              { "connector": { "name": "PurchasingSystem", "operation": "createPurchaseOrder" }, "output": { "x/y~z": 1 } }
            ],
            "steps": [
              { "type": "createRecord", "entity": "PurchaseRequest", "as": "c~d", "save": "request", "values": { "p/q~r": 1 } }
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path, SamplePackages.Versions);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownScenarioField, "/steps/0/values/p~1q~0r"),
                (DiagnosticCodes.UnknownStubOutput, "/stubs/0/output/x~1y~0z"),
                (DiagnosticCodes.UnknownScenarioRole, "/users/a~1b/0"),
            ],
            result.Diagnostics.Where(diagnostic => diagnostic.File == ScenarioFile).Select(diagnostic => (diagnostic.Code, diagnostic.Path)).ToArray());
    }

    [Fact]
    public void Adding_or_changing_a_scenario_keeps_the_content_hash_and_the_resources()
    {
        using var folder = SampleCopy();
        var before = ApplicationCompiler.Compile(folder.Path, SamplePackages.Versions);
        Assert.Empty(before.Diagnostics);

        folder.With(ScenarioFile, ValidScenario("Laptops"));
        var added = ApplicationCompiler.Compile(folder.Path, SamplePackages.Versions);
        folder.With(ScenarioFile, ValidScenario("Monitors"));
        var changed = ApplicationCompiler.Compile(folder.Path, SamplePackages.Versions);

        Assert.Empty(added.Diagnostics);
        Assert.Empty(changed.Diagnostics);
        Assert.NotNull(before.ContentHash);
        Assert.Equal(before.ContentHash, added.ContentHash);
        Assert.Equal(before.ContentHash, changed.ContentHash);
        Assert.Equal(before.Resources!.Select(resource => resource.Path), changed.Resources!.Select(resource => resource.Path));
        Assert.DoesNotContain(changed.Resources!, resource => resource.Path.StartsWith("scenarios/", StringComparison.Ordinal));
    }

    [Fact]
    public void Valid_values_of_every_type_compile_and_null_computed_and_read_only_fields_are_allowed()
    {
        using var folder = SampleCopy().With(ScenarioFile, Scenario("""
            "users": { "anna": ["employee"] },
            "steps": [
              { "type": "createRecord", "entity": "PurchaseRequest", "as": "anna", "save": "request",
                "values": {
                  "TITLE": "Laptops",
                  "department": "15d5bba1-eaf1-431b-9001-e1e3ade50979",
                  "supplier": null,
                  "currency": "EUR",
                  "submittedAt": "2026-10-01T09:00:00Z",
                  "totalAmount": 5,
                  "number": "PR-1",
                  "lineItems": [{ "description": "Laptop", "quantity": 3, "unitPrice": 12.5 }]
                } },
              { "type": "expectRecord", "record": "request", "values": { "status": "draft", "lineItems": null } }
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path, SamplePackages.Versions);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void A_name_whose_file_did_not_load_is_not_reported_again()
    {
        using var folder = SampleCopy()
            .With("entities/broken.json", """{ "id": "0a0a0a0a-0a0a-4a0a-8a0a-0a0a0a0a0a0a", "kind": "entity", "name": "Broken", "formatVersion": 1 }""")
            .With("roles/broken.json", """{ "id": "0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b", "kind": "role", "name": "broken-role", "formatVersion": 1 }""")
            .With(ScenarioFile, Scenario("""
                "users": { "anna": ["broken-role"] },
                "steps": [{ "type": "createRecord", "entity": "Broken", "as": "anna", "values": {} }]
                """));

        var result = ApplicationCompiler.Compile(folder.Path, SamplePackages.Versions);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.File == ScenarioFile);
        Assert.NotEmpty(result.Diagnostics);
    }

    private static TemporaryFolder SampleCopy()
    {
        var folder = new TemporaryFolder();
        var source = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            folder.With(Path.GetRelativePath(source, file), File.ReadAllText(file));
        }

        return folder;
    }

    /// <summary>The first JSON block after the "Test scenarios" heading of the configuration reference.</summary>
    private static string DocsScenario()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Docs", "reference", "configuration.md"));
        var heading = Regex.Match(text, @"^## Test scenarios\s*$", RegexOptions.Multiline);
        Assert.True(heading.Success, "The configuration reference has no 'Test scenarios' heading.");
        var block = Regex.Match(text[heading.Index..], @"```json\r?\n(.*?)\r?\n```", RegexOptions.Singleline);
        Assert.True(block.Success, "The 'Test scenarios' section has no JSON block.");
        return block.Groups[1].Value;
    }

    private static string ValidScenario(string title) => Scenario($$"""
        "users": { "anna": ["employee"] },
        "steps": [
          { "type": "createRecord", "entity": "PurchaseRequest", "as": "anna", "save": "request", "values": { "title": "{{title}}" } }
        ]
        """);

    private static string Scenario(string body) => $$"""
        { "id": "{{ScenarioId}}", "kind": "scenario", "name": "Check", "formatVersion": 1,
          {{body}} }
        """;
}
