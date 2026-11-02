using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using static Axis.Configuration.Tests.PresentationCompilerTests;

namespace Axis.Configuration.Tests;

public sealed class WidgetActionCompilerTests
{
    private const string ProcessId = "99999999-9999-4999-8999-999999999991";
    private const string PageFile = "pages/order-form.json";
    private const string OrdersFile = "pages/orders.json";

    private const string TextsEn = """
        { "id": "33333333-3333-4333-8333-333333333333", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "site.title": "Sales", "nav.orders": "Orders", "orders.title": "Orders", "orderForm.title": "Order",
                     "customerForm.title": "Customer", "order.submit": "Submit" } }
        """;

    [Fact]
    public void Form_widget_action_compiles_to_the_process_as_declared_and_its_label()
    {
        using var folder = Folder("Order")
            .With(PageFile, FormPage(Action("orderREVIEW")));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var widget = Assert.Single(Assert.Single(result.Model.Pages, page => page.Name == "OrderForm").Widgets);
        Assert.Equal(
            new WidgetActionModel[] { new StartProcessActionModel(new ProcessReference(Guid.Parse(ProcessId), "OrderReview"), new TextReference("order.submit")) },
            widget.Actions);
        var table = Assert.Single(Assert.Single(result.Model.Pages, page => page.Name == "Orders").Widgets);
        Assert.Empty(table.Actions);
    }

    [Fact]
    public void Action_naming_no_loaded_process_is_reported_at_its_process()
    {
        using var folder = Folder("Order")
            .With(PageFile, FormPage(Action("Missing")));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownActionProcess, PageFile, "/widgets/0/actions/0/process"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Missing'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Action_naming_a_process_that_failed_to_load_is_not_reported_again()
    {
        using var folder = Folder("Order")
            .With("processes/order-review.json", """{ "id": "99999999-9999-4999-8999-999999999991", "kind": "process", "name": "OrderReview", "formatVersion": 1 }""")
            .With(PageFile, FormPage(Action("OrderReview")));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("processes/order-review.json", diagnostic.File));
    }

