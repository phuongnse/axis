using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class ConnectorInputMappingTests
{
    private const string Address = """
        { "name": "address", "type": "object", "fields": [ { "name": "city", "type": "text", "required": true }, { "name": "zip", "type": "text" } ] }
        """;

    private const string Lines = """
        { "name": "lines", "type": "list", "fields": [ { "name": "sku", "type": "text", "required": true }, { "name": "quantity", "type": "integer" } ] }
        """;

    [Fact]
    public void An_object_and_a_list_mapping_compile_into_nested_inputs_in_the_operations_order()
    {
        var result = Compile("""
            "address": { "fields": { "zip": "'1000'", "city": "title" } },
            "lines": { "from": "LINES", "fields": { "quantity": "count", "sku": "code" } }
            """);

        Assert.Empty(result.Diagnostics);
        var call = Assert.IsType<CallConnectorStepModel>(Assert.Single(result.Model!.Processes).Steps[0]);
        Assert.Equal(["address", "lines"], call.Input.Select(input => input.Input));

        var address = call.Input[0];
        Assert.Null(address.Value);
        Assert.Null(address.From);
        Assert.Equal([("city", "title"), ("zip", "'1000'")], address.Fields.Select(field => (field.Input, field.Value!.Expression)));

        var lines = call.Input[1];
        Assert.Null(lines.Value);
        Assert.Equal("lines", lines.From);
        Assert.Equal([("sku", "code"), ("quantity", "count")], lines.Fields.Select(field => (field.Input, field.Value!.Expression)));
    }

    [Fact]
    public void A_from_that_names_a_scalar_field_is_refused_at_its_path()
    {
        var result = Compile("""
            "address": { "fields": { "city": "title" } },
            "lines": { "from": "title", "fields": { "sku": "code" } }
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidMappingSource, "/steps/0/input/lines/from"), (diagnostic.Code, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_from_that_names_no_field_is_refused_but_the_item_names_are_still_checked()
    {
        var result = Compile("""
            "address": { "fields": { "city": "title" } },
            "lines": { "from": "rows", "fields": { "sku": "code", "nope": "code" } }
            """);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownConnectorInput, "/steps/0/input/lines/fields/nope"),
                (DiagnosticCodes.InvalidMappingSource, "/steps/0/input/lines/from"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)).OrderBy(found => found.Code, StringComparer.Ordinal));
    }

    [Fact]
    public void A_text_item_field_mapped_to_an_integer_expression_is_refused_at_its_path()
    {
        var result = Compile("""
            "address": { "fields": { "city": "title" } },
            "lines": { "from": "lines", "fields": { "sku": "count" } }
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((ExpressionDiagnosticCodes.ResultTypeMismatch, "/steps/0/input/lines/fields/sku"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void An_item_expression_sees_the_child_row_and_not_the_parent()
    {
        var result = Compile("""
            "address": { "fields": { "city": "title" } },
            "lines": { "from": "lines", "fields": { "sku": "title" } }
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(("AXC0046", "/steps/0/input/lines/fields/sku"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void An_unknown_repeated_and_missing_nested_name_are_reported_at_their_paths()
    {
        var result = Compile("""
            "address": { "fields": { "zip": "'1'", "nope": "title", "ZIP": "'2'" } },
            "lines": { "from": "lines", "fields": { "quantity": "count" } }
            """);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownConnectorInput, "/steps/0/input/address/fields/ZIP"),
                (DiagnosticCodes.UnknownConnectorInput, "/steps/0/input/address/fields/nope"),
                (DiagnosticCodes.MissingConnectorInput, "/steps/0/input/address/fields"),
                (DiagnosticCodes.MissingConnectorInput, "/steps/0/input/lines/fields"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)).OrderBy(found => found.Code, StringComparer.Ordinal).ThenBy(found => found.Path, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("\"address\": \"title\"", "/steps/0/input/address")]
    [InlineData("\"address\": { \"from\": \"lines\", \"fields\": { \"city\": \"title\" } }", "/steps/0/input/address")]
    [InlineData("\"address\": { \"fields\": { \"city\": \"title\" } }, \"lines\": \"title\"", "/steps/0/input/lines")]
    [InlineData("\"address\": { \"fields\": { \"city\": \"title\" } }, \"lines\": { \"fields\": { \"sku\": \"code\" } }", "/steps/0/input/lines")]
    [InlineData("\"address\": { \"fields\": { \"city\": { \"fields\": { \"x\": \"title\" } } } }", "/steps/0/input/address/fields/city")]
    public void A_mapping_whose_shape_does_not_fit_its_input_is_refused(string input, string path)
    {
        var lines = input.Contains("\"lines\"", StringComparison.Ordinal) ? "" : ", \"lines\": { \"from\": \"lines\", \"fields\": { \"sku\": \"code\" } }";
        var result = Compile(input + lines);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.MismatchedInputMapping, path), (diagnostic.Code, diagnostic.Path));
        Assert.Null(result.Model);
    }

    private static CompilationResult Compile(string input) =>
        ApplicationCompiler.Compile(
        [
            new ResourceContent("application.json", """
                { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1 }
                """),
            new ResourceContent("entities/request.json", """
                { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Request", "formatVersion": 1,
                  "fields": [
                    { "name": "title", "type": "text" },
                    { "name": "lines", "type": "child-collection", "target": "Line" }
                  ] }
                """),
            new ResourceContent("entities/line.json", """
                { "id": "44444444-4444-4444-8444-444444444444", "kind": "entity", "name": "Line", "formatVersion": 1,
                  "fields": [
                    { "name": "code", "type": "text" },
                    { "name": "count", "type": "integer" },
                    { "name": "product", "type": "reference", "target": "Person" }
                  ] }
                """),
            new ResourceContent("entities/person.json", """
                { "id": "33333333-3333-4333-8333-333333333333", "kind": "entity", "name": "Person", "formatVersion": 1, "displayField": "name",
                  "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """),
            new ResourceContent("connectors/purchasing.json", $$"""
                { "id": "66666666-6666-4666-8666-666666666666", "kind": "connector", "name": "Purchasing", "formatVersion": 1,
                  "baseUrl": { "binding": "PurchasingUrl" },
                  "auth": { "type": "bearer", "token": { "binding": "PurchasingToken" } },
                  "idempotency": "Idempotency-Key",
                  "operations": [
                    { "name": "createOrder", "method": "POST", "path": "/orders",
                      "input": [ {{Address}}, {{Lines}} ],
                      "output": [ { "name": "orderNumber", "type": "text" } ] }
                  ] }
                """),
            new ResourceContent("processes/order.json", $$"""
                { "id": "77777777-7777-4777-8777-777777777777", "kind": "process", "name": "Order", "formatVersion": 1,
                  "entity": "Request", "start": "call",
                  "steps": [
                    { "name": "call", "type": "operation", "operation": "callConnector",
                      "connector": { "name": "Purchasing", "operation": "createOrder" },
                      "input": { {{input}} }, "next": "done" },
                    { "name": "done", "type": "end" }
                  ] }
                """),
        ], []);
}
