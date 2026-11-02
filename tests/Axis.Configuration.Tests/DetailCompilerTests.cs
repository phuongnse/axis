using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using static Axis.Configuration.Tests.PresentationCompilerTests;

namespace Axis.Configuration.Tests;

public sealed class DetailCompilerTests
{
    private const string PageFile = "pages/order-detail.json";
    private const string PageId = "55555555-5555-4555-8555-555555555560";
    private const string FormId = "44444444-4444-4444-8444-444444444441";
    private const string ProcessId = "99999999-9999-4999-8999-999999999991";

    private const string TextsEn = """
        { "id": "33333333-3333-4333-8333-333333333333", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "site.title": "Sales", "nav.orders": "Orders", "orders.title": "Orders", "orderForm.title": "Order",
                     "customerForm.title": "Customer", "orderDetail.title": "Order detail", "orderSections.main": "Main",
                     "order.submit": "Submit" } }
        """;

    private const string Parameter = """{ "name": "order", "type": "reference", "target": "Order", "required": true }""";

    [Fact]
    public void Detail_over_an_entity_compiles_to_the_page_parameter_as_declared()
    {
        using var folder = Folder("""{ "type": "detail", "name": "detail", "entity": "Order", "record": "ORDER" }""", Parameter);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var widget = Assert.Single(Assert.Single(result.Model.Pages, page => page.Name == "OrderDetail").Widgets);
        Assert.Equal(WidgetType.Detail, widget.Type);
        Assert.Equal(new EntityReference(Guid.Parse("11111111-1111-4111-8111-111111111111"), "Order"), widget.Entity);
        Assert.Null(widget.Form);
        Assert.Null(widget.DataSource);
        Assert.Equal(new DetailModel("order"), widget.Detail);
        Assert.Empty(widget.Actions);
    }

    [Fact]
    public void Detail_over_a_form_compiles_to_the_form_and_its_entity()
    {
        using var folder = Folder("""{ "type": "detail", "name": "detail", "form": "OrderSections", "record": "order" }""", Parameter);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var widget = Assert.Single(Assert.Single(result.Model!.Pages, page => page.Name == "OrderDetail").Widgets);
        Assert.Equal(new FormReference(Guid.Parse(FormId), "OrderSections"), widget.Form);
        Assert.Equal("Order", widget.Entity!.Name);
        Assert.Equal(new DetailModel("order"), widget.Detail);
    }

    [Fact]
    public void Detail_with_actions_compiles_them_as_a_form_widget_does()
    {
        using var folder = Folder(
            """
            { "type": "detail", "name": "detail", "entity": "Order", "record": "order", "actions": [
                { "type": "startProcess", "name": "startProcess", "process": "OrderReview", "label": { "textKey": "order.submit" } },
                { "type": "navigate", "name": "navigate", "page": "OrderForm", "label": { "textKey": "orderForm.title" }, "record": "id" } ] }
            """,
            Parameter);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var widget = Assert.Single(Assert.Single(result.Model!.Pages, page => page.Name == "OrderDetail").Widgets);
        Assert.Collection(
            widget.Actions,
            action => Assert.Equal(new StartProcessActionModel(new ProcessReference(Guid.Parse(ProcessId), "OrderReview"), new TextReference("order.submit")), action),
            action => Assert.Equal("OrderForm", Assert.IsType<NavigateActionModel>(action).Page.Name));
    }

    [Fact]
    public void Other_widgets_have_no_detail()
    {
        using var folder = Folder("""{ "type": "detail", "name": "detail", "entity": "Order", "record": "order" }""", Parameter);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.All(
            result.Model!.Pages.Where(page => page.Name != "OrderDetail").SelectMany(page => page.Widgets),
            widget => Assert.Null(widget.Detail));
    }

