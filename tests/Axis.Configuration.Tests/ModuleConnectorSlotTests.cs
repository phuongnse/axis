using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;

namespace Axis.Configuration.Tests;

public sealed class ModuleConnectorSlotTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01";
    private const string ManifestFile = "departments/1.0.0/module.json";

    private const string Slots = """
        [ { "name": "orders", "label": { "textKey": "departments.orders" },
            "operations": [ { "name": "CreateOrder",
                              "input": [ { "name": "code", "type": "text" }, { "name": "amount", "type": "decimal" } ],
                              "output": [ { "name": "orderId", "type": "text" } ] } ] } ]
        """;

    private const string Operation = """
        { "name": "CreateOrder", "method": "POST", "path": "/orders",
          "input": [ { "name": "code", "type": "text", "required": true }, { "name": "amount", "type": "decimal" } ],
          "output": [ { "name": "orderId", "type": "text" } ] }
        """;

    [Fact]
    public void A_connector_that_fits_every_operation_and_field_keeps_the_map_in_the_model()
    {
        using var modules = Modules(Slots);
        using var folder = Application("""{ "orders": "PurchasingSystem" }""", Operation);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);
        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(compiled.Diagnostics);
        var expected = new Dictionary<string, string> { ["orders"] = "PurchasingSystem" };
        Assert.Equal(expected, Assert.Single(compiled.Model!.Modules).Connectors);
        Assert.Empty(rebuilt.Diagnostics);
        Assert.Equal(expected, Assert.Single(rebuilt.Model!.Modules).Connectors);
    }

    [Fact]
    public void A_connector_name_is_matched_ignoring_letter_case_and_the_map_keeps_the_declared_name()
    {
        using var modules = Modules(Slots);
        using var folder = Application(
            """{ "orders": "purchasingsystem" }""",
            Operation.Replace("CreateOrder", "createORDER", StringComparison.Ordinal).Replace("\"code\"", "\"Code\"", StringComparison.Ordinal));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal("PurchasingSystem", Assert.Single(compiled.Model!.Modules).Connectors["orders"]);
    }

    [Fact]
    public void A_module_without_slots_has_an_empty_map()
    {
        using var modules = Modules(null);
        using var folder = Application(null, Operation);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.Empty(Assert.Single(compiled.Model!.Modules).Connectors);
    }

    [Fact]
    public void An_unmapped_slot_is_reported_at_the_connectors_of_the_entry()
    {
        using var modules = Modules(Slots);
        using var folder = Application("""{ "other": "PurchasingSystem" }""", Operation);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = compiled.Diagnostics.First(candidate => candidate.Code == DiagnosticCodes.UnmappedModuleConnectorSlot);
        Assert.Equal(("application.json", "/modules/0/connectors"), (diagnostic.File, diagnostic.Path));
        Assert.Contains("'orders'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void An_unmapped_slot_of_an_entry_without_connectors_is_reported_at_the_entry()
    {
        using var modules = Modules(Slots);
        using var folder = Application(null, Operation);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnmappedModuleConnectorSlot, "application.json", "/modules/0"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_slot_mapped_to_an_unknown_connector_is_reported_at_its_key()
    {
        using var modules = Modules(Slots);
        using var folder = Application("""{ "orders": "Billing" }""", Operation);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownModuleSlotConnector, "application.json", "/modules/0/connectors/orders"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Billing'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_that_is_no_slot_is_reported_at_the_key_with_the_pointer_escaped()
    {
        using var modules = Modules(Slots);
        using var folder = Application("""{ "orders": "PurchasingSystem", "a/b~c": "PurchasingSystem", "Orders": "PurchasingSystem" }""", Operation);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownModuleConnectorSlot, "application.json", "/modules/0/connectors/Orders"),
                (DiagnosticCodes.UnknownModuleConnectorSlot, "application.json", "/modules/0/connectors/a~1b~0c"),
            ],
            compiled.Diagnostics
                .Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path))
                .OrderBy(diagnostic => diagnostic.Path, StringComparer.Ordinal));
    }

    [Fact]
    public void A_key_of_a_module_without_slots_is_reported_as_no_slot()
    {
        using var modules = Modules(null);
        using var folder = Application("""{ "orders": "PurchasingSystem" }""", Operation);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownModuleConnectorSlot, "application.json", "/modules/0/connectors/orders"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("no connector slots", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_connector_without_the_operation_of_the_slot_is_reported()
    {
        using var modules = Modules(Slots);
        using var folder = Application("""{ "orders": "PurchasingSystem" }""", Operation.Replace("CreateOrder", "PlaceOrder", StringComparison.Ordinal));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingSlotOperation, "application.json", "/modules/0/connectors/orders"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        AssertNames(diagnostic.Message, "orders", "CreateOrder");
    }

    [Fact]
    public void A_slot_input_of_another_type_is_reported()
    {
        using var modules = Modules(Slots);
        using var folder = Application(
            """{ "orders": "PurchasingSystem" }""",
            Operation.Replace("""{ "name": "amount", "type": "decimal" }""", """{ "name": "amount", "type": "integer" }""", StringComparison.Ordinal));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MismatchedSlotInput, "application.json", "/modules/0/connectors/orders"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        AssertNames(diagnostic.Message, "orders", "CreateOrder", "amount", "decimal", "integer");
    }

    [Fact]
    public void A_slot_input_the_operation_lacks_is_reported()
    {
        using var modules = Modules(Slots);
        using var folder = Application(
            """{ "orders": "PurchasingSystem" }""",
            Operation.Replace(""", { "name": "amount", "type": "decimal" }""", "", StringComparison.Ordinal));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(DiagnosticCodes.MismatchedSlotInput, diagnostic.Code);
        AssertNames(diagnostic.Message, "orders", "CreateOrder", "amount");
    }

    [Fact]
    public void A_required_operation_input_the_slot_lacks_is_reported()
    {
        using var modules = Modules(Slots.Replace("""{ "name": "code", "type": "text" }, """, "", StringComparison.Ordinal));
        using var folder = Application("""{ "orders": "PurchasingSystem" }""", Operation);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingSlotRequiredInput, "application.json", "/modules/0/connectors/orders"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        AssertNames(diagnostic.Message, "orders", "CreateOrder", "code");
    }

    [Fact]
    public void A_slot_output_the_operation_lacks_is_reported()
    {
        using var modules = Modules(Slots);
        using var folder = Application(
            """{ "orders": "PurchasingSystem" }""",
            Operation.Replace("""{ "name": "orderId", "type": "text" }""", """{ "name": "other", "type": "text" }""", StringComparison.Ordinal));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MismatchedSlotOutput, "application.json", "/modules/0/connectors/orders"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        AssertNames(diagnostic.Message, "orders", "CreateOrder", "orderId");
    }

    [Fact]
    public void A_slot_output_of_another_type_is_reported()
    {
        using var modules = Modules(Slots);
        using var folder = Application(
            """{ "orders": "PurchasingSystem" }""",
            Operation.Replace("""{ "name": "orderId", "type": "text" }""", """{ "name": "orderId", "type": "integer" }""", StringComparison.Ordinal));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(DiagnosticCodes.MismatchedSlotOutput, diagnostic.Code);
        AssertNames(diagnostic.Message, "orders", "CreateOrder", "orderId", "text", "integer");
    }

    [Fact]
    public void A_slot_label_without_the_module_prefix_is_reported_in_the_manifest()
    {
        using var modules = Modules(Slots.Replace("departments.orders", "orders", StringComparison.Ordinal));
        using var folder = Application("""{ "orders": "PurchasingSystem" }""", Operation);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleTextKeyWithoutPrefix, "modules/departments/module.json", "/connectors/0/label/textKey"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("Orders")]
    public void Two_connector_slots_with_the_same_name_leave_the_version_out(string second)
    {
        using var root = new TemporaryFolder().With(
            ManifestFile,
            $$"""
            { "kind": "module", "id": "departments", "version": "1.0.0", "label": "Departments",
              "connectors": [
                { "name": "orders", "label": { "textKey": "departments.a" }, "operations": [ { "name": "CreateOrder" } ] },
                { "name": "{{second}}", "label": { "textKey": "departments.b" }, "operations": [ { "name": "CreateOrder" } ] } ] }
            """);

        var catalog = ModuleCatalog.Load(root.Path);

        Assert.Empty(catalog.Modules);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleManifestInvalid, ManifestFile, "/connectors/1/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Theory]
    [InlineData("""{ "name": "CreateOrder" }, { "name": "createorder" }""", "/connectors/0/operations/1/name")]
    [InlineData("""{ "name": "CreateOrder", "input": [ { "name": "a", "type": "text" }, { "name": "A", "type": "text" } ] }""", "/connectors/0/operations/0/input/1/name")]
    [InlineData("""{ "name": "CreateOrder", "output": [ { "name": "a", "type": "text" }, { "name": "A", "type": "text" } ] }""", "/connectors/0/operations/0/output/1/name")]
    public void A_repeated_operation_or_field_name_leaves_the_version_out(string operations, string path)
    {
        using var root = new TemporaryFolder().With(
            ManifestFile,
            $$"""
            { "kind": "module", "id": "departments", "version": "1.0.0", "label": "Departments",
              "connectors": [ { "name": "orders", "label": { "textKey": "departments.a" }, "operations": [ {{operations}} ] } ] }
            """);

        var catalog = ModuleCatalog.Load(root.Path);

        Assert.Empty(catalog.Modules);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleManifestInvalid, ManifestFile, path),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    private static void AssertNames(string message, params string[] names)
    {
        foreach (var name in names)
        {
            Assert.Contains($"'{name}'", message, StringComparison.Ordinal);
        }
    }

    private static TemporaryFolder Modules(string? slots)
    {
        var connectors = slots is null ? "" : $$""", "connectors": {{slots}}""";
        return new TemporaryFolder()
            .With(ManifestFile, $$"""{ "kind": "module", "id": "departments", "version": "1.0.0", "label": "Departments"{{connectors}} }""")
            .With("departments/1.0.0/TextsEn.json", """
                { "id": "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b6c", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "departments.orders": "Orders" } }
                """);
    }

    private static TemporaryFolder Application(string? mapping, string operation)
    {
        var connectors = mapping is null ? "" : $$""", "connectors": {{mapping}}""";
        return new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1,
                  "modules": [{ "id": "departments", "version": "1.0.0"{{connectors}} }] }
                """)
            .With("connectors/purchasing.json", $$"""
                { "id": "66666666-6666-4666-8666-666666666601", "kind": "connector", "name": "PurchasingSystem", "formatVersion": 1,
                  "baseUrl": { "binding": "PurchasingUrl" }, "operations": [ {{operation}} ] }
                """);
    }
}
