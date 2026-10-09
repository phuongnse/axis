using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;

namespace Axis.Configuration.Tests;

public sealed class PresentationCompilerTests
{
    internal const string Manifest = """
        { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1 }
        """;

    internal const string Order = """
        { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1,
          "fields": [ { "name": "number", "type": "text", "required": true } ] }
        """;

    private const string Customer = """
        { "id": "22222222-2222-4222-8222-222222222222", "kind": "entity", "name": "Customer", "formatVersion": 1,
          "fields": [ { "name": "name", "type": "text", "required": true } ] }
        """;

    private const string TextsEn = """
        { "id": "33333333-3333-4333-8333-333333333333", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "site.title": "Sales", "nav.orders": "Orders", "orders.title": "Orders", "orderForm.title": "Order", "customerForm.title": "Customer" } }
        """;

    private const string OrderListId = "77777777-7777-4777-8777-777777777771";

    private const string OrdersWidget = """{ "type": "table", "entity": "Order", "formPage": "OrderForm" }""";

    [Fact]
    public void Site_with_a_table_page_and_its_form_page_compiles_without_diagnostics()
    {
        using var folder = Folder();

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var orderForm = new PageReference(Guid.Parse("55555555-5555-4555-8555-555555555552"), "OrderForm");
        var orders = Assert.Single(result.Model.Pages, page => page.Name == "Orders");
        Assert.Equal(
            new WidgetModel(WidgetType.Table, new EntityReference(Guid.Parse("11111111-1111-4111-8111-111111111111"), "Order"), orderForm),
            Assert.Single(orders.Widgets));
        var site = Assert.Single(result.Model.Sites);
        Assert.Equal(new PageReference(orders.Id, "Orders"), Assert.Single(site.Navigation).Page);
    }

