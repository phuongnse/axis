using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class FormConditionCompilerTests
{
    private const string FormId = "88888888-8888-4888-8888-888888888881";
    private const string FormFile = "forms/order-layout.json";

    // An order with a required number, an optional quantity and note, and a child collection of lines.
    private const string Order = """
        { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1,
          "fields": [ { "name": "number", "type": "text", "required": true },
                      { "name": "quantity", "type": "integer" },
                      { "name": "note", "type": "text" },
                      { "name": "lines", "type": "child-collection", "target": "OrderLine" } ] }
        """;

    private const string OrderLine = """
        { "id": "22222222-2222-4222-8222-222222222223", "kind": "entity", "name": "OrderLine", "formatVersion": 1,
          "fields": [ { "name": "amount", "type": "decimal" } ] }
        """;

    private const string TextsEn = """
        { "id": "33333333-3333-4333-8333-333333333333", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "site.title": "Sales", "nav.orders": "Orders", "orders.title": "Orders", "orderForm.title": "Order",
                     "customerForm.title": "Customer", "orderLayout.main": "Main", "orderLayout.details": "Details",
                     "orderLayout.first": "First", "orderLayout.second": "Second" } }
        """;

    private const string MainSection = """{ "name": "main", "title": { "textKey": "orderLayout.main" }, "fields": [ { "field": "number" }, { "field": "quantity" } ] }""";

    private const string DetailsSection = """{ "name": "details", "title": { "textKey": "orderLayout.details" }, "fields": [ { "field": "note" } ] }""";

    [Fact]
    public void Form_with_conditions_section_names_and_steps_compiles_and_the_model_holds_them()
    {
        using var folder = Folder($$"""
            "sections": [
              { "name": "main", "title": { "textKey": "orderLayout.main" },
                "fields": [ { "field": "number" }, { "field": "quantity" } ] },
              { "name": "Details", "title": { "textKey": "orderLayout.details" }, "visibleWhen": "quantity > 0",
                "fields": [ { "field": "note", "visibleWhen": "quantity > 1", "requiredWhen": "quantity is not null", "readOnlyWhen": "number == 'x'" },
                            { "field": "lines", "visibleWhen": "sum(lines, amount) > 0" } ] } ],
            "steps": [ { "title": { "textKey": "orderLayout.first" }, "sections": [ "MAIN" ] },
                       { "title": { "textKey": "orderLayout.second" }, "sections": [ "details" ] } ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetForm("OrderLayout", out var form));
        Assert.Equal(["main", "Details"], form.Sections.Select(section => section.Name));
        Assert.Null(form.Sections[0].VisibleWhen);
        Assert.Equal("quantity > 0", form.Sections[1].VisibleWhen?.Expression);
        var note = form.Sections[1].Fields[0];
        Assert.Equal(
            ["quantity > 1", "quantity is not null", "number == 'x'"],
            [note.VisibleWhen?.Expression, note.RequiredWhen?.Expression, note.ReadOnlyWhen?.Expression]);
        Assert.True(note.VisibleWhen!.Check.Succeeded);
        Assert.Null(form.Sections[0].Fields[0].VisibleWhen);
        Assert.Equal(
            [("orderLayout.first", "main"), ("orderLayout.second", "Details")],
            form.Steps.Select(step => (step.Title.TextKey, Assert.Single(step.Sections))));
    }

    [Fact]
    public void Form_without_steps_has_an_empty_step_list()
    {
        using var folder = Folder($$"""
            "sections": [ {{MainSection}} ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetForm("OrderLayout", out var form));
        Assert.Empty(form.Steps);
    }

    [Theory]
    [InlineData("visibleWhen")]
    [InlineData("requiredWhen")]
    [InlineData("readOnlyWhen")]
    public void Field_condition_that_is_not_boolean_is_reported_at_the_condition(string condition)
    {
        using var folder = Folder($$"""
            "sections": [ { "title": { "textKey": "orderLayout.main" },
              "fields": [ { "field": "number" }, { "field": "quantity", "{{condition}}": "number" } ] } ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.ResultTypeMismatch, FormFile, $"/sections/0/fields/1/{condition}"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Section_condition_that_names_an_unknown_field_is_reported_at_the_condition()
    {
        using var folder = Folder($$"""
            "sections": [ { "title": { "textKey": "orderLayout.main" }, "visibleWhen": "total > 0",
              "fields": [ { "field": "number" } ] } ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UnknownName, FormFile, "/sections/0/visibleWhen"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Condition_that_does_not_parse_is_reported_at_the_condition()
    {
        using var folder = Folder($$"""
            "sections": [ { "title": { "textKey": "orderLayout.main" },
              "fields": [ { "field": "number", "requiredWhen": "quantity >" } ] } ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(FormFile, diagnostic.File);
        Assert.Equal("/sections/0/fields/0/requiredWhen", diagnostic.Path);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "field": "number", "requiredWhen": "quantity > 0" }""", "/sections/0/fields/0/requiredWhen", "'quantity'")]
    [InlineData("""{ "field": "number", "readOnlyWhen": "not (QUANTITY is null)" }""", "/sections/0/fields/0/readOnlyWhen", "'QUANTITY'")]
    [InlineData("""{ "field": "number", "readOnlyWhen": "coalesce(quantity, 1) > 0" }""", "/sections/0/fields/0/readOnlyWhen", "'quantity'")]
    [InlineData("""{ "field": "number", "requiredWhen": "sum(lines, amount) > 0" }""", "/sections/0/fields/0/requiredWhen", "'lines'")]
    public void Condition_that_reads_a_field_the_form_does_not_list_is_reported_at_the_condition(string field, string path, string quoted)
    {
        using var folder = Folder($$"""
            "sections": [ { "title": { "textKey": "orderLayout.main" }, "fields": [ {{field}} ] } ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnlistedFormConditionField, FormFile, path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(quoted, diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Condition_may_read_a_field_listed_in_another_section_and_an_aggregate_over_a_listed_collection()
    {
        using var folder = Folder($$"""
            "sections": [
              { "title": { "textKey": "orderLayout.main" }, "fields": [ { "field": "number" }, { "field": "lines" } ] },
              { "title": { "textKey": "orderLayout.details" }, "visibleWhen": "sum(lines, amount) > 1 and number is not null",
                "fields": [ { "field": "note", "requiredWhen": "count(lines) > 0" } ] } ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void Visible_when_on_a_field_the_entity_requires_is_reported_at_the_condition()
    {
        using var folder = Folder($$"""
            "sections": [ { "title": { "textKey": "orderLayout.main" },
              "fields": [ { "field": "NUMBER", "visibleWhen": "quantity > 0" }, { "field": "quantity", "visibleWhen": "number is not null" } ] } ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.VisibleWhenOnRequiredField, FormFile, "/sections/0/fields/0/visibleWhen"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'NUMBER'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Required_when_and_read_only_when_are_allowed_on_a_field_the_entity_requires()
    {
        using var folder = Folder($$"""
            "sections": [ { "title": { "textKey": "orderLayout.main" },
              "fields": [ { "field": "number", "requiredWhen": "number is null", "readOnlyWhen": "number is not null" } ] } ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Section_name_used_again_ignoring_letter_case_is_reported_at_the_repeat()
    {
        using var folder = Folder($$"""
            "sections": [
              {{MainSection}},
              { "name": "MAIN", "title": { "textKey": "orderLayout.details" }, "fields": [ { "field": "note" } ] } ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateFormSectionName, FormFile, "/sections/1/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'/sections/0/name'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Step_naming_an_unknown_section_is_reported_at_the_name()
    {
        using var folder = Folder($$"""
            "sections": [ {{MainSection}}, {{DetailsSection}} ],
            "steps": [ { "title": { "textKey": "orderLayout.first" }, "sections": [ "main", "missing" ] },
                       { "title": { "textKey": "orderLayout.second" }, "sections": [ "details" ] } ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownStepSection, FormFile, "/steps/0/sections/1"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'missing'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""[ "main", "details" ]""", """, { "title": { "textKey": "orderLayout.second" }, "sections": [ "Main" ] }""", "/steps/1/sections/0", "'/steps/0/sections/0'")]
    [InlineData("""[ "main", "details", "details" ]""", "", "/steps/0/sections/2", "'/steps/0/sections/1'")]
    public void Section_in_two_steps_is_reported_at_the_second_place(string first, string secondStep, string path, string firstPath)
    {
        using var folder = Folder($$"""
            "sections": [ {{MainSection}}, {{DetailsSection}} ],
            "steps": [ { "title": { "textKey": "orderLayout.first" }, "sections": {{first}} }{{secondStep}} ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.DuplicateStepSection, FormFile, path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(firstPath, diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Section_in_no_step_and_an_unnamed_section_are_reported_at_the_section_when_steps_exist()
    {
        using var folder = Folder($$"""
            "sections": [
              {{MainSection}},
              {{DetailsSection}},
              { "title": { "textKey": "orderLayout.second" }, "fields": [ { "field": "lines" } ] } ],
            "steps": [ { "title": { "textKey": "orderLayout.first" }, "sections": [ "main" ] } ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [(DiagnosticCodes.SectionInNoStep, "/sections/1"), (DiagnosticCodes.SectionInNoStep, "/sections/2")],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
        Assert.Contains("'details'", result.Diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Unnamed_sections_are_fine_without_steps()
    {
        using var folder = Folder($$"""
            "sections": [ { "title": { "textKey": "orderLayout.main" }, "fields": [ { "field": "number" } ] } ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Step_title_key_no_locale_has_is_reported_at_the_title()
    {
        using var folder = Folder($$"""
            "sections": [ {{MainSection}} ],
            "steps": [ { "title": { "textKey": "orderLayout.missing" }, "sections": [ "main" ] } ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingTextKey, FormFile, "/steps/0/title/textKey"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Theory]
    [InlineData(""" "steps": [] """, "/steps")]
    [InlineData(""" "steps": [ { "title": { "textKey": "orderLayout.first" }, "sections": [] } ] """, "/steps/0/sections")]
    [InlineData(""" "steps": [ { "sections": [ "main" ] } ] """, "/steps/0")]
    [InlineData(""" "steps": [ { "title": { "textKey": "orderLayout.first" }, "sections": [ "main" ], "extra": 1 } ] """, "/steps/0/extra")]
    public void Steps_that_break_the_schema_are_reported(string steps, string path)
    {
        using var folder = Folder($$"""
            "sections": [ {{MainSection}} ], {{steps}}
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, FormFile, path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Empty_condition_breaks_the_schema()
    {
        using var folder = Folder($$"""
            "sections": [ { "title": { "textKey": "orderLayout.main" }, "fields": [ { "field": "number", "requiredWhen": "" } ] } ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, FormFile, "/sections/0/fields/0/requiredWhen"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    /// <summary>The presentation folder with an order that has a child collection, texts and a form with the given sections and steps.</summary>
    private static TemporaryFolder Folder(string sectionsAndSteps) =>
        PresentationCompilerTests.Folder()
            .With("entities/order.json", Order)
            .With("entities/order-line.json", OrderLine)
            .With("texts/en.json", TextsEn)
            .With(FormFile, $$"""
                { "id": "{{FormId}}", "kind": "form", "name": "OrderLayout", "formatVersion": 1, "entity": "Order", {{sectionsAndSteps}} }
                """);
}
