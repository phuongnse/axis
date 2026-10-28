using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;

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

    internal const string Client = """
        { "id": "22222222-2222-4222-8222-222222222223", "kind": "entity", "name": "Client", "formatVersion": 1, "displayField": "name",
          "fields": [ { "name": "name", "type": "text", "required": true } ] }
        """;

    private const string TextsEn = """
        { "id": "33333333-3333-4333-8333-333333333333", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "site.title": "Sales", "nav.orders": "Orders", "orders.title": "Orders", "orderForm.title": "Order", "customerForm.title": "Customer" } }
        """;

    private const string OrderListId = "77777777-7777-4777-8777-777777777771";

    // The buttons reuse text keys that every fixture of these tests has.
    private const string OrdersWidget = """
        { "type": "table", "entity": "Order",
          "rowActions": [ { "type": "navigate", "page": "OrderForm", "label": { "textKey": "orders.title" }, "record": "id" } ],
          "toolbarActions": [ { "type": "navigate", "page": "OrderForm", "label": { "textKey": "orderForm.title" } } ] }
        """;

    [Fact]
    public void Site_with_a_table_page_that_opens_its_form_page_compiles_without_diagnostics()
    {
        using var folder = Folder();

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var orderForm = new PageReference(Guid.Parse("55555555-5555-4555-8555-555555555552"), "OrderForm");
        var orders = Assert.Single(result.Model.Pages, page => page.Name == "Orders");
        var table = Assert.Single(orders.Widgets);
        Assert.Equal(
            (WidgetType.Table, new EntityReference(Guid.Parse("11111111-1111-4111-8111-111111111111"), "Order")),
            (table.Type, table.Entity));
        Assert.Equal(
            [new NavigateActionModel(orderForm, new TextReference("orders.title")) { Record = "id" }],
            table.RowActions);
        Assert.Equal([new NavigateActionModel(orderForm, new TextReference("orderForm.title"))], table.ToolbarActions);
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

    [Fact]
    public void Table_widget_naming_the_removed_form_page_property_is_a_schema_violation()
    {
        // The property name is built at run time so that no file under tests spells it out.
        var removed = "form" + "Page";
        using var folder = Folder().With("pages/orders.json", Page("Orders", $$"""{ "type": "table", "entity": "Order", "{{removed}}": "OrderForm" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.SchemaViolation, "pages/orders.json", $"/widgets/0/{removed}"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Table_bound_to_a_data_source_compiles_to_a_widget_with_the_data_source_and_no_entity()
    {
        using var folder = Folder()
            .With("data-sources/order-list.json", OrderList())
            .With("pages/orders.json", Page("Orders", """{ "type": "table", "dataSource": "orderList" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var orders = Assert.Single(result.Model.Pages, page => page.Name == "Orders");
        Assert.Equal(
            new WidgetModel(
                WidgetType.Table,
                null,
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
    public void Task_inbox_with_no_binding_compiles_to_a_widget_with_no_references()
    {
        using var folder = Folder().With("pages/orders.json", Page("Orders", """{ "type": "taskInbox" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var orders = Assert.Single(result.Model.Pages, page => page.Name == "Orders");
        Assert.Equal(new WidgetModel(WidgetType.TaskInbox, null), Assert.Single(orders.Widgets));
    }

    [Theory]
    [InlineData("entity", "\"Order\"", DiagnosticCodes.InvalidWidgetBinding)]
    [InlineData("dataSource", "\"OrderList\"", DiagnosticCodes.InvalidWidgetBinding)]
    [InlineData("form", "\"OrderEdit\"", DiagnosticCodes.InvalidWidgetBinding)]
    public void Task_inbox_naming_a_binding_is_reported_at_that_property(string property, string value, string code)
    {
        using var folder = Folder()
            .With("data-sources/order-list.json", OrderList())
            .With("pages/orders.json", Page("Orders", $$"""{ "type": "taskInbox", "{{property}}": {{value}} }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "pages/orders.json", $"/widgets/0/{property}"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains($"'{property}'", diagnostic.Message, StringComparison.Ordinal);
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

    private const string BoundTable = """{ "type": "table", "dataSource": "OrderList", "parameters": { "numberFilter": "number" } }""";

    private static TemporaryFolder BoundFolder(string dataSourceParameters, string pageParameters, string widget = BoundTable) =>
        Folder()
            .With("entities/client.json", Client)
            .With("data-sources/order-list.json", OrderList($$""", "parameters": [ {{dataSourceParameters}} ] """))
            .With("pages/orders.json", Page("Orders", widget, parameters: pageParameters));

    [Fact]
    public void Table_binding_a_required_parameter_to_a_required_page_parameter_compiles_with_the_declared_names()
    {
        using var folder = BoundFolder(
            """{ "name": "numberFilter", "type": "text", "required": true }""",
            """{ "name": "Number", "type": "text", "required": true }""",
            """{ "type": "table", "dataSource": "OrderList", "parameters": { "NUMBERFILTER": "number" } }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var widget = Assert.Single(Assert.Single(result.Model!.Pages, page => page.Name == "Orders").Widgets);
        Assert.Equal(new Dictionary<string, string> { ["numberFilter"] = "Number" }, widget.Parameters);
    }

    [Theory]
    [InlineData("""{ "name": "number", "type": "integer", "required": true }""")]
    [InlineData("""{ "name": "number", "type": "reference", "target": "Client", "required": true }""")]
    public void Table_binding_a_page_parameter_of_another_type_is_reported_at_the_binding(string pageParameter)
    {
        using var folder = BoundFolder(
            """{ "name": "numberFilter", "type": "text", "required": true }""",
            pageParameter);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, "pages/orders.json", "/widgets/0/parameters/numberFilter"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Table_binding_a_reference_parameter_to_a_page_parameter_with_another_target_is_reported()
    {
        using var folder = BoundFolder(
            """{ "name": "numberFilter", "type": "reference", "target": "Client", "required": true }""",
            """{ "name": "number", "type": "reference", "target": "Vendor", "required": true }""")
            .With("entities/vendor.json", Client.Replace("22222222-2222-4222-8222-222222222223", "22222222-2222-4222-8222-222222222224", StringComparison.Ordinal).Replace("Client", "Vendor", StringComparison.Ordinal));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, "/widgets/0/parameters/numberFilter"),
            (diagnostic.Code, diagnostic.Path));
    }

    [Theory]
    [InlineData("""["new", "open"]""", true)]
    [InlineData("""["new", "closed"]""", false)]
    public void Table_binding_an_enum_page_parameter_needs_its_values_to_be_values_of_the_data_source_parameter(string pageValues, bool compiles)
    {
        using var folder = BoundFolder(
            """{ "name": "numberFilter", "type": "enum", "values": ["new", "open", "done"], "required": true }""",
            $$"""{ "name": "number", "type": "enum", "values": {{pageValues}}, "required": true }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(compiles, result.Diagnostics.Count == 0);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("/widgets/0/parameters/numberFilter", diagnostic.Path));
    }

    [Fact]
    public void Table_binding_a_required_parameter_to_an_optional_page_parameter_is_reported_at_the_binding()
    {
        using var folder = BoundFolder(
            """{ "name": "numberFilter", "type": "text", "required": true }""",
            """{ "name": "number", "type": "text" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, "/widgets/0/parameters/numberFilter"),
            (diagnostic.Code, diagnostic.Path));
        Assert.Contains("'numberFilter'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Table_leaving_a_required_parameter_unbound_is_reported_at_the_data_source()
    {
        using var folder = BoundFolder(
            """{ "name": "numberFilter", "type": "text", "required": true }, { "name": "other", "type": "text", "required": true }""",
            """{ "name": "number", "type": "text", "required": true }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, "/widgets/0/dataSource"),
            (diagnostic.Code, diagnostic.Path));
        Assert.Contains("'other'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "type": "table", "dataSource": "OrderList", "parameters": { "missing": "number" } }""", "/widgets/0/parameters/missing")]
    [InlineData("""{ "type": "table", "dataSource": "OrderList", "parameters": { "numberFilter": "missing" } }""", "/widgets/0/parameters/numberFilter")]
    public void Table_binding_an_unknown_name_is_reported_at_the_binding(string widget, string path)
    {
        using var folder = BoundFolder(
            """{ "name": "numberFilter", "type": "text" }""",
            """{ "name": "number", "type": "text" }""",
            widget);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidWidgetBinding, path), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void Page_parameter_named_like_an_unbound_parameter_of_its_table_is_reported_at_its_name()
    {
        using var folder = BoundFolder(
            """{ "name": "numberFilter", "type": "text" }, { "name": "other", "type": "text" }""",
            """{ "name": "number", "type": "text" }, { "name": "OTHER", "type": "text" }""",
            """{ "type": "table", "dataSource": "OrderList", "parameters": { "numberFilter": "number" } }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidPageParameterName, "pages/orders.json", "/parameters/1/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Page_parameter_named_like_a_bound_parameter_of_its_table_compiles()
    {
        using var folder = BoundFolder(
            """{ "name": "numberFilter", "type": "text" }""",
            """{ "name": "numberFilter", "type": "text" }""",
            """{ "type": "table", "dataSource": "OrderList", "parameters": { "numberFilter": "numberFilter" } }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Parameters_on_a_table_over_an_entity_are_reported()
    {
        using var folder = Folder().With(
            "pages/orders.json",
            Page("Orders", """{ "type": "table", "entity": "Order", "parameters": { "a": "b" } }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, "/widgets/0/parameters"),
            (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void Table_bound_to_a_grouped_data_source_compiles()
    {
        using var folder = Folder()
            .With("data-sources/order-list.json", OrderList(
                """, "aggregate": { "groupBy": ["number"], "measures": [{ "name": "orders", "function": "count" }] } """))
            .With("pages/orders.json", Page("Orders", """{ "type": "table", "dataSource": "OrderList" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void Navigate_action_naming_a_page_file_that_failed_its_schema_reports_only_that_file()
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
    [InlineData("studio")]
    [InlineData("account")]
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

    [Fact]
    public void Page_parameters_of_every_type_compile_in_file_order()
    {
        using var folder = Folder().With("entities/client.json", Client).With(
            "pages/orders.json",
            Page(
                "Orders",
                OrdersWidget,
                parameters: """
                    { "name": "note", "type": "text" },
                    { "name": "count", "type": "integer", "required": true },
                    { "name": "amount", "type": "decimal", "required": false },
                    { "name": "urgent", "type": "boolean" },
                    { "name": "day", "type": "date" },
                    { "name": "moment", "type": "date-time" },
                    { "name": "status", "type": "enum", "values": ["open", "closed"] },
                    { "name": "client", "type": "reference", "target": "client" }
                    """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var orders = Assert.Single(result.Model.Pages, page => page.Name == "Orders");
        var client = new EntityReference(Guid.Parse("22222222-2222-4222-8222-222222222223"), "Client");
        Assert.Equal(
            [
                ("note", FieldType.Text, false),
                ("count", FieldType.Integer, true),
                ("amount", FieldType.Decimal, false),
                ("urgent", FieldType.Boolean, false),
                ("day", FieldType.Date, false),
                ("moment", FieldType.DateTime, false),
                ("status", FieldType.Enum, false),
                ("client", FieldType.Reference, false),
            ],
            orders.Parameters.Select(parameter => (parameter.Name, parameter.Type, parameter.Required)));
        Assert.Equal(["open", "closed"], orders.Parameters[6].Values);
        Assert.Equal(client, orders.Parameters[7].Target);
        Assert.All(orders.Parameters.Where(parameter => parameter.Type != FieldType.Enum), parameter => Assert.Null(parameter.Values));
        Assert.All(orders.Parameters.Where(parameter => parameter.Type != FieldType.Reference), parameter => Assert.Null(parameter.Target));
    }

    [Fact]
    public void A_page_without_parameters_has_none()
    {
        using var folder = Folder();

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotNull(result.Model);
        Assert.All(result.Model.Pages, page => Assert.Empty(page.Parameters));
    }

    [Theory]
    [InlineData("""{ "name": "note", "type": "text" }, { "name": "NOTE", "type": "text" }""", 1)]
    [InlineData("""{ "name": "page", "type": "integer" }""", 0)]
    [InlineData("""{ "name": "PageSize", "type": "integer" }""", 0)]
    [InlineData("""{ "name": "SORT", "type": "text" }""", 0)]
    [InlineData("""{ "name": "Search", "type": "text" }""", 0)]
    public void A_duplicate_or_reserved_page_parameter_name_is_reported(string parameters, int index)
    {
        using var folder = Folder().With("pages/orders.json", Page("Orders", OrdersWidget, parameters: parameters));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidPageParameterName, "pages/orders.json", $"/parameters/{index}/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "total", "type": "money" }""", DiagnosticCodes.SchemaViolation, "/parameters/0/type")]
    [InlineData("""{ "name": "status", "type": "enum" }""", DiagnosticCodes.MissingTypeProperty, "/parameters/0")]
    [InlineData("""{ "name": "client", "type": "reference" }""", DiagnosticCodes.MissingTypeProperty, "/parameters/0")]
    [InlineData("""{ "name": "client", "type": "reference", "target": "Missing" }""", DiagnosticCodes.UnknownReferenceTarget, "/parameters/0/target")]
    [InlineData("""{ "name": "note", "type": "text", "values": ["a"] }""", DiagnosticCodes.InvalidConstraint, "/parameters/0/values")]
    public void A_page_parameter_with_a_bad_type_is_reported_as_a_field_is(string parameters, string code, string path)
    {
        using var folder = Folder().With("pages/orders.json", Page("Orders", OrdersWidget, parameters: parameters));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "pages/orders.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    /// <summary>A folder that compiles: a site whose navigation opens a table page of orders, and form pages for orders and customers.</summary>
    internal static TemporaryFolder Folder() =>
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

    internal static string Page(
        string name, string widget, string id = "55555555-5555-4555-8555-555555555551", string titleKey = "orders.title", string? parameters = null) =>
        $$"""
        { "id": "{{id}}", "kind": "page", "name": "{{name}}", "formatVersion": 1,
          "title": { "textKey": "{{titleKey}}" },{{(parameters is null ? "" : $" \"parameters\": [ {parameters} ],")}} "widgets": [ {{widget}} ] }
        """;

    private static string OrderList(string extra = "") =>
        $$"""
        { "id": "{{OrderListId}}", "kind": "dataSource", "name": "OrderList", "formatVersion": 1, "entity": "Order",
          "fields": [ { "name": "number", "path": "number" } ]{{extra}} }
        """;
}
