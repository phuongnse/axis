using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using static Axis.Configuration.Tests.PresentationCompilerTests;

namespace Axis.Configuration.Tests;

public sealed class FormCompilerTests
{
    private const string OrderId = "11111111-1111-4111-8111-111111111111";
    private const string FormId = "88888888-8888-4888-8888-888888888881";
    private const string FormFile = "forms/order-layout.json";

    // The order of the presentation folder with a second field, so a form can have two sections.
    private const string OrderWithNote = $$"""
        { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
          "fields": [ { "name": "number", "type": "text", "required": true }, { "name": "note", "type": "text" } ] }
        """;

    private const string TextsEn = """
        { "id": "33333333-3333-4333-8333-333333333333", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "site.title": "Sales", "nav.orders": "Orders", "orders.title": "Orders", "orderForm.title": "Order",
                     "customerForm.title": "Customer", "orderLayout.main": "Main", "orderLayout.details": "Details" } }
        """;

    private const string TwoSections = """
        [ { "title": { "textKey": "orderLayout.main" }, "fields": [ { "field": "NUMBER" } ] },
          { "title": { "textKey": "orderLayout.details" }, "fields": [ { "field": "note", "readOnly": true } ] } ]
        """;

    [Fact]
    public void Form_widget_naming_a_form_compiles_to_the_form_and_its_entity_and_a_table_may_open_it()
    {
        using var folder = Folder(Form("order", TwoSections))
            .With("pages/order-form.json", FormPage("""{ "type": "form", "form": "orderLAYOUT" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var widget = Assert.Single(Assert.Single(result.Model.Pages, page => page.Name == "OrderForm").Widgets);
        Assert.Equal(WidgetType.Form, widget.Type);
        Assert.Equal(new EntityReference(Guid.Parse(OrderId), "Order"), widget.Entity);
        Assert.Equal(new FormReference(Guid.Parse(FormId), "OrderLayout"), widget.Form);
        Assert.Null(widget.DataSource);
        Assert.Null(widget.FormPage);

        Assert.True(result.Model.TryGetForm("orderlayout", out var form));
        Assert.Equal(new EntityReference(Guid.Parse(OrderId), "Order"), form.Entity);
        Assert.Equal(FormFile, form.File);
        Assert.Equal(
            [("orderLayout.main", [new FormFieldModel("number", false)]), ("orderLayout.details", [new FormFieldModel("note", true)])],
            form.Sections.Select(section => (section.Title.TextKey, section.Fields.ToArray())));
    }

    [Fact]
    public void Form_widget_naming_an_entity_compiles_without_a_form()
    {
        using var folder = Folder(Form("Order", TwoSections));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var widget = Assert.Single(Assert.Single(result.Model.Pages, page => page.Name == "OrderForm").Widgets);
        Assert.Equal(new EntityReference(Guid.Parse(OrderId), "Order"), widget.Entity);
        Assert.Null(widget.Form);
    }

    [Fact]
    public void Form_over_an_unknown_entity_is_reported_at_its_entity_and_its_fields_are_not_checked()
    {
        using var folder = Folder(Form("Invoice", """[ { "title": { "textKey": "orderLayout.main" }, "fields": [ { "field": "missing" } ] } ]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownFormEntity, FormFile, "/entity"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Invoice'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse(FormId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Form_over_an_entity_file_that_failed_its_schema_reports_only_that_file()
    {
        using var folder = Folder(Form("Order", TwoSections))
            .With("entities/order.json", $$"""{ "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1 }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("entities/order.json", diagnostic.File));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Form_field_the_entity_does_not_have_is_reported_at_the_field()
    {
        using var folder = Folder(Form(
            "Order",
            """[ { "title": { "textKey": "orderLayout.main" }, "fields": [ { "field": "number" }, { "field": "total" } ] } ]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownFormField, FormFile, "/sections/0/fields/1/field"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'total'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Form_field_listed_again_in_any_section_and_letter_case_is_reported_at_the_repeat()
    {
        using var folder = Folder(Form(
            "Order",
            """
            [ { "title": { "textKey": "orderLayout.main" }, "fields": [ { "field": "number" }, { "field": "note" } ] },
              { "title": { "textKey": "orderLayout.details" }, "fields": [ { "field": "Number", "readOnly": true } ] } ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateFormField, FormFile, "/sections/1/fields/0/field"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'/sections/0/fields/0/field'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Section_title_key_no_locale_has_is_reported_at_the_title()
    {
        using var folder = Folder(Form(
            "Order",
            """
            [ { "title": { "textKey": "orderLayout.main" }, "fields": [ { "field": "number" } ] },
              { "title": { "textKey": "orderLayout.missing" }, "fields": [ { "field": "note" } ] } ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingTextKey, FormFile, "/sections/1/title/textKey"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'orderLayout.missing'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""[ { "fields": [ { "field": "number" } ] } ]""", "/sections/0")]
    [InlineData("""[ { "title": { "textKey": "orderLayout.main" }, "fields": [] } ]""", "/sections/0/fields")]
    [InlineData("""[ { "title": { "textKey": "orderLayout.main" }, "fields": [ { "field": "number", "readOnly": "yes" } ] } ]""", "/sections/0/fields/0/readOnly")]
    [InlineData("[]", "/sections")]
    public void Form_that_breaks_its_schema_is_reported_and_a_widget_naming_it_is_not(string sections, string path)
    {
        using var folder = Folder(Form("Order", sections))
            .With("pages/order-form.json", FormPage("""{ "type": "form", "form": "OrderLayout" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, FormFile, path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "type": "form", "form": "OrderLayout", "entity": "Order" }""", "/widgets/0")]
    [InlineData("""{ "type": "form" }""", "/widgets/0")]
    [InlineData("""{ "type": "table", "entity": "Order", "form": "OrderLayout" }""", "/widgets/0/form")]
    public void Widget_naming_both_or_neither_of_form_and_entity_or_a_table_naming_a_form_is_reported(string widget, string path)
    {
        using var folder = Folder(Form("Order", TwoSections))
            .With("pages/customers.json", Page("Customers", widget, "55555555-5555-4555-8555-555555555554"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, "pages/customers.json", path),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Widget_naming_no_loaded_form_is_reported_at_its_form()
    {
        using var folder = Folder(Form("Order", TwoSections))
            .With("pages/order-form.json", FormPage("""{ "type": "form", "form": "Missing" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownWidgetForm, "pages/order-form.json", "/widgets/0/form"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Missing'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Table_whose_form_page_shows_a_form_over_another_entity_is_reported_at_its_form_page()
    {
        using var folder = Folder(Form("Order", TwoSections))
            .With("forms/customer-layout.json", """
                { "id": "88888888-8888-4888-8888-888888888882", "kind": "form", "name": "CustomerLayout", "formatVersion": 1, "entity": "Customer",
                  "sections": [ { "title": { "textKey": "orderLayout.main" }, "fields": [ { "field": "name" } ] } ] }
                """)
            .With("pages/order-form.json", FormPage("""{ "type": "form", "form": "CustomerLayout" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidFormPage, "pages/orders.json", "/widgets/0/formPage"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Customer'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Unknown_kind_message_lists_form()
    {
        using var folder = Folder(Form("Order", TwoSections)).With("forms/other.json", """{ "kind": "layout" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.UnknownKind, diagnostic.Code);
        Assert.Contains($"'{ResourceKinds.Form}'", diagnostic.Message, StringComparison.Ordinal);
    }

    /// <summary>The presentation folder with an order that has a note, texts for the form and the given form.</summary>
    private static TemporaryFolder Folder(string form) =>
        PresentationCompilerTests.Folder()
            .With("entities/order.json", OrderWithNote)
            .With("texts/en.json", TextsEn)
            .With(FormFile, form);

    private static string Form(string entity, string sections) =>
        $$"""
        { "id": "{{FormId}}", "kind": "form", "name": "OrderLayout", "formatVersion": 1, "entity": "{{entity}}", "sections": {{sections}} }
        """;

    private static string FormPage(string widget) =>
        Page("OrderForm", widget, "55555555-5555-4555-8555-555555555552", "orderForm.title");
}
