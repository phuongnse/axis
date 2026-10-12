using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;

namespace Axis.Configuration.Tests;

public sealed class ConnectorCompilerTests
{
    private const string ConnectorId = "409bc638-0aec-4084-ada2-e3baed50057d";

    private const string Output = """[{ "name": "orderNumber", "type": "text", "required": true }]""";

    private const string FindOperation = """
        { "name": "findPurchaseOrder", "method": "GET", "path": "/purchase-orders/by-key/{idempotencyKey}", "input": [],
          "output": [{ "name": "orderNumber", "type": "text", "required": true }] }
        """;

    [Fact]
    public void A_connector_compiles_into_the_model_with_its_operations_and_their_typed_fields()
    {
        using var folder = Folder().With("connectors/purchasing-system.json", Connector($$"""
            "auth": { "type": "bearer", "token": { "binding": "PurchasingSystemToken" } },
            "timeout": "PT45S",
            "idempotency": "Idempotency-Key",
            "operations": [
              {
                "name": "createPurchaseOrder", "method": "POST", "path": "/purchase-orders",
                "input": [
                  { "name": "requestNumber", "type": "text", "required": true },
                  { "name": "supplierCode", "type": "text" },
                  { "name": "amount", "type": "decimal", "required": true }
                ],
                "output": {{Output}},
                "lookup": "FINDPURCHASEORDER"
              },
              {{FindOperation}}
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var connector = Assert.Single(result.Model.Connectors);
        Assert.Equal(
            (Guid.Parse(ConnectorId), "PurchasingSystem", "connectors/purchasing-system.json", "PurchasingSystemUrl", TimeSpan.FromSeconds(45), "Idempotency-Key"),
            (connector.Id, connector.Name, connector.File, connector.BaseUrlBinding, connector.Timeout, connector.Idempotency));
        Assert.Equal(new ConnectorAuthModel("bearer", null, "PurchasingSystemToken", "/auth/token/binding"), connector.Auth);
        Assert.Equal(2, connector.Operations.Count);
        var create = connector.Operations[0];
        Assert.Equal(("createPurchaseOrder", "POST", "/purchase-orders", "findPurchaseOrder"), (create.Name, create.Method, create.Path, create.Lookup));
        Assert.Equal(
            [
                new ConnectorFieldModel("requestNumber", FieldType.Text, true),
                new ConnectorFieldModel("supplierCode", FieldType.Text, false),
                new ConnectorFieldModel("amount", FieldType.Decimal, true),
            ],
            create.Input);
        Assert.Equal([new ConnectorFieldModel("orderNumber", FieldType.Text, true)], create.Output);
        var find = connector.Operations[1];
        Assert.Equal(("findPurchaseOrder", "GET", "/purchase-orders/by-key/{idempotencyKey}", null), (find.Name, find.Method, find.Path, find.Lookup));
        Assert.Empty(find.Input);
        Assert.Equal([new ConnectorFieldModel("orderNumber", FieldType.Text, true)], find.Output);
        Assert.Contains(result.Resources, resource => resource.Path == "connectors/purchasing-system.json");
    }

    [Fact]
    public void A_connector_without_timeout_or_auth_gets_the_default_timeout_and_no_auth()
    {
        using var folder = Folder().With("connectors/purchasing-system.json", Connector("""
            "auth": { "type": "none" },
            "operations": [{ "name": "ping", "method": "GET", "path": "/ping" }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var connector = Assert.Single(result.Model!.Connectors);
        Assert.Equal((TimeSpan.FromSeconds(30), null, null), (connector.Timeout, connector.Auth, connector.Idempotency));
    }

    [Fact]
    public void A_header_auth_names_its_header_and_its_secret_binding()
    {
        using var folder = Folder().With("connectors/purchasing-system.json", Connector("""
            "auth": { "type": "header", "name": "X-Api-Key", "value": { "binding": "PurchasingSystemKey" } },
            "operations": [{ "name": "ping", "method": "GET", "path": "/ping" }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            new ConnectorAuthModel("header", "X-Api-Key", "PurchasingSystemKey", "/auth/value/binding"),
            Assert.Single(result.Model!.Connectors).Auth);
    }

    [Fact]
    public void Connector_mistakes_are_each_a_diagnostic_at_their_path_in_one_pass()
    {
        using var folder = Folder().With("connectors/purchasing-system.json", Connector($$"""
            "timeout": "PT3M",
            "operations": [
              {
                "name": "createPurchaseOrder", "method": "POST", "path": "/purchase-orders",
                "input": [{ "name": "amount", "type": "decimal" }, { "name": "AMOUNT", "type": "integer" }],
                "output": {{Output}},
                "lookup": "nothing"
              },
              { "name": "CreatePurchaseOrder", "method": "PUT", "path": "/orders/{orderId}", "output": {{Output}}, "lookup": "createPurchaseOrder" }
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.DuplicateConnectorName, "/operations/0/input/1/name"),
                (DiagnosticCodes.InvalidOperationLookup, "/operations/0/lookup"),
                (DiagnosticCodes.InvalidOperationLookup, "/operations/1/lookup"),
                (DiagnosticCodes.DuplicateConnectorName, "/operations/1/name"),
                (DiagnosticCodes.InvalidOperationPath, "/operations/1/path"),
                (DiagnosticCodes.InvalidConnectorTimeout, "/timeout"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(("connectors/purchasing-system.json", Guid.Parse(ConnectorId)), (diagnostic.File, diagnostic.ResourceId)));
        Assert.Equal(
            "The lookup 'nothing' was not found. No operation of this connector has that name.",
            result.Diagnostics[1].Message);
        Assert.Equal(
            "The lookup 'createPurchaseOrder' is a POST operation. A lookup is a GET.",
            result.Diagnostics[2].Message);
        Assert.Equal(
            "The path '/orders/{orderId}' has the placeholder '{orderId}'. '{idempotencyKey}' is the only placeholder, and inputs are sent in the JSON body.",
            result.Diagnostics[4].Message);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("PT2M1S")]
    [InlineData("P1D")]
    [InlineData("PT0S")]
    [InlineData("P1M")]
    [InlineData("30s")]
    public void A_timeout_that_is_not_a_positive_duration_of_at_most_two_minutes_is_invalid(string timeout)
    {
        using var folder = Folder().With("connectors/purchasing-system.json", Connector($$"""
            "timeout": "{{timeout}}",
            "operations": [{ "name": "ping", "method": "GET", "path": "/ping" }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidConnectorTimeout, "/timeout"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void A_timeout_of_exactly_two_minutes_is_allowed()
    {
        using var folder = Folder().With("connectors/purchasing-system.json", Connector("""
            "timeout": "PT2M",
            "operations": [{ "name": "ping", "method": "GET", "path": "/ping" }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(TimeSpan.FromMinutes(2), Assert.Single(result.Model!.Connectors).Timeout);
    }

    [Theory]
    [InlineData("/orders/{idempotencyKey")]
    [InlineData("/orders/idempotencyKey}")]
    [InlineData("/orders/{{idempotencyKey}}")]
    [InlineData("/orders/{IdempotencyKey}")]
    [InlineData("/orders/{}")]
    public void A_path_with_an_unbalanced_brace_or_another_placeholder_is_invalid(string path)
    {
        using var folder = Folder().With("connectors/purchasing-system.json", Connector($$"""
            "operations": [{ "name": "ping", "method": "GET", "path": "{{path}}" }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidOperationPath, "/operations/0/path"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void A_repeated_output_field_name_is_a_duplicate_name()
    {
        using var folder = Folder().With("connectors/purchasing-system.json", Connector("""
            "operations": [{ "name": "ping", "method": "GET", "path": "/ping",
              "output": [{ "name": "status", "type": "text" }, { "name": "Status", "type": "text" }] }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.DuplicateConnectorName, "/operations/0/output/1/name"), (diagnostic.Code, diagnostic.Path));
    }

    [Theory]
    [InlineData("""{ "name": "find", "method": "GET", "path": "/orders/by-key", "output": [{ "name": "orderNumber", "type": "text", "required": true }] }""", "has no '{idempotencyKey}' in its path")]
    [InlineData("""{ "name": "find", "method": "GET", "path": "/orders/{idempotencyKey}", "output": [{ "name": "orderNumber", "type": "text" }] }""", "has other outputs")]
    [InlineData("""{ "name": "find", "method": "GET", "path": "/orders/{idempotencyKey}", "output": [{ "name": "orderNumber", "type": "integer", "required": true }] }""", "has other outputs")]
    [InlineData("""{ "name": "find", "method": "GET", "path": "/orders/{idempotencyKey}", "output": [] }""", "has other outputs")]
    public void A_lookup_that_cannot_serve_its_operation_is_invalid(string second, string reason)
    {
        using var folder = Folder().With("connectors/purchasing-system.json", Connector($$"""
            "operations": [
              { "name": "create", "method": "POST", "path": "/orders", "output": {{Output}}, "lookup": "find" },
              {{second}}
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCodes.InvalidOperationLookup && diagnostic.Message.Contains(reason, StringComparison.Ordinal));
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(DiagnosticCodes.InvalidOperationLookup, diagnostic.Code));
    }

    [Fact]
    public void A_lookup_naming_its_own_operation_is_invalid()
    {
        using var folder = Folder().With("connectors/purchasing-system.json", Connector("""
            "operations": [{ "name": "find", "method": "GET", "path": "/orders/{idempotencyKey}", "lookup": "Find" }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidOperationLookup, "/operations/0/lookup", "The lookup 'Find' names its own operation. Name another operation of this connector."),
            (diagnostic.Code, diagnostic.Path, diagnostic.Message));
    }

    [Fact]
    public void A_lookup_with_the_same_outputs_in_another_order_and_letter_case_is_valid()
    {
        using var folder = Folder().With("connectors/purchasing-system.json", Connector("""
            "operations": [
              { "name": "create", "method": "POST", "path": "/orders", "lookup": "find",
                "output": [{ "name": "orderNumber", "type": "text", "required": true }, { "name": "total", "type": "decimal" }] },
              { "name": "find", "method": "GET", "path": "/orders/{idempotencyKey}",
                "output": [{ "name": "TOTAL", "type": "decimal" }, { "name": "orderNumber", "type": "text", "required": true }] }
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("""{ "name": "find", "method": "GET", "path": "/orders", "input": [{ "name": "number", "type": "text" }] }""")]
    [InlineData("""{ "name": "find", "method": "HEAD", "path": "/orders" }""")]
    [InlineData("""{ "name": "find", "method": "GET", "path": "orders" }""")]
    [InlineData("""{ "name": "find", "method": "GET", "path": "/orders", "output": [{ "name": "number", "type": "enum" }] }""")]
    [InlineData("""{ "name": "find", "method": "GET", "path": "/orders", "retries": 3 }""")]
    public void An_operation_in_another_shape_is_a_schema_violation(string operation)
    {
        using var folder = Folder().With("connectors/purchasing-system.json", Connector($$"""
            "operations": [{{operation}}]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, "connectors/purchasing-system.json"), (diagnostic.Code, diagnostic.File)));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "type": "bearer" }""")]
    [InlineData("""{ "type": "bearer", "token": { "binding": "Token" }, "name": "X-Key" }""")]
    [InlineData("""{ "type": "header", "value": { "binding": "Key" } }""")]
    [InlineData("""{ "type": "header", "name": "X Key", "value": { "binding": "Key" } }""")]
    [InlineData("""{ "type": "none", "token": { "binding": "Token" } }""")]
    [InlineData("""{ "type": "basic" }""")]
    [InlineData("""{ "type": "bearer", "token": "secret-value" }""")]
    public void An_auth_in_another_shape_is_a_schema_violation(string auth)
    {
        using var folder = Folder().With("connectors/purchasing-system.json", Connector($$"""
            "auth": {{auth}},
            "operations": [{ "name": "ping", "method": "GET", "path": "/ping" }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, "connectors/purchasing-system.json"), (diagnostic.Code, diagnostic.File)));
    }

    [Theory]
    [InlineData(""" "baseUrl": "https://example.test", "operations": [{ "name": "ping", "method": "GET", "path": "/ping" }] """)]
    [InlineData(""" "baseUrl": { "binding": "Url" }, "operations": [] """)]
    [InlineData(""" "baseUrl": { "binding": "Url" }, "idempotency": "Idempotency Key", "operations": [{ "name": "ping", "method": "GET", "path": "/ping" }] """)]
    public void A_connector_in_another_shape_is_a_schema_violation(string body)
    {
        using var folder = Folder().With("connectors/purchasing-system.json", $$"""
            { "id": "{{ConnectorId}}", "kind": "connector", "name": "PurchasingSystem", "formatVersion": 1, {{body}} }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(DiagnosticCodes.SchemaViolation, diagnostic.Code));
    }

    private static string Connector(string body) => $$"""
        { "id": "{{ConnectorId}}", "kind": "connector", "name": "PurchasingSystem", "formatVersion": 1,
          "baseUrl": { "binding": "PurchasingSystemUrl" },
          {{body}} }
        """;

    private static TemporaryFolder Folder() =>
        new TemporaryFolder().With("application.json", PresentationCompilerTests.Manifest);
}