    [Fact]
    public void Action_naming_a_process_without_a_subject_is_reported_at_its_process()
    {
        using var folder = Folder("Order")
            .With("processes/nightly-job.json", """
                { "id": "99999999-9999-4999-8999-999999999992", "kind": "process", "name": "NightlyJob", "formatVersion": 1,
                  "parameters": [{ "name": "days", "type": "integer" }],
                  "start": "done", "steps": [ { "name": "done", "type": "end" } ] }
                """)
            .With(PageFile, FormPage(Action("NightlyJob")));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ActionProcessOverOtherEntity, PageFile, "/widgets/0/actions/0/process"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("has no subject", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Action_naming_a_process_over_another_entity_is_reported_at_its_process()
    {
        using var folder = Folder("Customer")
            .With(PageFile, FormPage(Action("OrderReview")));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ActionProcessOverOtherEntity, PageFile, "/widgets/0/actions/0/process"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Customer'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'Order'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Action_with_a_label_key_no_locale_has_is_reported_at_its_label()
    {
        using var folder = Folder("Order")
            .With(PageFile, FormPage(Action("OrderReview", "order.missing")));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingTextKey, PageFile, "/widgets/0/actions/0/label/textKey"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "table", "type": "table", "entity": "Order", "actions": [ ACTION ] }""")]
    [InlineData("""{ "name": "taskInbox", "type": "taskInbox", "actions": [ ACTION ] }""")]
    public void Actions_on_a_widget_other_than_a_form_are_reported_at_its_actions(string widget)
    {
        using var folder = Folder("Order")
            .With("pages/orders.json", Page("Orders", widget.Replace("ACTION", ActionJson("OrderReview"), StringComparison.Ordinal)));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, "pages/orders.json", "/widgets/0/actions"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Table_row_action_compiles_to_the_process_as_declared_and_its_label()
    {
        using var folder = Folder("Order")
            .With(OrdersFile, OrdersPage(ActionJson("orderREVIEW")));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var widget = Assert.Single(Assert.Single(result.Model.Pages, page => page.File == OrdersFile).Widgets);
        Assert.Equal(
            new WidgetActionModel[] { new StartProcessActionModel(new ProcessReference(Guid.Parse(ProcessId), "OrderReview"), new TextReference("order.submit")) },
            widget.RowActions);
        Assert.Empty(widget.Actions);
    }

    [Fact]
    public void Row_action_naming_no_loaded_process_is_reported_at_its_process()
    {
        using var folder = Folder("Order")
            .With(OrdersFile, OrdersPage(ActionJson("Missing")));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownActionProcess, OrdersFile, "/widgets/0/rowActions/0/process"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Row_action_naming_a_process_over_another_entity_is_reported_at_its_process()
    {
        using var folder = Folder("Customer")
            .With(OrdersFile, OrdersPage(ActionJson("OrderReview")));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ActionProcessOverOtherEntity, OrdersFile, "/widgets/0/rowActions/0/process"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Customer'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'Order'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Start_process_action_in_toolbar_actions_is_a_schema_violation()
    {
        using var folder = Folder("Order")
            .With(OrdersFile, Page("Orders", $$"""{ "name": "table", "type": "table", "entity": "Order", "toolbarActions": [ {{ActionJson("OrderReview")}} ] }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.SchemaViolation && diagnostic.File == OrdersFile);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Table_delete_row_action_compiles_to_a_delete_action_with_its_label_and_visible_when()
    {
        using var folder = Folder("Order")
            .With(OrdersFile, OrdersPage(DeleteJson(visibleWhen: "id is not null")));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var widget = Assert.Single(Assert.Single(result.Model.Pages, page => page.File == OrdersFile).Widgets);
        Assert.Equal(
            new WidgetActionModel[] { new DeleteActionModel(new TextReference("order.submit")) { VisibleWhen = "id is not null" } },
            widget.RowActions);
        Assert.Empty(widget.Actions);
        Assert.Empty(widget.ToolbarActions);
    }

    [Fact]
    public void Delete_action_in_toolbar_actions_is_a_schema_violation()
    {
        using var folder = Folder("Order")
            .With(OrdersFile, Page("Orders", $$"""{ "name": "table", "type": "table", "entity": "Order", "toolbarActions": [ {{DeleteJson()}} ] }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.SchemaViolation && diagnostic.File == OrdersFile);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Delete_action_in_a_form_widgets_actions_is_a_schema_violation()
    {
        using var folder = Folder("Order")
            .With(PageFile, FormPage($$"""{ "name": "form", "type": "form", "entity": "Order", "actions": [ {{DeleteJson()}} ] }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.SchemaViolation && diagnostic.File == PageFile);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Delete_action_naming_a_process_is_a_schema_violation()
    {
        using var folder = Folder("Order")
            .With(OrdersFile, OrdersPage("""{ "name": "delete", "type": "delete", "process": "OrderReview", "label": { "textKey": "order.submit" } }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.SchemaViolation && diagnostic.File == OrdersFile);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Delete_action_with_a_label_key_no_locale_has_is_reported_at_its_label()
    {
        using var folder = Folder("Order")
            .With(OrdersFile, OrdersPage(DeleteJson("order.missing")));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingTextKey, OrdersFile, "/widgets/0/rowActions/0/label/textKey"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    /// <summary>The presentation folder with texts for the action label and the process <c>OrderReview</c> over <paramref name="processEntity"/>.</summary>
    private static TemporaryFolder Folder(string processEntity) =>
        PresentationCompilerTests.Folder()
            .With("texts/en.json", TextsEn)
            .With("processes/order-review.json", $$"""
                { "id": "{{ProcessId}}", "kind": "process", "name": "OrderReview", "formatVersion": 1,
                  "entity": "{{processEntity}}", "start": "done", "steps": [ { "name": "done", "type": "end" } ] }
                """);

    private static string ActionJson(string process, string labelKey = "order.submit") =>
        $$"""{ "name": "startProcess", "type": "startProcess", "process": "{{process}}", "label": { "textKey": "{{labelKey}}" } }""";

    private static string DeleteJson(string labelKey = "order.submit", string? visibleWhen = null)
    {
        var condition = visibleWhen is null ? "" : $", \"visibleWhen\": \"{visibleWhen}\"";
        return $$"""{ "name": "delete", "type": "delete", "label": { "textKey": "{{labelKey}}" }{{condition}} }""";
    }

    private static string Action(string process, string labelKey = "order.submit") =>
        $$"""{ "name": "form", "type": "form", "entity": "Order", "actions": [ {{ActionJson(process, labelKey)}} ] }""";

    private static string OrdersPage(string rowAction) =>
        Page("Orders", $$"""{ "name": "table", "type": "table", "entity": "Order", "rowActions": [ {{rowAction}} ] }""");

    private static string FormPage(string widget) =>
        Page("OrderForm", widget, "55555555-5555-4555-8555-555555555552", "orderForm.title");
}