    [Theory]
    [InlineData("""{ "name": "other", "type": "reference", "target": "Order", "required": true }""", "'order'")]
    [InlineData("""{ "name": "order", "type": "reference", "target": "Order" }""", "optional")]
    [InlineData("""{ "name": "order", "type": "reference", "target": "Order", "required": false }""", "optional")]
    [InlineData("""{ "name": "order", "type": "text", "required": true }""", "'text'")]
    [InlineData("""{ "name": "order", "type": "reference", "target": "Customer", "required": true }""", "'Customer'")]
    public void Record_that_is_no_required_reference_to_the_widgets_entity_is_reported_at_its_record(string parameter, string expected)
    {
        using var folder = Folder("""{ "type": "detail", "name": "detail", "entity": "Order", "record": "order" }""", parameter);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidDetailRecord, PageFile, "/widgets/0/record"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(expected, diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Record_aimed_at_another_entity_than_the_form_is_reported_at_its_record()
    {
        using var folder = Folder(
            """{ "type": "detail", "name": "detail", "form": "OrderSections", "record": "customer" }""",
            """{ "name": "customer", "type": "reference", "target": "Customer", "required": true }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidDetailRecord, PageFile, "/widgets/0/record"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Order'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "type": "detail", "name": "detail", "entity": "Order", "form": "OrderSections", "record": "order" }""")]
    [InlineData("""{ "type": "detail", "name": "detail", "record": "order" }""")]
    [InlineData("""{ "type": "detail", "name": "detail", "entity": "Order", "record": "order", "dataSource": "OrderList" }""")]
    public void Detail_that_does_not_name_exactly_one_of_form_and_entity_or_names_a_data_source_is_reported_as_an_invalid_binding(string widget)
    {
        using var folder = Folder(widget, Parameter);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.InvalidWidgetBinding, diagnostic.Code);
        Assert.Equal(PageFile, diagnostic.File);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "type": "detail", "name": "detail", "entity": "Order" }""")]
    [InlineData("""{ "type": "detail", "name": "detail", "entity": "Order", "record": "order", "parameters": {} }""")]
    [InlineData("""{ "type": "table", "name": "table", "entity": "Order", "record": "order" }""")]
    [InlineData("""{ "type": "form", "name": "form", "entity": "Order", "record": "order" }""")]
    public void Detail_without_a_record_or_another_widget_with_one_violates_the_page_schema(string widget)
    {
        using var folder = Folder(widget, Parameter);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, PageFile), (diagnostic.Code, diagnostic.File)));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Action_naming_a_process_over_another_entity_is_reported_at_its_process()
    {
        using var folder = Folder(
            """{ "type": "detail", "name": "detail", "entity": "Customer", "record": "customer", "actions": [ { "type": "startProcess", "name": "startProcess", "process": "OrderReview", "label": { "textKey": "order.submit" } } ] }""",
            """{ "name": "customer", "type": "reference", "target": "Customer", "required": true }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ActionProcessOverOtherEntity, PageFile, "/widgets/0/actions/0/process"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Navigate_to_an_unknown_page_is_reported_at_its_page()
    {
        using var folder = Folder(
            """{ "type": "detail", "name": "detail", "entity": "Order", "record": "order", "actions": [ { "type": "navigate", "name": "navigate", "page": "Missing", "label": { "textKey": "order.submit" } } ] }""",
            Parameter);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownNavigatePage, PageFile, "/widgets/0/actions/0/page"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    /// <summary>
    /// The presentation folder with the form <c>OrderSections</c>, the process <c>OrderReview</c> over orders and the
    /// page <c>OrderDetail</c> holding <paramref name="widgets"/> and declaring <paramref name="parameters"/>.
    /// </summary>
    private static TemporaryFolder Folder(string widgets, string? parameters) =>
        PresentationCompilerTests.Folder()
            .With("texts/en.json", TextsEn)
            .With("entities/order.json", """
                { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1, "displayField": "number",
                  "fields": [ { "name": "number", "type": "text", "required": true } ] }
                """)
            .With("entities/customer.json", """
                { "id": "22222222-2222-4222-8222-222222222222", "kind": "entity", "name": "Customer", "formatVersion": 1, "displayField": "name",
                  "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """)
            .With("forms/order-sections.json", $$"""
                { "id": "{{FormId}}", "kind": "form", "name": "OrderSections", "formatVersion": 1, "entity": "Order",
                  "sections": [ { "name": "main", "title": { "textKey": "orderSections.main" }, "fields": [ { "field": "number" } ] } ] }
                """)
            .With("processes/order-review.json", $$"""
                { "id": "{{ProcessId}}", "kind": "process", "name": "OrderReview", "formatVersion": 1,
                  "entity": "Order", "start": "done", "steps": [ { "name": "done", "type": "end" } ] }
                """)
            .With(PageFile, Page("OrderDetail", widgets, PageId, "orderDetail.title", parameters));
}
