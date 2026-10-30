using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;

namespace Axis.Configuration.Tests;

public sealed class ModuleConnectorCallTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01";
    private const string ProcessFile = "modules/departments/processes/Place.json";

    private const string Slots = """
        [ { "name": "orders", "label": { "textKey": "departments.orders" },
            "operations": [ { "name": "CreateOrder",
                              "input": [ { "name": "code", "type": "text", "required": true } ],
                              "output": [ { "name": "orderId", "type": "text" } ] } ] } ]
        """;

    private const string Operation = """
        { "name": "CreateOrder", "method": "POST", "path": "/orders",
          "input": [ { "name": "code", "type": "text", "required": true }, { "name": "amount", "type": "decimal" } ],
          "output": [ { "name": "orderId", "type": "text" } ] }
        """;

    private const string Call = """
        "connector": { "name": "orders", "operation": "createOrder" },
        "input": { "code": "code" }, "output": { "orderId": "orderId" }
        """;

    [Fact]
    public void Each_application_compiles_the_step_to_the_connector_its_slot_maps_to()
    {
        using var modules = Modules(Call);
        using var first = Application("PurchasingSystem", Operation);
        using var second = Application("OtherSystem", Operation.Replace("CreateOrder", "createORDER", StringComparison.Ordinal));
        var catalog = ModuleCatalog.Load(modules.Path).Modules;

        var firstResult = ApplicationCompiler.Compile(first.Path, null, catalog);
        var secondResult = ApplicationCompiler.Compile(second.Path, null, catalog);

        Assert.Empty(firstResult.Diagnostics);
        Assert.Empty(secondResult.Diagnostics);
        AssertCall(firstResult.Model!, "PurchasingSystem", "CreateOrder");
        AssertCall(secondResult.Model!, "OtherSystem", "createORDER");
        AssertCall(ApplicationCompiler.Compile(firstResult.Resources).Model!, "PurchasingSystem", "CreateOrder");
        AssertCall(ApplicationCompiler.Compile(secondResult.Resources).Model!, "OtherSystem", "createORDER");
    }

    [Fact]
    public void A_step_that_names_an_application_connector_is_reported_in_the_module_file()
    {
        using var modules = Modules(Call.Replace("\"orders\"", "\"PurchasingSystem\"", StringComparison.Ordinal));
        using var folder = Application("PurchasingSystem", Operation);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownConnector, ProcessFile, "/steps/0/connector/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("A module process calls a connector slot", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_slot_name_is_matched_exactly()
    {
        using var modules = Modules(Call.Replace("\"orders\"", "\"Orders\"", StringComparison.Ordinal));
        using var folder = Application("PurchasingSystem", Operation);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownConnector, ProcessFile, "/steps/0/connector/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_step_whose_operation_the_slot_lacks_is_reported_in_the_module_file()
    {
        using var modules = Modules(Call.Replace("createOrder", "Missing", StringComparison.Ordinal));
        using var folder = Application("PurchasingSystem", Operation);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownConnectorOperation, ProcessFile, "/steps/0/connector/operation"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Missing'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_step_input_that_the_connector_has_but_the_slot_lacks_is_reported_in_the_module_file()
    {
        using var modules = Modules(Call.Replace("\"code\": \"code\"", "\"code\": \"code\", \"amount\": \"code\"", StringComparison.Ordinal));
        using var folder = Application("PurchasingSystem", Operation);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownConnectorInput, ProcessFile, "/steps/0/input/amount"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_required_slot_input_the_step_leaves_out_is_reported_in_the_module_file()
    {
        using var modules = Modules(Call.Replace("\"code\": \"code\"", "", StringComparison.Ordinal));
        using var folder = Application("PurchasingSystem", Operation);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(ProcessFile, diagnostic.File);
        Assert.Contains("'code'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
    }

    private static void AssertCall(ApplicationModel model, string connector, string operation)
    {
        var process = Assert.Single(model.Processes);
        var call = Assert.IsType<CallConnectorStepModel>(process.Steps[0]);
        Assert.Equal((connector, operation), (call.Connector, call.Operation));
        Assert.Equal(["code"], call.Input.Select(input => input.Input));
        Assert.Equal([("orderId", "orderId")], call.Output.Select(output => (output.Output, output.Field)));
    }

    private static TemporaryFolder Modules(string call) =>
        new TemporaryFolder()
            .With("departments/1.0.0/module.json", $$"""
                { "kind": "module", "id": "departments", "version": "1.0.0", "label": "Departments", "connectors": {{Slots}} }
                """)
            .With("departments/1.0.0/Order.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c41", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [ { "name": "code", "type": "text" }, { "name": "orderId", "type": "text" } ] }
                """)
            .With("departments/1.0.0/processes/Place.json", $$"""
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c42", "kind": "process", "name": "Place", "formatVersion": 1,
                  "entity": "Order", "start": "call", "steps": [
                    { "name": "call", "type": "operation", "operation": "callConnector", {{call}}, "next": "done" },
                    { "name": "done", "type": "end" }
                  ] }
                """)
            .With("departments/1.0.0/TextsEn.json", """
                { "id": "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b6c", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "departments.orders": "Orders" } }
                """);

    private static TemporaryFolder Application(string connector, string operation) =>
        new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1,
                  "modules": [{ "id": "departments", "version": "1.0.0", "connectors": { "orders": "{{connector}}" } }] }
                """)
            .With("connectors/purchasing.json", $$"""
                { "id": "66666666-6666-4666-8666-666666666601", "kind": "connector", "name": "{{connector}}", "formatVersion": 1,
                  "baseUrl": { "binding": "PurchasingUrl" }, "operations": [ {{operation}} ] }
                """);
}
