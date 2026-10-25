using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class EnumValueLabelCompilerTests
{
    private const string Manifest = """
        { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1 }
        """;

    private const string OrderId = "11111111-1111-4111-8111-111111111111";

    private const string TextsJson = """ "priority.low": "Low", "priority.high": "High", "priority.mid": "Medium" """;

    [Fact]
    public void Complete_value_labels_compile_into_the_field_model()
    {
        using var folder = Folder("""
            { "name": "priority", "type": "enum", "values": ["low", "high"],
              "valueLabels": { "high": { "textKey": "priority.high" }, "low": { "textKey": "priority.low" } } }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.True(order.TryGetField("priority", out var priority));
        Assert.NotNull(priority.ValueLabels);
        Assert.Equal(
            [("high", "priority.high"), ("low", "priority.low")],
            priority.ValueLabels.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value.TextKey)));
    }

    [Fact]
    public void Enum_without_value_labels_has_none()
    {
        using var folder = Folder("""{ "name": "priority", "type": "enum", "values": ["low", "high"] }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.True(order.TryGetField("priority", out var priority));
        Assert.Null(priority.ValueLabels);
    }

    [Fact]
    public void Value_labels_keys_load_as_written_and_a_slash_in_a_value_is_escaped_in_the_path()
    {
        using var folder = Folder("""
            { "name": "priority", "type": "enum", "values": ["Low", "a/b"],
              "valueLabels": { "Low": { "textKey": "priority.low" }, "a/b": { "textKey": "priority.mid" }, "x/y": { "textKey": "priority.high" } } }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidValueLabels, "order.json", "/fields/0/valueLabels/x~1y", Guid.Parse(OrderId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Value_labels_that_miss_a_value_are_reported_once_at_the_map()
    {
        using var folder = Folder("""
            { "name": "number", "type": "text" },
            { "name": "priority", "type": "enum", "values": ["low", "mid", "high"],
              "valueLabels": { "mid": { "textKey": "priority.mid" } } }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidValueLabels, "order.json", "/fields/1/valueLabels", Guid.Parse(OrderId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'low', 'high'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Value_labels_are_compared_to_the_values_by_letter_case()
    {
        using var folder = Folder("""
            { "name": "priority", "type": "enum", "values": ["low"],
              "valueLabels": { "Low": { "textKey": "priority.low" } } }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [("/fields/0/valueLabels", DiagnosticCodes.InvalidValueLabels), ("/fields/0/valueLabels/Low", DiagnosticCodes.InvalidValueLabels)],
            result.Diagnostics.Select(diagnostic => (diagnostic.Path, diagnostic.Code)));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Value_label_for_an_unknown_value_is_reported_at_that_value()
    {
        using var folder = Folder("""
            { "name": "priority", "type": "enum", "values": ["low"],
              "valueLabels": { "low": { "textKey": "priority.low" }, "urgent": { "textKey": "missing.key" } } }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        // The text key of an unknown value is not checked.
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidValueLabels, "order.json", "/fields/0/valueLabels/urgent", Guid.Parse(OrderId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'urgent'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Value_labels_on_a_field_that_is_not_an_enum_are_reported_as_an_invalid_constraint_only()
    {
        using var folder = Folder("""
            { "name": "number", "type": "text", "valueLabels": { "low": { "textKey": "missing.key" } } }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidConstraint, "order.json", "/fields/0/valueLabels", Guid.Parse(OrderId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Value_labels_on_an_enum_without_values_are_reported_as_a_missing_type_property_only()
    {
        using var folder = Folder("""
            { "name": "priority", "type": "enum", "valueLabels": { "low": { "textKey": "priority.low" } } }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingTypeProperty, "order.json", "/fields/0"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Value_label_key_that_no_locale_has_is_reported_at_its_text_key()
    {
        using var folder = Folder("""
            { "name": "priority", "type": "enum", "values": ["low", "high"],
              "valueLabels": { "low": { "textKey": "priority.low" }, "high": { "textKey": "missing.key" } } }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingTextKey, "order.json", "/fields/0/valueLabels/high/textKey", Guid.Parse(OrderId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'missing.key'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Data_source_enum_parameter_with_value_labels_that_miss_a_value_is_reported_at_the_map()
    {
        using var folder = Folder("""{ "name": "number", "type": "text" }""")
            .With("data-sources/orders.json", DataSourceCompilerTests.DataSource(
                "Order",
                """[{ "name": "number", "path": "number" }]""",
                """
                , "parameters": [
                    { "name": "first", "type": "text" },
                    { "name": "p", "type": "enum", "values": ["low", "high"], "valueLabels": { "low": { "textKey": "priority.low" } } }
                  ]
                """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidValueLabels, "data-sources/orders.json", "/parameters/1/valueLabels", Guid.Parse("99999999-9999-4999-8999-999999999991")),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'high'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Data_source_enum_parameter_with_complete_value_labels_compiles_into_the_parameter_model()
    {
        using var folder = Folder("""{ "name": "number", "type": "text" }""")
            .With("data-sources/orders.json", DataSourceCompilerTests.DataSource(
                "Order",
                """[{ "name": "number", "path": "number" }]""",
                """
                , "parameters": [
                    { "name": "p", "type": "enum", "values": ["low", "high"],
                      "valueLabels": { "low": { "textKey": "priority.low" }, "high": { "textKey": "priority.high" } } }
                  ]
                """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var parameter = Assert.Single(Assert.Single(result.Model.DataSources).Parameters);
        Assert.NotNull(parameter.ValueLabels);
        Assert.Equal("priority.high", parameter.ValueLabels["high"].TextKey);
    }

    private static TemporaryFolder Folder(string fields) =>
        new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", $$"""{ "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [{{fields}}] }""")
            .With("texts/en.json", $$"""{ "id": "33333333-3333-4333-8333-333333333333", "kind": "text", "name": "Texts", "formatVersion": 1, "locale": "en", "texts": {{{TextsJson}}} }""");
}
