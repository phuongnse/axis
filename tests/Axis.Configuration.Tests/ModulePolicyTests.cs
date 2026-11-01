using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Releases;

namespace Axis.Configuration.Tests;

public sealed class ModulePolicyTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01";
    private const string ApprovalId = "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c21";
    private const string ApprovalsDataSourceId = "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c22";
    private const string PolicyId = "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c23";
    private const string ApproveProcessId = "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c24";
    private const string PolicyFile = "modules/approvals/policies/ApproverAccess.json";

    private const string DefaultGrants = """
        [ { "entity": "Approval", "actions": ["read", "create"], "filter": "title != 'secret'" },
          { "dataSource": "Approvals", "actions": ["read"] } ]
        """;

    [Fact]
    public void A_module_policy_joins_the_model_for_the_role_its_slot_maps_to()
    {
        using var modules = Modules(["approver"], DefaultGrants);
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);
        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(compiled.Diagnostics);
        Assert.Empty(rebuilt.Diagnostics);
        foreach (var model in new[] { compiled.Model!, rebuilt.Model! })
        {
            var policy = Assert.Single(model.Policies);
            Assert.Equal("approvals/ApproverAccess", policy.Name);
            Assert.Equal(["manager"], policy.Roles);
            Assert.Equal(ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(PolicyId)), policy.Id);
            Assert.NotEqual(Guid.Parse(PolicyId), policy.Id);
            Assert.Equal(PolicyFile, policy.File);

            var entity = Assert.Single(model.Entities, candidate => candidate.Name == "approvals/Approval");
            var dataSource = Assert.Single(model.DataSources, candidate => candidate.Name == "approvals/Approvals");
            var entityGrant = policy.Grants[0];
            Assert.Equal(new EntityReference(entity.Id, "approvals/Approval"), entityGrant.Entity);
            Assert.NotNull(entityGrant.Filter);
            Assert.Equal(new DataSourceReference(dataSource.Id, "approvals/Approvals"), policy.Grants[1].DataSource);
        }
    }

    [Fact]
    public void A_module_policy_grants_on_a_process_of_its_own_module()
    {
        using var modules = Modules(["approver"], """[{ "process": "Approve", "actions": ["start", "inspect"] }]""");
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);
        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(compiled.Diagnostics);
        Assert.Empty(rebuilt.Diagnostics);
        foreach (var model in new[] { compiled.Model!, rebuilt.Model! })
        {
            var policy = Assert.Single(model.Policies);
            Assert.Equal(["manager"], policy.Roles);

            var process = Assert.Single(model.Processes, candidate => candidate.Name == "approvals/Approve");
            var grant = Assert.Single(policy.Grants);
            Assert.Equal(new ProcessReference(process.Id, "approvals/Approve"), grant.Process);
        }
    }

    [Fact]
    public void Each_application_maps_the_slot_to_its_own_role()
    {
        using var modules = Modules(["approver"], DefaultGrants);
        using var folder = Application(mapping: """{ "approver": "reviewer" }""");

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(["reviewer"], Assert.Single(compiled.Model!.Policies).Roles);
    }

    [Fact]
    public void A_policy_that_names_a_role_instead_of_a_slot_is_reported_in_the_module_file()
    {
        using var modules = Modules(["approver", "manager"], DefaultGrants);
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModulePolicyRoleNotSlot, PolicyFile, "/roles/1", Guid.Parse(PolicyId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'manager'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'approver'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_policy_of_a_module_without_slots_that_names_a_role_says_there_are_none()
    {
        using var modules = Modules(["approver"], DefaultGrants, slots: false);
        using var folder = Application(mapping: null);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModulePolicyRoleNotSlot, PolicyFile, "/roles/0"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("no role slots", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""[{ "entity": "Invoice", "actions": ["read"] }]""", "/grants/0/entity", "Invoice")]
    [InlineData("""[{ "entity": "departments/Department", "actions": ["read"] }]""", "/grants/0/entity", "departments/Department")]
    [InlineData("""[{ "dataSource": "Invoices", "actions": ["read"] }]""", "/grants/0/dataSource", "Invoices")]
    [InlineData("""[{ "dataSource": "departments/Departments", "actions": ["read"] }]""", "/grants/0/dataSource", "departments/Departments")]
    [InlineData("""[{ "entity": "Approval", "actions": ["read"] }, { "process": "departments/Approve", "actions": ["start"] }]""", "/grants/1/process", "departments/Approve")]
    [InlineData("""[{ "process": "Archive", "actions": ["start"] }]""", "/grants/0/process", "Archive")]
    [InlineData("""[{ "endpoint": "Intake", "actions": ["invoke"] }]""", "/grants/0/endpoint", "")]
    public void A_grant_outside_the_module_is_reported_in_the_module_file(string grants, string path, string named)
    {
        using var modules = Modules(["approver"], grants);
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleGrantOutsideModule, PolicyFile, path, Guid.Parse(PolicyId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains(named, diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
    }

    [Theory]
    [InlineData("""[{ "entity": "Missing", "actions": ["read"] }]""", "/grants/0/entity")]
    [InlineData("""[{ "dataSource": "Missing", "actions": ["read"] }]""", "/grants/0/dataSource")]
    [InlineData("""[{ "process": "Missing", "actions": ["start"] }]""", "/grants/0/process")]
    public void A_plain_name_that_is_nothing_anywhere_keeps_the_unknown_target_code(string grants, string path)
    {
        using var modules = Modules(["approver"], grants);
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownGrantTarget, PolicyFile, path),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Missing'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_filter_cannot_call_an_application_rule()
    {
        using var modules = Modules(["approver"], """[{ "entity": "Approval", "actions": ["read"], "filter": "IsSecret(title)" }]""");
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(PolicyFile, diagnostic.File);
        Assert.Equal("/grants/0/filter", diagnostic.Path);
    }

    [Fact]
    public void The_matrix_row_of_the_mapped_role_holds_the_module_grants_read_only()
    {
        using var modules = Modules(["approver"], DefaultGrants);
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var matrix = compiled.PolicyMatrix;
        var manager = Assert.Single(matrix.Roles, role => role.Name == "manager");
        var cell = Assert.Single(manager.Cells, candidate => candidate is { Kind: "entity", Target: "approvals/Approval" });
        Assert.Equal(["read", "create"], cell.Actions);
        var grant = Assert.Single(cell.Grants);
        Assert.Equal(
            ("approvals/ApproverAccess", 0, true, "approvals"),
            (grant.Policy, grant.Index, grant.Shared, grant.Module));
        Assert.Equal("title != 'secret'", grant.Filter);
        var dataSourceCell = Assert.Single(manager.Cells, candidate => candidate is { Kind: "dataSource", Target: "approvals/Approvals" });
        Assert.Equal(["read"], dataSourceCell.Actions);
        Assert.Equal("approvals", Assert.Single(dataSourceCell.Grants).Module);

        var reviewer = Assert.Single(matrix.Roles, role => role.Name == "reviewer");
        Assert.All(reviewer.Cells, candidate => Assert.Empty(candidate.Grants));
    }

    [Fact]
    public void A_matrix_grant_of_an_application_policy_has_no_module()
    {
        using var modules = Modules(["approver"], DefaultGrants);
        using var folder = Application().With("policies/InvoiceAccess.json", """
            { "id": "44444444-4444-4444-8444-444444444499", "kind": "policy", "name": "InvoiceAccess", "formatVersion": 1,
              "roles": ["reviewer"], "grants": [ { "entity": "Invoice", "actions": ["read"] } ] }
            """);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var reviewer = Assert.Single(compiled.PolicyMatrix.Roles, role => role.Name == "reviewer");
        var grant = Assert.Single(Assert.Single(reviewer.Cells, cell => cell.Target == "Invoice").Grants);
        Assert.Null(grant.Module);
    }

    private static TemporaryFolder Modules(string[] roles, string grants, bool slots = true)
    {
        var slotList = slots ? """, "roles": [ { "name": "approver", "label": { "textKey": "approvals.approver" } } ]""" : "";
        return new TemporaryFolder()
            .With("departments/1.0.0/module.json", """{ "kind": "module", "id": "departments", "version": "1.0.0", "label": "Departments" }""")
            .With("departments/1.0.0/Department.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c31", "kind": "entity", "name": "Department", "formatVersion": 1,
                  "fields": [ { "name": "name", "type": "text" } ] }
                """)
            .With("departments/1.0.0/Departments.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c32", "kind": "dataSource", "name": "Departments", "formatVersion": 1,
                  "entity": "Department", "fields": [ { "name": "name", "path": "name" } ] }
                """)
            .With("departments/1.0.0/TextsEn.json", """
                { "id": "d9f3a5b7-4c6e-4a81-8d1c-2e3f4a5b6c7d", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "departments.department": "Department" } }
                """)
            .With("approvals/1.0.0/module.json", $$"""
                { "kind": "module", "id": "approvals", "version": "1.0.0", "label": "Approvals",
                  "modules": [ { "id": "departments", "version": "^1.0.0" } ]{{slotList}} }
                """)
            .With("approvals/1.0.0/Approval.json", $$"""
                { "id": "{{ApprovalId}}", "kind": "entity", "name": "Approval", "formatVersion": 1,
                  "fields": [ { "name": "title", "type": "text" } ] }
                """)
            .With("approvals/1.0.0/Approvals.json", $$"""
                { "id": "{{ApprovalsDataSourceId}}", "kind": "dataSource", "name": "Approvals", "formatVersion": 1,
                  "entity": "Approval", "fields": [ { "name": "title", "path": "title" } ] }
                """)
            .With("approvals/1.0.0/Approve.json", $$"""
                { "id": "{{ApproveProcessId}}", "kind": "process", "name": "Approve", "formatVersion": 1,
                  "entity": "Approval", "start": "done", "steps": [ { "name": "done", "type": "end" } ] }
                """)
            .With("approvals/1.0.0/policies/ApproverAccess.json", $$"""
                { "id": "{{PolicyId}}", "kind": "policy", "name": "ApproverAccess", "formatVersion": 1,
                  "roles": [{{string.Join(", ", roles.Select(role => $"\"{role}\""))}}], "grants": {{grants}} }
                """)
            .With("approvals/1.0.0/TextsEn.json", """
                { "id": "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b6c", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "approvals.approver": "Approver" } }
                """);
    }

    private static TemporaryFolder Application(string? mapping = """{ "approver": "manager" }""")
    {
        var roles = mapping is null ? "" : $$""", "roles": {{mapping}}""";
        return new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1,
                  "modules": [{ "id": "approvals", "version": "1.0.0"{{roles}} }] }
                """)
            .With("texts/en.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "role.manager": "Manager", "role.reviewer": "Reviewer" } }
                """)
            .With("roles/manager.json", """
                { "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "manager", "formatVersion": 1, "label": { "textKey": "role.manager" } }
                """)
            .With("roles/reviewer.json", """
                { "id": "44444444-4444-4444-8444-444444444402", "kind": "role", "name": "reviewer", "formatVersion": 1, "label": { "textKey": "role.reviewer" } }
                """)
            .With("entities/Invoice.json", """
                { "id": "44444444-4444-4444-8444-444444444411", "kind": "entity", "name": "Invoice", "formatVersion": 1,
                  "fields": [ { "name": "number", "type": "text" } ] }
                """)
            .With("dataSources/Invoices.json", """
                { "id": "44444444-4444-4444-8444-444444444412", "kind": "dataSource", "name": "Invoices", "formatVersion": 1,
                  "entity": "Invoice", "fields": [ { "name": "number", "path": "number" } ] }
                """)
            .With("processes/Archive.json", """
                { "id": "44444444-4444-4444-8444-444444444414", "kind": "process", "name": "Archive", "formatVersion": 1,
                  "entity": "Invoice", "start": "done", "steps": [ { "name": "done", "type": "end" } ] }
                """)
            .With("rules/IsSecret.json", """
                { "id": "44444444-4444-4444-8444-444444444413", "kind": "rule", "name": "IsSecret", "formatVersion": 1,
                  "parameters": [ { "name": "value", "type": "text" } ], "resultType": "boolean", "expression": "value == 'secret'" }
                """);
    }
}
