using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Resources;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class ValidationCompilerTests
{
    private const string OrderId = "11111111-1111-4111-8111-111111111111";

    private const string Texts = """
        { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "order.quantityPositive": "Quantity must be greater than zero." } }
        """;

    public static TheoryData<string, string, string, string> InvalidValidations => new()
    {
        { """{ "name": "check1", "expression": "quantity > 'a'", "message": { "textKey": "order.quantityPositive" }, "field": "quantity" }""", ExpressionDiagnosticCodes.TypeMismatch, "/validations/0/expression", "character" },
        { """{ "name": "check2", "expression": "quantity > 0 and date('2026-13-45') is null", "message": { "textKey": "order.quantityPositive" }, "field": "quantity" }""", ExpressionDiagnosticCodes.TypeMismatch, "/validations/0/expression", "'2026-13-45' is not a valid date at character 18." },
        { """{ "name": "check3", "expression": "quantity + 1", "message": { "textKey": "order.quantityPositive" }, "field": "quantity" }""", ExpressionDiagnosticCodes.ResultTypeMismatch, "/validations/0/expression", "boolean" },
        { """{ "name": "check4", "expression": "quantity >", "message": { "textKey": "order.quantityPositive" }, "field": "quantity" }""", ExpressionDiagnosticCodes.SyntaxError, "/validations/0/expression", "character" },
        { """{ "name": "check5", "expression": "lines is null", "message": { "textKey": "order.quantityPositive" }, "field": "quantity" }""", ExpressionDiagnosticCodes.TypeMismatch, "/validations/0/expression", "list<OrderLine>" },
        { """{ "name": "check6", "expression": "lines == null", "message": { "textKey": "order.quantityPositive" }, "field": "lines" }""", ExpressionDiagnosticCodes.TypeMismatch, "/validations/0/expression", "list<OrderLine>" },
        { """{ "name": "check7", "expression": "count(quantity) > 0", "message": { "textKey": "order.quantityPositive" }, "field": "quantity" }""", ExpressionDiagnosticCodes.TypeMismatch, "/validations/0/expression", "child collection" },
        { """{ "name": "check8", "expression": "sum(lines) > 0", "message": { "textKey": "order.quantityPositive" }, "field": "lines" }""", ExpressionDiagnosticCodes.WrongArgumentCount, "/validations/0/expression", "'sum'" },
        { """{ "name": "check9", "expression": "all(lines, quantity > 0)", "message": { "textKey": "order.quantityPositive" }, "field": "lines" }""", ExpressionDiagnosticCodes.UnknownName, "/validations/0/expression", "'quantity'" },
        // Only data source filters and process conditions follow paths.
        { """{ "name": "check10", "expression": "customer.name == 'x'", "message": { "textKey": "order.quantityPositive" }, "field": "customer" }""", ExpressionDiagnosticCodes.UnknownName, "/validations/0/expression", "Unknown field 'name' after '.'" },
        { """{ "name": "check11", "expression": "receipt == null", "message": { "textKey": "order.quantityPositive" }, "field": "receipt" }""", ExpressionDiagnosticCodes.TypeMismatch, "/validations/0/expression", "A file allows only 'is null' and 'is not null'" },
        { """{ "name": "check12", "expression": "quantity > 0", "message": { "textKey": "order.quantityPositive" }, "field": "nope" }""", DiagnosticCodes.UnknownValidationField, "/validations/0/field", "'nope'" },
        { """{ "name": "check13", "expression": "quantity > 0", "message": { "textKey": "order.missing" }, "field": "quantity" }""", DiagnosticCodes.MissingTextKey, "/validations/0/message/textKey", "'order.missing'" },
        { """{ "expression": "quantity > 0", "message": { "textKey": "order.quantityPositive" }, "field": "quantity" }""", DiagnosticCodes.SchemaViolation, "/validations/0", "" },
    };

    [Theory]
    [MemberData(nameof(InvalidValidations))]
    public void Invalid_validation_is_reported_at_its_path_and_gives_no_model(string validation, string code, string path, string messagePart)
    {
        using var folder = Folder($"[{validation}]");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "entities/order.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(messagePart, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse(OrderId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Validation_names_that_differ_only_in_letter_case_are_reported_at_the_later_name()
    {
        using var folder = Folder("""
            [
              { "name": "positive", "expression": "quantity > 0", "message": { "textKey": "order.quantityPositive" }, "field": "quantity" },
              { "name": "Positive", "expression": "quantity < 100", "message": { "textKey": "order.quantityPositive" }, "field": "quantity" }
            ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateEntityElementName, "entities/order.json", "/validations/1/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'/validations/0'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Every_invalid_validation_is_reported_at_its_own_index()
    {
        using var folder = Folder("""
            [
              { "name": "check14", "expression": "quantity", "message": { "textKey": "order.quantityPositive" }, "field": "quantity" },
              { "name": "check15", "expression": "quantity > 0", "message": { "textKey": "order.quantityPositive" }, "field": "quantity" },
              { "name": "check16", "expression": "status == 'lost'", "message": { "textKey": "order.quantityPositive" }, "field": "status" }
            ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (ExpressionDiagnosticCodes.ResultTypeMismatch, "/validations/0/expression"),
                (ExpressionDiagnosticCodes.UnknownEnumValue, "/validations/2/expression"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Valid_validation_is_in_the_entity_model_and_changes_the_content_hash()
    {
        using var withoutValidations = Folder(null);
        // The field resolves ignoring letter case, and the model holds its declared name.
        using var withValidations = Folder("""
            [{ "name": "check17", "expression": "quantity is null or quantity > 0", "message": { "textKey": "order.quantityPositive" }, "field": "Quantity" }]
            """);

        var before = ApplicationCompiler.Compile(withoutValidations.Path);
        var result = ApplicationCompiler.Compile(withValidations.Path);

        Assert.Empty(before.Diagnostics);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        var validation = Assert.Single(order.Validations);
        Assert.Equal(
            ("quantity is null or quantity > 0", "quantity", new TextReference("order.quantityPositive")),
            (validation.Expression, validation.Field, validation.Message));
        Assert.True(validation.Check.Succeeded);
        Assert.NotNull(before.ContentHash);
        Assert.NotNull(result.ContentHash);
        Assert.NotEqual(before.ContentHash, result.ContentHash);
    }

    [Fact]
    public void Validation_can_aggregate_over_a_child_collection_and_name_it_as_its_field()
    {
        using var folder = Folder("""
            [
              { "name": "check18", "expression": "count(lines) >= 1", "message": { "textKey": "order.quantityPositive" }, "field": "lines" },
              { "name": "check19", "expression": "all(lines, amount > 0) and sum(lines, total) < 1000", "message": { "textKey": "order.quantityPositive" }, "field": "Lines" }
            ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.Equal(["lines", "lines"], order.Validations.Select(validation => validation.Field));
        Assert.All(order.Validations, validation => Assert.True(validation.Check.Succeeded));
    }

    [Theory]
    [InlineData("receipt is not null")]
    [InlineData("receipt is null or quantity > 0")]
    public void Validation_can_test_a_file_field_for_null(string expression)
    {
        using var folder = Folder($$"""
            [{ "name": "check20", "expression": "{{expression}}", "message": { "textKey": "order.quantityPositive" }, "field": "receipt" }]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(Assert.Single(result.Model.Entities.Single(entity => entity.Name == "Order").Validations).Check.Succeeded);
    }

    [Fact]
    public void Validation_can_compare_a_field_with_the_current_time()
    {
        using var folder = Folder("""
            [{ "name": "check21", "expression": "dueAt is null or dueAt > now()", "message": { "textKey": "order.quantityPositive" }, "field": "dueAt" }]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.True(Assert.Single(order.Validations).Check.Succeeded);
    }

    private static TemporaryFolder Folder(string? validations) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", Texts)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    { "name": "quantity", "type": "integer" },
                    { "name": "dueAt", "type": "date-time" },
                    { "name": "status", "type": "enum", "values": ["open", "closed"] },
                    { "name": "lines", "type": "child-collection", "target": "OrderLine" },
                    { "name": "customer", "type": "reference", "target": "Customer" },
                    { "name": "receipt", "type": "file" }
                  ]{{(validations is null ? "" : $", \"validations\": {validations}")}} }
                """)
            .With("entities/order-line.json", """
                { "id": "22222222-2222-4222-8222-222222222222", "kind": "entity", "name": "OrderLine", "formatVersion": 1,
                  "fields": [
                    { "name": "description", "type": "text" },
                    { "name": "amount", "type": "decimal" },
                    { "name": "total", "type": "decimal", "expression": "amount * 2" }
                  ] }
                """)
            .With("entities/customer.json", """
                { "id": "33333333-3333-4333-8333-333333333333", "kind": "entity", "name": "Customer", "formatVersion": 1,
                  "displayField": "name", "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """);
}
