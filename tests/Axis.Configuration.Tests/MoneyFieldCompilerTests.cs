using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;

namespace Axis.Configuration.Tests;

public sealed class MoneyFieldCompilerTests
{
    private const string Manifest = """
        { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1 }
        """;

    private const string Fields = """
        { "name": "count", "type": "integer" },
        { "name": "currency", "type": "enum", "values": ["USD", "EUR"] },
        { "name": "code", "type": "text" }
        """;

    [Theory]
    [InlineData("""{ "name": "amount", "type": "money" }""", "/fields/0")]
    [InlineData("""{ "name": "amount", "type": "money", "currency": "VND", "currencyField": "currency" }""", "/fields/0/currencyField")]
    [InlineData("""{ "name": "amount", "type": "money", "currency": "ABC" }""", "/fields/0/currency")]
    [InlineData("""{ "name": "amount", "type": "money", "currency": "vnd" }""", "/fields/0/currency")]
    [InlineData("""{ "name": "amount", "type": "money", "currency": "XAU" }""", "/fields/0/currency")]
    [InlineData("""{ "name": "amount", "type": "money", "currencyField": "count" }""", "/fields/0/currencyField")]
    [InlineData("""{ "name": "amount", "type": "money", "currencyField": "missing" }""", "/fields/0/currencyField")]
    public void Money_with_a_wrong_currency_setup_is_reported(string field, string path)
    {
        var result = Compile(field);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidMoneyCurrency, "entities/expense.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "amount", "type": "money", "currencyField": "currency" }""")]
    [InlineData("""{ "name": "amount", "type": "money", "currencyField": "CODE" }""")]
    [InlineData("""{ "name": "amount", "type": "money", "currency": "KWD" }""")]
    public void Money_with_one_valid_currency_compiles(string field)
    {
        var result = Compile(field);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void Currency_on_a_decimal_field_is_an_invalid_constraint()
    {
        var result = Compile("""{ "name": "amount", "type": "decimal", "currency": "VND" }""");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidConstraint, "/fields/0/currency"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void Currency_field_on_a_text_field_is_an_invalid_constraint()
    {
        var result = Compile("""{ "name": "amount", "type": "text", "currencyField": "currency" }""");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidConstraint, "/fields/0/currencyField"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void Compiled_money_field_holds_the_default_precision_and_the_scale_of_its_currency()
    {
        var result = Compile(
            """
            { "name": "vnd", "type": "money", "currency": "VND" },
            { "name": "usd", "type": "money", "currency": "USD" },
            { "name": "kwd", "type": "money", "currency": "KWD" },
            { "name": "other", "type": "money", "currencyField": "CODE" }
            """);

        var entity = Assert.Single(result.Model!.Entities);
        Assert.Equal(
            new (string, FieldType, int, int, string?, string?)[]
            {
                ("vnd", FieldType.Money, 18, 0, "VND", null),
                ("usd", FieldType.Money, 18, 2, "USD", null),
                ("kwd", FieldType.Money, 18, 3, "KWD", null),
                ("other", FieldType.Money, 18, 2, null, "code"),
            },
            entity.Fields.Where(field => field.Type == FieldType.Money)
                .Select(field => (field.Name, field.Type, field.Precision!.Value, field.Scale!.Value, field.Currency, field.CurrencyField)));
    }

    [Fact]
    public void Compiled_money_field_keeps_an_explicit_precision_and_scale()
    {
        var result = Compile("""{ "name": "amount", "type": "money", "currency": "VND", "precision": 12, "scale": 4 }""");

        var field = Assert.Single(result.Model!.Entities).Fields[0];
        Assert.Equal((12, 4), (field.Precision, field.Scale));
    }

    [Fact]
    public void Money_scale_can_be_set_without_precision()
    {
        var result = Compile("""{ "name": "amount", "type": "money", "currency": "VND", "scale": 5 }""");

        var field = Assert.Single(result.Model!.Entities).Fields[0];
        Assert.Equal((18, 5), (field.Precision, field.Scale));
    }

    [Theory]
    [InlineData("""{ "name": "amount", "type": "money", "currency": "USD", "precision": 1 }""", "/fields/0/precision")]
    [InlineData("""{ "name": "amount", "type": "money", "currency": "USD", "precision": 3, "scale": 4 }""", "/fields/0/scale")]
    [InlineData("""{ "name": "amount", "type": "money", "currency": "USD", "scale": 19 }""", "/fields/0/scale")]
    public void Money_scale_larger_than_the_precision_is_an_invalid_constraint(string field, string path)
    {
        var result = Compile(field);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidConstraint, path), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void Validation_over_a_money_field_treats_it_as_a_decimal()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With(
                "texts/en.json",
                """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "expense.amountPositive": "Amount must be greater than zero." } }
                """)
            .With(
                "entities/expense.json",
                """
                {
                  "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Expense", "formatVersion": 1,
                  "fields": [{ "name": "amount", "type": "money", "currency": "VND" }],
                  "validations": [{ "field": "amount", "expression": "amount > 0", "message": { "textKey": "expense.amountPositive" } }]
                }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    private static CompilationResult Compile(string field)
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With(
                "entities/expense.json",
                $$"""
                { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Expense", "formatVersion": 1, "fields": [{{field}}, {{Fields}}] }
                """);
        return ApplicationCompiler.Compile(folder.Path);
    }
}