    [Fact]
    public void Widget_over_an_unknown_entity_is_reported_at_its_entity()
    {
        using var folder = Folder().With("pages/orders.json", Page("Orders", """{ "type": "table", "entity": "Invoice" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownWidgetEntity, "pages/orders.json", "/widgets/0/entity"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Invoice'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "type": "form", "entity": "Order", "formPage": "OrderForm" }""")]
    [InlineData("""{ "type": "table", "entity": "Order", "formPage": "Orders" }""")]
    [InlineData("""{ "type": "table", "entity": "Order", "formPage": "CustomerForm" }""")]
    [InlineData("""{ "type": "table", "entity": "Order", "formPage": "Missing" }""")]
    public void Form_page_on_a_form_widget_or_not_naming_a_form_page_of_the_same_entity_is_reported(string widget)
    {
        using var folder = Folder().With("pages/orders.json", Page("Orders", widget));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidFormPage, "pages/orders.json", "/widgets/0/formPage"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Table_bound_to_a_data_source_compiles_to_a_widget_with_the_data_source_and_no_entity()
    {
        using var folder = Folder()
            .With("data-sources/order-list.json", OrderList())
            .With("pages/orders.json", Page("Orders", """{ "type": "table", "dataSource": "orderList", "formPage": "OrderForm" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var orders = Assert.Single(result.Model.Pages, page => page.Name == "Orders");
        Assert.Equal(
            new WidgetModel(
                WidgetType.Table,
                null,
                new PageReference(Guid.Parse("55555555-5555-4555-8555-555555555552"), "OrderForm"),
                new DataSourceReference(Guid.Parse(OrderListId), "OrderList")),
            Assert.Single(orders.Widgets));
    }

    [Fact]
    public void Widget_over_an_unknown_data_source_is_reported_at_its_data_source()
    {
        using var folder = Folder().With("pages/orders.json", Page("Orders", """{ "type": "table", "dataSource": "Missing" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownWidgetDataSource, "pages/orders.json", "/widgets/0/dataSource"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Missing'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Widget_over_a_data_source_file_that_failed_its_schema_reports_only_that_file()
    {
        using var folder = Folder()
            .With("data-sources/order-list.json", $$"""{ "id": "{{OrderListId}}", "kind": "dataSource", "name": "OrderList", "formatVersion": 1, "entity": "Order" }""")
            .With("pages/orders.json", Page("Orders", """{ "type": "table", "dataSource": "OrderList" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "data-sources/order-list.json"), (diagnostic.Code, diagnostic.File));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "type": "table", "entity": "Order", "dataSource": "OrderList" }""", "/widgets/0")]
    [InlineData("""{ "type": "table" }""", "/widgets/0")]
    [InlineData("""{ "type": "form", "dataSource": "OrderList" }""", "/widgets/0/dataSource")]
    public void Widget_naming_both_or_neither_of_entity_and_data_source_or_a_form_over_a_data_source_is_reported(string widget, string path)
    {
        using var folder = Folder()
            .With("data-sources/order-list.json", OrderList())
            .With("pages/orders.json", Page("Orders", widget));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, "pages/orders.json", path),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Table_over_a_data_source_with_a_required_parameter_is_reported_naming_the_parameter()
    {
        using var folder = Folder()
            .With("data-sources/order-list.json", OrderList(
                """, "parameters": [{ "name": "numberFilter", "type": "text", "required": true }, { "name": "optional", "type": "text" }] """))
            .With("pages/orders.json", Page("Orders", """{ "type": "table", "dataSource": "OrderList" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, "pages/orders.json", "/widgets/0/dataSource"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'numberFilter'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Table_over_a_grouped_data_source_is_reported_at_its_data_source()
    {
        // A table builds its columns from the projected fields and opens rows by id, which a group lacks.
        using var folder = Folder()
            .With("data-sources/order-list.json", OrderList(
                """, "aggregate": { "groupBy": ["number"], "measures": [{ "name": "orders", "function": "count" }] } """))
            .With("pages/orders.json", Page("Orders", """{ "type": "table", "dataSource": "OrderList" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, "pages/orders.json", "/widgets/0/dataSource"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("grouped", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Form_page_of_a_data_source_table_over_another_entity_than_its_root_is_reported()
    {
        using var folder = Folder()
            .With("data-sources/order-list.json", OrderList())
            .With("pages/orders.json", Page("Orders", """{ "type": "table", "dataSource": "OrderList", "formPage": "CustomerForm" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidFormPage, "pages/orders.json", "/widgets/0/formPage"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Order'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Form_page_naming_a_page_file_that_failed_its_schema_reports_only_that_file()
    {
        using var folder = Folder()
            .With("pages/order-form.json", """
                { "id": "55555555-5555-4555-8555-555555555552", "kind": "page", "name": "OrderForm", "formatVersion": 1,
                  "title": { "textKey": "orderForm.title" }, "widgets": [] }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.SchemaViolation, "pages/order-form.json", "/widgets"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Navigation_entry_to_an_unknown_page_is_reported_at_its_page()
    {
        using var folder = Folder().With("sites/sales.json", Site(navigationPage: "Missing"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownNavigationPage, "sites/sales.json", "/navigation/0/page"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Missing'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("health")]
    [InlineData("assets")]
    public void Site_path_reserved_by_the_platform_is_reported_at_the_path(string path)
    {
        using var folder = Folder().With("sites/sales.json", Site(path: path));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidSitePath, "sites/sales.json", "/path"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("reserved", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Site_path_used_by_an_earlier_site_is_reported_on_the_later_file()
    {
        using var folder = Folder().With("sites/second.json", Site(name: "Second", id: "66666666-6666-4666-8666-666666666662"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidSitePath, "sites/second.json", "/path"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'sites/sales.json'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Two_sites_with_a_reserved_path_each_get_one_diagnostic()
    {
        using var folder = Folder()
            .With("sites/sales.json", Site(path: "api"))
            .With("sites/second.json", Site(name: "Second", id: "66666666-6666-4666-8666-666666666662", path: "api"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [(DiagnosticCodes.InvalidSitePath, "sites/sales.json", "/path"), (DiagnosticCodes.InvalidSitePath, "sites/second.json", "/path")],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData(""" "default": "vi", "fallback": "en", "available": ["en"] """, "/locales/default")]
    [InlineData(""" "default": "en", "fallback": "vi", "available": ["en"] """, "/locales/fallback")]
    [InlineData(""" "default": "en", "fallback": "en", "available": ["en", "fr"] """, "/locales/available/1")]
    public void Site_locale_not_available_or_without_texts_is_reported_at_the_locale(string locales, string path)
    {
        using var folder = Folder().With("sites/sales.json", Site(locales: locales));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidSiteLocale, "sites/sales.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Site_locales_match_text_locales_ignoring_letter_case()
    {
        using var folder = Folder().With("sites/sales.json", Site(locales: """ "default": "EN", "fallback": "En", "available": ["eN"] """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Theory]
    [InlineData("sites/sales.json", "/title/textKey")]
    [InlineData("sites/sales.json", "/navigation/0/label/textKey")]
    [InlineData("pages/orders.json", "/title/textKey")]
    public void Site_title_navigation_label_or_page_title_with_a_missing_text_key_is_reported(string file, string path)
    {
        var content = (file, path) switch
        {
            ("pages/orders.json", _) => Page("Orders", OrdersWidget, titleKey: "missing.key"),
            (_, "/title/textKey") => Site(titleKey: "missing.key"),
            _ => Site(navigationKey: "missing.key"),
        };
        using var folder = Folder().With(file, content);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.MissingTextKey, file, path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'missing.key'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    /// <summary>A folder that compiles: a site whose navigation opens a table page of orders, and form pages for orders and customers.</summary>
    private static TemporaryFolder Folder() =>
        new TemporaryFolder()
            .With("application.json", Manifest)
            .With("entities/order.json", Order)
            .With("entities/customer.json", Customer)
            .With("texts/en.json", TextsEn)
            .With("sites/sales.json", Site())
            .With("pages/orders.json", Page("Orders", OrdersWidget))
            .With("pages/order-form.json", Page("OrderForm", """{ "type": "form", "entity": "Order" }""", "55555555-5555-4555-8555-555555555552", "orderForm.title"))
            .With("pages/customer-form.json", Page("CustomerForm", """{ "type": "form", "entity": "Customer" }""", "55555555-5555-4555-8555-555555555553", "customerForm.title"));

    private static string Site(
        string name = "Sales",
        string id = "66666666-6666-4666-8666-666666666661",
        string path = "sales",
        string titleKey = "site.title",
        string locales = """ "default": "en", "fallback": "en", "available": ["en"] """,
        string navigationPage = "Orders",
        string navigationKey = "nav.orders") =>
        $$"""
        { "id": "{{id}}", "kind": "site", "name": "{{name}}", "formatVersion": 1, "path": "{{path}}",
          "title": { "textKey": "{{titleKey}}" }, "locales": { {{locales}} },
          "navigation": [ { "page": "{{navigationPage}}", "label": { "textKey": "{{navigationKey}}" } } ] }
        """;

    private static string Page(string name, string widget, string id = "55555555-5555-4555-8555-555555555551", string titleKey = "orders.title") =>
        $$"""
        { "id": "{{id}}", "kind": "page", "name": "{{name}}", "formatVersion": 1,
          "title": { "textKey": "{{titleKey}}" }, "widgets": [ {{widget}} ] }
        """;

    private static string OrderList(string extra = "") =>
        $$"""
        { "id": "{{OrderListId}}", "kind": "dataSource", "name": "OrderList", "formatVersion": 1, "entity": "Order",
          "fields": [ { "name": "number", "path": "number" } ]{{extra}} }
        """;
}
