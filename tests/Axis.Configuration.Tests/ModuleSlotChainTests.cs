using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;

namespace Axis.Configuration.Tests;

public sealed class ModuleSlotChainTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01";
    private const string ItemId = "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c41";
    private const string PolicyId = "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c42";

    private const string OrdersOperation = """
        { "name": "CreateOrder", "method": "POST", "path": "/orders",
          "input": [ { "name": "code", "type": "text", "required": true }, { "name": "amount", "type": "decimal" } ],
          "output": [ { "name": "orderId", "type": "text" } ] }
        """;

    [Fact]
    public void A_role_slot_of_a_dependency_becomes_the_application_role_the_chain_reaches()
    {
        using var modules = RoleModules();
        using var folder = Application(Entry("b", roles: """{ "approver": "lead" }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);
        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(compiled.Diagnostics);
        Assert.Empty(rebuilt.Diagnostics);
        foreach (var model in new[] { compiled.Model!, rebuilt.Model! })
        {
            Assert.Equal(["b", "a"], model.Modules.Select(pin => pin.Id));
            Assert.Equal(new Dictionary<string, string> { ["approver"] = "lead" }, model.Modules[0].Roles);
            Assert.Equal(new Dictionary<string, string> { ["manager"] = "lead" }, model.Modules[1].Roles);
        }
    }

    [Fact]
    public void A_policy_of_a_dependency_only_module_grants_the_role_the_chain_reaches()
    {
        using var modules = RoleModules()
            .With("a/1.0.0/Item.json", $$"""
                { "id": "{{ItemId}}", "kind": "entity", "name": "Item", "formatVersion": 1,
                  "fields": [ { "name": "title", "type": "text" } ] }
                """)
            .With("a/1.0.0/policies/Access.json", $$"""
                { "id": "{{PolicyId}}", "kind": "policy", "name": "Access", "formatVersion": 1,
                  "roles": ["manager"], "grants": [ { "entity": "Item", "actions": ["read"] } ] }
                """);
        using var folder = Application(Entry("b", roles: """{ "approver": "other" }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var policy = Assert.Single(compiled.Model!.Policies);
        Assert.Equal("a/Access", policy.Name);
        Assert.Equal(["other"], policy.Roles);
    }

    [Fact]
    public void An_unmapped_slot_of_the_dependency_is_reported_in_the_manifest_of_the_depending_module()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "a", RoleSlots("a", "manager", "auditor"));
        Install(modules, "b", RoleSlots("b", "approver") + Depends("a", roles: """{ "manager": "approver" }"""));
        using var folder = Application(Entry("b", roles: """{ "approver": "lead" }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnmappedModuleRoleSlot, "modules/b/module.json", "/modules/0/roles"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'auditor'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(diagnostic.ResourceId);
    }

    [Fact]
    public void An_entry_without_roles_reports_the_unmapped_slot_at_the_entry()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "a", RoleSlots("a", "manager"));
        Install(modules, "b", RoleSlots("b", "approver") + Depends("a"));
        using var folder = Application(Entry("b", roles: """{ "approver": "lead" }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnmappedModuleRoleSlot, "modules/b/module.json", "/modules/0"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_slot_mapped_to_a_name_that_is_no_slot_of_the_depending_module_is_reported_at_its_key()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "a", RoleSlots("a", "manager"));
        Install(modules, "b", RoleSlots("b", "approver") + Depends("a", roles: """{ "manager": "Approver" }"""));
        using var folder = Application(Entry("b", roles: """{ "approver": "lead" }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownModuleSlotRole, "modules/b/module.json", "/modules/0/roles/manager"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Approver'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'b'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_that_is_no_slot_of_the_dependency_is_reported_at_the_key_with_the_pointer_escaped()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "a", RoleSlots("a", "manager"));
        Install(modules, "b", RoleSlots("b", "approver") + Depends("a", roles: """{ "manager": "approver", "x/y~z": "approver" }"""));
        using var folder = Application(Entry("b", roles: """{ "approver": "lead" }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownModuleRoleSlot, "modules/b/module.json", "/modules/0/roles/x~1y~0z"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_connector_slot_of_a_dependency_becomes_the_application_connector_the_chain_reaches()
    {
        using var modules = ConnectorModules("decimal");
        using var folder = Application(Entry("b", connectors: """{ "purchasing": "purchasingsystem" }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);
        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(compiled.Diagnostics);
        Assert.Empty(rebuilt.Diagnostics);
        foreach (var model in new[] { compiled.Model!, rebuilt.Model! })
        {
            Assert.Equal(["b", "a"], model.Modules.Select(pin => pin.Id));
            Assert.Equal(new Dictionary<string, string> { ["purchasing"] = "PurchasingSystem" }, model.Modules[0].Connectors);
            Assert.Equal(new Dictionary<string, string> { ["orders"] = "PurchasingSystem" }, model.Modules[1].Connectors);
        }
    }

    [Fact]
    public void A_type_mismatch_between_the_slots_names_the_slot_the_operation_and_the_field()
    {
        using var modules = ConnectorModules("integer");
        using var folder = Application(Entry("b", connectors: """{ "purchasing": "PurchasingSystem" }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MismatchedSlotInput, "modules/b/module.json", "/modules/0/connectors/orders"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        foreach (var name in new[] { "orders", "CreateOrder", "amount", "integer", "decimal" })
        {
            Assert.Contains($"'{name}'", diagnostic.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_slot_of_the_depending_module_without_the_operation_is_reported()
    {
        using var modules = ConnectorModules("decimal", operation: "PlaceOrder");
        using var folder = Application(Entry("b", connectors: """{ "purchasing": "PurchasingSystem" }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        // The slot of b names PlaceOrder, which the application connector lacks, so application.json reports it too.
        var diagnostic = Assert.Single(compiled.Diagnostics, candidate => candidate.File == "modules/b/module.json");
        Assert.Equal(
            (DiagnosticCodes.MissingSlotOperation, "/modules/0/connectors/orders"),
            (diagnostic.Code, diagnostic.Path));
        Assert.Contains("'CreateOrder'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_required_input_is_checked_against_the_connector_the_chain_reaches()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "a", ConnectorSlot("a", "orders", """{ "name": "amount", "type": "decimal" }"""));
        Install(modules, "b", ConnectorSlot("b", "purchasing", """{ "name": "code", "type": "text" }, { "name": "amount", "type": "decimal" }""")
            + Depends("a", connectors: """{ "orders": "purchasing" }"""));
        using var folder = Application(Entry("b", connectors: """{ "purchasing": "PurchasingSystem" }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingSlotRequiredInput, "modules/b/module.json", "/modules/0/connectors/orders"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'code'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_application_mapping_that_differs_from_the_chain_is_a_conflict_that_names_both_roles()
    {
        using var modules = RoleModules();
        using var folder = Application(
            Entry("a", roles: """{ "manager": "other" }"""),
            Entry("b", roles: """{ "approver": "lead" }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleSlotMappingConflict, "modules/b/module.json", "/modules/0/roles/manager"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        foreach (var name in new[] { "manager", "a", "other", "lead", "b", "approver" })
        {
            Assert.Contains($"'{name}'", diagnostic.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_application_mapping_that_equals_the_chain_is_no_conflict()
    {
        using var modules = RoleModules();
        using var folder = Application(
            Entry("a", roles: """{ "manager": "lead" }"""),
            Entry("b", roles: """{ "approver": "lead" }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal("lead", compiled.Model!.Modules[0].Roles["manager"]);
    }

    [Fact]
    public void A_connector_mapping_of_the_application_that_differs_from_the_chain_is_a_conflict()
    {
        using var modules = ConnectorModules("decimal");
        using var folder = Application(
            Entry("a", connectors: """{ "orders": "OtherSystem" }"""),
            Entry("b", connectors: """{ "purchasing": "PurchasingSystem" }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleSlotMappingConflict, "modules/b/module.json", "/modules/0/connectors/orders"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'OtherSystem'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'PurchasingSystem'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_depending_modules_that_map_a_shared_slot_differently_conflict_at_the_later_one()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "a", RoleSlots("a", "manager"));
        Install(modules, "b", RoleSlots("b", "approver") + Depends("a", roles: """{ "manager": "approver" }"""));
        Install(modules, "c", RoleSlots("c", "reviewer") + Depends("a", roles: """{ "manager": "reviewer" }"""));
        using var folder = Application(
            Entry("b", roles: """{ "approver": "lead" }"""),
            Entry("c", roles: """{ "reviewer": "other" }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleSlotMappingConflict, "modules/c/module.json", "/modules/0/roles/manager"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'lead'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'other'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_slot_of_the_depending_module_the_application_leaves_unmapped_gives_nothing_beyond_that_report()
    {
        using var modules = RoleModules();
        using var folder = Application(Entry("b", roles: """{ }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnmappedModuleRoleSlot, "application.json"),
            (diagnostic.Code, diagnostic.File));
    }

    [Fact]
    public void A_cycle_is_reported_once_and_gives_no_slot_map()
    {
        using var modules = new TemporaryFolder();
        Install(modules, "a", RoleSlots("a", "manager") + Depends("b", roles: """{ "approver": "manager" }"""));
        Install(modules, "b", RoleSlots("b", "approver") + Depends("a", roles: """{ "manager": "approver" }"""));
        using var folder = Application(Entry("b", roles: """{ "approver": "lead" }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(DiagnosticCodes.ModuleDependencyCycle, diagnostic.Code);
    }

    /// <summary>Module <c>a</c> has the role slot <c>manager</c>. Module <c>b</c> depends on it and has the slot <c>approver</c>.</summary>
    private static TemporaryFolder RoleModules()
    {
        var modules = new TemporaryFolder();
        Install(modules, "a", RoleSlots("a", "manager"));
        Install(modules, "b", RoleSlots("b", "approver") + Depends("a", roles: """{ "manager": "approver" }"""));
        return modules;
    }

    /// <summary>
    /// Module <c>a</c> has the connector slot <c>orders</c>, whose <c>amount</c> input has the type
    /// <paramref name="amountType"/>. Module <c>b</c> depends on it and has the slot <c>purchasing</c>,
    /// which fits the application's connector.
    /// </summary>
    private static TemporaryFolder ConnectorModules(string amountType, string operation = "CreateOrder")
    {
        var modules = new TemporaryFolder();
        Install(modules, "a", ConnectorSlot("a", "orders", $$"""{ "name": "code", "type": "text" }, { "name": "amount", "type": "{{amountType}}" }"""));
        Install(
            modules,
            "b",
            ConnectorSlot("b", "purchasing", """{ "name": "code", "type": "text" }, { "name": "amount", "type": "decimal" }""", operation)
                + Depends("a", connectors: """{ "orders": "purchasing" }"""));
        return modules;
    }

    private static void Install(TemporaryFolder root, string id, string extras)
    {
        root.With($"{id}/1.0.0/module.json", $$"""
            { "kind": "module", "id": "{{id}}", "version": "1.0.0", "label": "{{id}}"{{extras}} }
            """);
        root.With($"{id}/1.0.0/TextsEn.json", $$"""
            { "id": "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b{{(int)id[0]:x2}}", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
              "texts": { "{{id}}.slot": "Slot" } }
            """);
    }

    private static string RoleSlots(string module, params string[] names) =>
        $$""", "roles": [{{string.Join(", ", names.Select(name => $$"""{ "name": "{{name}}", "label": { "textKey": "{{module}}.slot" } }"""))}}]""";

    private static string ConnectorSlot(string module, string name, string input, string operation = "CreateOrder") => $$"""
        , "connectors": [ { "name": "{{name}}", "label": { "textKey": "{{module}}.slot" },
            "operations": [ { "name": "{{operation}}", "input": [ {{input}} ], "output": [ { "name": "orderId", "type": "text" } ] } ] } ]
        """;

    private static string Depends(string target, string? roles = null, string? connectors = null) =>
        $$""", "modules": [ { "id": "{{target}}", "version": "^1.0.0"{{(roles is null ? "" : $$""", "roles": {{roles}}""")}}{{(connectors is null ? "" : $$""", "connectors": {{connectors}}""")}} } ]""";

    private static string Entry(string id, string? roles = null, string? connectors = null) =>
        $$"""{ "id": "{{id}}", "version": "^1.0.0"{{(roles is null ? "" : $$""", "roles": {{roles}}""")}}{{(connectors is null ? "" : $$""", "connectors": {{connectors}}""")}} }""";

    private static TemporaryFolder Application(params string[] entries) =>
        new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1,
                  "modules": [{{string.Join(", ", entries)}}] }
                """)
            .With("texts/en.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "role.manager": "lead", "role.other": "other" } }
                """)
            .With("roles/lead.json", """
                { "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "lead", "formatVersion": 1, "label": { "textKey": "role.manager" } }
                """)
            .With("roles/other.json", """
                { "id": "44444444-4444-4444-8444-444444444402", "kind": "role", "name": "other", "formatVersion": 1, "label": { "textKey": "role.other" } }
                """)
            .With("connectors/purchasing.json", $$"""
                { "id": "66666666-6666-4666-8666-666666666601", "kind": "connector", "name": "PurchasingSystem", "formatVersion": 1,
                  "baseUrl": { "binding": "PurchasingUrl" }, "operations": [ {{OrdersOperation}} ] }
                """)
            .With("connectors/other.json", $$"""
                { "id": "66666666-6666-4666-8666-666666666602", "kind": "connector", "name": "OtherSystem", "formatVersion": 1,
                  "baseUrl": { "binding": "OtherUrl" }, "operations": [ {{OrdersOperation}} ] }
                """);
}
