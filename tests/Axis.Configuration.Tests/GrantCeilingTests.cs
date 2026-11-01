using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;

namespace Axis.Configuration.Tests;

public sealed class GrantCeilingTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e21";
    private const string PolicyIdPrefix = "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c";
    private const string ModulePolicyId = "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c53";

    // The buyer slot reads and creates two fields, the seller slot reads every field and updates one, others only read.
    private static readonly string[] _roleNames = ["clerk", "lead", "guest", "other"];

    private const string Ceiling = """
        { "slots": {
            "buyer": [ { "actions": ["read", "create"], "fields": ["title", "amount"] } ],
            "seller": [ { "actions": ["read"] }, { "actions": ["update"], "fields": ["note"], "filter": "amount > 10" } ] },
          "others": [ { "actions": ["read"], "fields": ["title"] } ] }
        """;

    [Fact]
    public void A_grant_within_the_ceiling_compiles_and_the_contract_keeps_the_entries_of_each_role_and_of_others()
    {
        using var modules = Shop(Ceiling);
        using var folder = Application(
            Policy("clerk", """{ "entity": "shop/Order", "actions": ["read", "create"], "fields": ["TITLE", "amount"] }"""),
            Policy("lead", """{ "entity": "shop/Order", "actions": ["read"] }""", """{ "entity": "shop/Order", "actions": ["update"], "fields": ["note"] }"""),
            Policy("guest", """{ "entity": "shop/Order", "actions": ["read"], "fields": ["title"] }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);
        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(compiled.Diagnostics);
        Assert.Empty(rebuilt.Diagnostics);
        foreach (var model in new[] { compiled.Model!, rebuilt.Model! })
        {
            var ceiling = Assert.IsType<CeilingModel>(Assert.Single(model.Entities, entity => entity.Name == "shop/Order").Contract!.Ceiling);
            Assert.Equal(["clerk", "lead"], ceiling.Roles.Keys.Order());

            var clerk = Assert.Single(ceiling.Roles["clerk"]);
            Assert.Equal([PolicyAction.Read, PolicyAction.Create], clerk.Actions);
            Assert.Equal(["title", "amount"], clerk.Fields);
            Assert.Null(clerk.Filter);

            var lead = ceiling.Roles["lead"];
            Assert.Equal(2, lead.Count);
            Assert.Null(lead[0].Fields);
            Assert.Equal([PolicyAction.Update], lead[1].Actions);
            Assert.Equal("amount > 10", lead[1].Filter!.Expression);

            var others = Assert.Single(ceiling.Others!);
            Assert.Equal([PolicyAction.Read], others.Actions);
            Assert.Equal(["title"], others.Fields);
        }
    }

    [Fact]
    public void A_grant_past_the_ceiling_by_action_is_axc0334_at_the_grant_of_the_policy_file()
    {
        var diagnostic = Single(Policy("clerk", """{ "entity": "shop/Order", "actions": ["update"] }"""));

        AssertPast(diagnostic, 0);
        Assert.Contains("'clerk'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'update'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'buyer'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_grant_past_the_ceiling_by_a_field_is_axc0334_and_names_the_field()
    {
        var diagnostic = Single(Policy(
            "clerk",
            """{ "entity": "shop/Order", "actions": ["read"], "fields": ["title"] }""",
            """{ "entity": "shop/Order", "actions": ["read"], "fields": ["title", "Note"] }"""));

        AssertPast(diagnostic, 1);
        Assert.Contains("'note'", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'title'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_grant_without_fields_is_past_entries_that_all_have_fields_and_within_when_one_has_none()
    {
        var past = Single(Policy("clerk", """{ "entity": "shop/Order", "actions": ["read"] }"""));
        AssertPast(past, 0);

        // The seller slot has a 'read' entry without fields, so a grant without fields is within it.
        using var modules = Shop(Ceiling);
        using var folder = Application(Policy("lead", """{ "entity": "shop/Order", "actions": ["read"] }"""));
        Assert.Empty(ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules).Diagnostics);
    }

    [Fact]
    public void A_role_mapped_to_no_slot_is_checked_against_others()
    {
        var diagnostic = Single(Policy("guest", """{ "entity": "shop/Order", "actions": ["read"], "fields": ["title", "amount"] }"""));

        AssertPast(diagnostic, 0);
        Assert.Contains("'guest'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'others'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'amount'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_role_mapped_to_no_slot_may_get_no_grant_when_the_ceiling_has_no_others()
    {
        const string withoutOthers = """{ "slots": { "buyer": [ { "actions": ["read"] } ] } }""";
        var diagnostic = SingleWith(withoutOthers, Policy("guest", """{ "entity": "shop/Order", "actions": ["read"] }"""));

        AssertPast(diagnostic, 0);
        Assert.Contains("no 'others'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_role_mapped_to_a_slot_that_has_no_key_gets_no_entries_and_does_not_fall_back_to_others()
    {
        const string noSellerKey = """{ "slots": { "buyer": [ { "actions": ["read"] } ] }, "others": [ { "actions": ["read"] } ] }""";
        var diagnostic = SingleWith(noSellerKey, Policy("lead", """{ "entity": "shop/Order", "actions": ["read"] }"""));

        AssertPast(diagnostic, 0);
    }

    [Fact]
    public void Each_role_of_a_policy_is_checked_and_reported_once_for_the_grant()
    {
        using var modules = Shop(Ceiling);
        using var folder = Application(Policy(
            ["clerk", "lead", "guest"],
            """{ "entity": "shop/Order", "actions": ["delete"] }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Equal(3, compiled.Diagnostics.Count);
        Assert.All(compiled.Diagnostics, diagnostic => AssertPast(diagnostic, 0));
        Assert.Equal(["clerk", "guest", "lead"], compiled.Diagnostics.Select(diagnostic => diagnostic.Message.Split('\'')[1]).Order());
    }

    [Fact]
    public void A_role_mapped_to_two_slots_gets_the_entries_of_both()
    {
        using var modules = Shop(Ceiling);
        using var folder = Application(
            [Policy(
                "clerk",
                """{ "entity": "shop/Order", "actions": ["read", "create"], "fields": ["title"] }""",
                """{ "entity": "shop/Order", "actions": ["update"], "fields": ["note"] }""")],
            """{ "buyer": "clerk", "seller": "clerk" }""");

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(3, compiled.Model!.Entities.Single(entity => entity.Name == "shop/Order").Contract!.Ceiling!.Roles["clerk"].Count);
    }

    [Fact]
    public void A_module_policy_past_the_ceiling_of_its_own_module_is_axc0334_at_the_grant_in_the_module_policy_file()
    {
        using var modules = Shop(Ceiling)
            .With("shop/1.0.0/policies/Access.json", $$"""
                { "id": "{{ModulePolicyId}}", "kind": "policy", "name": "Access", "formatVersion": 1, "roles": ["buyer"],
                  "grants": [ { "entity": "Order", "actions": ["read"], "fields": ["title"] }, { "entity": "Order", "actions": ["delete"] } ] }
                """);
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.GrantPastCeiling, "modules/shop/policies/Access.json", "/grants/1", (Guid?)Guid.Parse(ModulePolicyId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'clerk'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_policy_within_the_ceiling_compiles()
    {
        using var modules = Shop(Ceiling)
            .With("shop/1.0.0/policies/Access.json", $$"""
                { "id": "{{ModulePolicyId}}", "kind": "policy", "name": "Access", "formatVersion": 1, "roles": ["buyer"],
                  "grants": [ { "entity": "Order", "actions": ["read", "create"], "fields": ["title"] } ] }
                """);
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
    }

    [Fact]
    public void A_role_reached_through_a_dependency_chain_gets_the_entries_of_the_slot_the_chain_gives_it()
    {
        // The application maps only the slot 'approver' of b. Module b maps the slot 'manager' of a to it.
        using var modules = new TemporaryFolder();
        Install(modules, "a", RoleSlots("a", "manager", "auditor"), """
            { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c61", "kind": "contract", "name": "ItemContract", "formatVersion": 1, "entity": "Item",
              "ceiling": { "slots": { "manager": [ { "actions": ["read", "update"] } ], "auditor": [ { "actions": ["read"] } ] } } }
            """);
        Install(modules, "b", RoleSlots("b", "approver", "watcher") + Depends("a", """{ "manager": "approver", "auditor": "watcher" }"""), null);
        using var folder = Application(
            [
                Policy("lead", """{ "entity": "a/Item", "actions": ["read", "update"] }"""),
                Policy("other", """{ "entity": "a/Item", "actions": ["read", "update"] }"""),
            ],
            "",
            modulesJson: """[ { "id": "b", "version": "^1.0.0", "roles": { "approver": "lead", "watcher": "other" } } ]""");

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        // 'lead' reaches manager and may update. 'other' reaches only auditor, which may only read.
        var diagnostic = Assert.Single(compiled.Diagnostics);
        AssertPast(diagnostic, 0, policy: 1);
        Assert.Contains("'other'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'auditor'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_grant_on_an_entity_without_a_ceiling_or_on_another_target_is_not_checked()
    {
        using var modules = Shop(Ceiling);
        using var folder = Application(Policy("guest", """{ "entity": "shop/Line", "actions": ["delete"] }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
    }

    /// <summary>Asserts a grant past the ceiling at a grant of the policy written at <paramref name="policy"/> of the application's policies.</summary>
    private static void AssertPast(Diagnostic diagnostic, int grant, int policy = 0)
    {
        Assert.Equal(
            (DiagnosticCodes.GrantPastCeiling, $"policies/Access{policy}.json", $"/grants/{grant}", (Guid?)PolicyId(policy)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
    }

    private static Guid PolicyId(int index) => Guid.Parse($"{PolicyIdPrefix}{70 + index}");

    private static Diagnostic Single(params string[] policies) => SingleWith(Ceiling, policies);

    private static Diagnostic SingleWith(string ceiling, params string[] policies)
    {
        using var modules = Shop(ceiling);
        using var folder = Application(policies);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Null(compiled.Model);
        return Assert.Single(compiled.Diagnostics);
    }

    /// <summary>A policy of the application. Its grants are the given JSON objects.</summary>
    private static string Policy(string role, params string[] grants) => Policy([role], grants);

    private static string Policy(string[] roles, params string[] grants) => $$"""
        { "id": "__ID__", "kind": "policy", "name": "__NAME__", "formatVersion": 1,
          "roles": [{{string.Join(", ", roles.Select(role => $"\"{role}\""))}}], "grants": [ {{string.Join(", ", grants)}} ] }
        """;

    private static string RoleSlots(string module, params string[] names) =>
        $$""", "roles": [{{string.Join(", ", names.Select(name => $$"""{ "name": "{{name}}", "label": { "textKey": "{{module}}.slot" } }"""))}}]""";

    private static string Depends(string target, string roles) =>
        $$""", "modules": [ { "id": "{{target}}", "version": "^1.0.0", "roles": {{roles}} } ]""";

    private static void Install(TemporaryFolder root, string id, string extras, string? contract)
    {
        root.With($"{id}/1.0.0/module.json", $$"""
            { "kind": "module", "id": "{{id}}", "version": "1.0.0", "label": "{{id}}"{{extras}} }
            """);
        root.With($"{id}/1.0.0/TextsEn.json", $$"""
            { "id": "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b{{(int)id[0]:x2}}", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
              "texts": { "{{id}}.slot": "Slot" } }
            """);
        if (contract is not null)
        {
            root.With($"{id}/1.0.0/Item.json", $$"""
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c60", "kind": "entity", "name": "Item", "formatVersion": 1,
                  "fields": [ { "name": "title", "type": "text" } ] }
                """);
            root.With($"{id}/1.0.0/Contract.json", contract);
        }
    }

    private static TemporaryFolder Shop(string ceiling) =>
        new TemporaryFolder()
            .With("shop/1.0.0/module.json", """
                { "kind": "module", "id": "shop", "version": "1.0.0", "label": "Shop",
                  "roles": [ { "name": "buyer", "label": { "textKey": "shop.slot" } }, { "name": "seller", "label": { "textKey": "shop.slot" } } ] }
                """)
            .With("shop/1.0.0/Order.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c51", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [ { "name": "title", "type": "text" }, { "name": "amount", "type": "integer" }, { "name": "note", "type": "text" } ] }
                """)
            .With("shop/1.0.0/Line.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c54", "kind": "entity", "name": "Line", "formatVersion": 1,
                  "fields": [ { "name": "quantity", "type": "integer" } ] }
                """)
            .With("shop/1.0.0/Contract.json", $$"""
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c55", "kind": "contract", "name": "OrderContract", "formatVersion": 1, "entity": "Order",
                  "ceiling": {{ceiling}} }
                """)
            .With("shop/1.0.0/TextsEn.json", """
                { "id": "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b6d", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "shop.slot": "Slot" } }
                """);

    private static TemporaryFolder Application(params string[] policies) =>
        Application(policies, """{ "buyer": "clerk", "seller": "lead" }""");

    private static TemporaryFolder Application(string[] policies, string roles, string? modulesJson = null)
    {
        var folder = new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1,
                  "modules": {{modulesJson ?? $$"""[ { "id": "shop", "version": "1.0.0", "roles": {{roles}} } ]"""}} }
                """)
            .With("texts/en.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "role.clerk": "Clerk", "role.lead": "Lead", "role.guest": "Guest", "role.other": "Other" } }
                """);
        foreach (var (name, index) in _roleNames.Select((name, index) => (name, index)))
        {
            folder.With($"roles/{name}.json", $$"""
                { "id": "44444444-4444-4444-8444-4444444444{{index + 1:x2}}", "kind": "role", "name": "{{name}}", "formatVersion": 1, "label": { "textKey": "role.{{name}}" } }
                """);
        }

        // Each policy gets its own id, name and file.
        for (var index = 0; index < policies.Length; index++)
        {
            folder.With($"policies/Access{index}.json", policies[index].Replace("__ID__", PolicyId(index).ToString(), StringComparison.Ordinal).Replace("__NAME__", $"Access{index}", StringComparison.Ordinal));
        }

        return folder;
    }
}
