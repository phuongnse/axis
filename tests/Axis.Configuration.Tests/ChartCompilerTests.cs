using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using static Axis.Configuration.Tests.PresentationCompilerTests;

namespace Axis.Configuration.Tests;

public sealed class ChartCompilerTests
{
    private const string TextsEn = """
        { "id": "33333333-3333-4333-8333-333333333333", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "site.title": "Sales", "nav.orders": "Orders", "orders.title": "Orders", "orderForm.title": "Order",
                     "customerForm.title": "Customer", "chart.title": "Orders by status", "chart.orders": "Orders",
                     "chart.quantity": "Quantity" } }
        """;

    private const string OrderEntity = """
        { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1, "displayField": "number",
          "fields": [ { "name": "number", "type": "text", "required": true }, { "name": "status", "type": "text" },
                      { "name": "quantity", "type": "integer" }, { "name": "total", "type": "decimal" },
                      { "name": "fee", "type": "money", "currency": "VND" }, { "name": "placedOn", "type": "date" } ] }
        """;

    private const string OrdersFile = "pages/orders.json";

    private const string Valid = """
        { "type": "chart", "kind": "bar", "dataSource": "OrderStats", "category": "status", "title": { "textKey": "chart.title" },
          "measures": [ { "name": "orders", "label": { "textKey": "chart.orders" } }, { "name": "quantitysum", "label": { "textKey": "chart.quantity" } } ] }
        """;

    private static string Chart(string kind = "bar", string dataSource = "OrderStats", string category = "status", string measures = "")
    {
        var list = measures.Length > 0 ? measures : """{ "name": "orders", "label": { "textKey": "chart.orders" } }""";
        return $$"""{ "type": "chart", "kind": "{{kind}}", "dataSource": "{{dataSource}}", "category": "{{category}}", "title": { "textKey": "chart.title" }, "measures": [ {{list}} ] }""";
    }

    private static string Measure(string name) => $$"""{ "name": "{{name}}", "label": { "textKey": "chart.orders" } }""";

    [Theory]
    [InlineData("bar", ChartKind.Bar)]
    [InlineData("line", ChartKind.Line)]
    [InlineData("pie", ChartKind.Pie)]
    public void Chart_compiles_to_its_kind_and_the_names_as_the_data_source_declares_them(string kind, ChartKind expected)
    {
        using var folder = Folder(Chart(kind, category: "STATUS", measures: Measure("QUANTITYSUM")));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var widget = Assert.Single(Assert.Single(result.Model.Pages, page => page.Name == "Orders").Widgets);
        Assert.Equal(WidgetType.Chart, widget.Type);
        Assert.Equal(new DataSourceReference(Guid.Parse("77777777-7777-4777-8777-777777777775"), "OrderStats"), widget.DataSource);
        Assert.Null(widget.Entity);
        Assert.Null(widget.Form);
        Assert.Empty(widget.Actions);
        Assert.Null(widget.Kpi);
        var chart = widget.Chart!;
        Assert.Equal(expected, chart.Kind);
        Assert.Equal(new TextReference("chart.title"), chart.Title);
        Assert.Equal("status", chart.Category);
        Assert.Equal([new ChartMeasureModel("quantitySum", new TextReference("chart.orders"))], chart.Measures);
    }

    [Fact]
    public void Chart_keeps_its_measures_in_file_order()
    {
        using var folder = Folder(Valid);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var chart = Assert.Single(Assert.Single(result.Model!.Pages, page => page.Name == "Orders").Widgets).Chart!;
        Assert.Equal(
            [new ChartMeasureModel("orders", new TextReference("chart.orders")), new ChartMeasureModel("quantitySum", new TextReference("chart.quantity"))],
            chart.Measures);
    }

    [Fact]
    public void Other_widgets_have_no_chart()
    {
        using var folder = Folder(Valid);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.All(
            result.Model!.Pages.Where(page => page.Name != "Orders").SelectMany(page => page.Widgets),
            widget => Assert.Null(widget.Chart));
    }

    [Fact]
    public void Chart_does_not_count_toward_the_one_table_and_the_one_record_widget_of_a_page()
    {
        using var folder = Folder(
            $$"""
            { "type": "table", "entity": "Order" },
            { "type": "form", "entity": "Order" },
            {{Chart()}},
            {{Chart()}}
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Theory]
    [InlineData("OrderList")]
    [InlineData("OrderTotals")]
    [InlineData("OrderPairs")]
    public void Chart_over_a_data_source_without_exactly_one_group_field_is_reported_at_its_data_source(string dataSource)
    {
        using var folder = Folder(Chart(dataSource: dataSource, category: "status"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ChartDataSourceNotGrouped, OrdersFile, "/widgets/0/dataSource"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains($"'{dataSource}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Chart_whose_category_is_not_the_group_field_is_reported_at_its_category()
    {
        using var folder = Folder(Chart(category: "number"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ChartDataSourceNotGrouped, OrdersFile, "/widgets/0/category"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'status'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Chart_naming_a_measure_its_data_source_lacks_is_reported_at_that_measure()
    {
        using var folder = Folder(Chart(measures: $"{Measure("orders")}, {Measure("missing")}"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidChartMeasure, OrdersFile, "/widgets/0/measures/1/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'missing'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("firstPlaced")]
    [InlineData("lastPlaced")]
    public void Chart_naming_a_min_or_max_of_a_date_is_reported_at_that_measure(string measure)
    {
        using var folder = Folder(Chart(measures: Measure(measure)));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidChartMeasure, OrdersFile, "/widgets/0/measures/0/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains($"'{measure}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quantitySum")]
    [InlineData("maxQuantity")]
    [InlineData("minTotal")]
    [InlineData("maxFee")]
    [InlineData("totalAvg")]
    [InlineData("distinctQuantities")]
    public void Chart_naming_a_count_a_count_distinct_sum_avg_or_a_min_or_max_of_a_number_compiles(string measure)
    {
        using var folder = Folder(Chart(measures: Measure(measure)));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void Pie_with_more_than_one_measure_is_reported_at_its_measures()
    {
        using var folder = Folder(Chart("pie", measures: $"{Measure("orders")}, {Measure("quantitySum")}"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidChartMeasure, OrdersFile, "/widgets/0/measures"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("bar")]
    [InlineData("line")]
    public void Bar_and_line_with_several_measures_compile(string kind)
    {
        using var folder = Folder(Chart(kind, measures: $"{Measure("orders")}, {Measure("quantitySum")}"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Theory]
    [InlineData("entity", "\"Order\"")]
    [InlineData("form", "\"OrderEdit\"")]
    [InlineData("actions", "[]")]
    [InlineData("rowActions", "[]")]
    [InlineData("toolbarActions", "[]")]
    public void Chart_naming_an_entity_a_form_or_actions_is_reported_at_that_property(string property, string value)
    {
        using var folder = Folder(Chart().Replace("\"type\": \"chart\"", $"\"type\": \"chart\", \"{property}\": {value}", StringComparison.Ordinal));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, OrdersFile, $"/widgets/0/{property}"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains($"'{property}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Chart_over_a_data_source_with_a_required_parameter_is_reported_naming_the_parameter()
    {
        using var folder = Folder(Chart(dataSource: "FilteredStats"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, OrdersFile, "/widgets/0/dataSource"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'numberFilter'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    private const string NumberParameter = """{ "name": "number", "type": "text", "required": true }""";

    private static string BoundChart(string dataSource, string parameters) =>
        Chart(dataSource: dataSource)[..^1] + $$""", "parameters": {{parameters}} }""";

    [Fact]
    public void Chart_binding_a_required_parameter_to_a_required_page_parameter_compiles_with_the_declared_names()
    {
        using var folder = Folder(
            BoundChart("FilteredStats", """{ "NUMBERFILTER": "NUMBER" }"""),
            pageParameters: NumberParameter);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var widget = Assert.Single(Assert.Single(result.Model!.Pages, page => page.Name == "Orders").Widgets);
        Assert.Equal(new Dictionary<string, string> { ["numberFilter"] = "number" }, widget.Parameters);
    }

    [Theory]
    [InlineData("""{ "missing": "number" }""", """{ "name": "number", "type": "text" }""")]
    [InlineData("""{ "numberFilter": "missing" }""", """{ "name": "number", "type": "text" }""")]
    [InlineData("""{ "numberFilter": "number" }""", """{ "name": "number", "type": "integer" }""")]
    [InlineData("""{ "numberFilter": "number", "NUMBERFILTER": "number" }""", """{ "name": "number", "type": "text" }""")]
    public void Chart_binding_an_unknown_name_or_a_mismatched_type_is_reported_at_the_binding(string parameters, string pageParameter)
    {
        using var folder = Folder(BoundChart("OptionalStats", parameters), pageParameters: pageParameter);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.InvalidWidgetBinding, diagnostic.Code);
        Assert.Equal(OrdersFile, diagnostic.File);
        Assert.StartsWith("/widgets/0/parameters/", diagnostic.Path, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Chart_binding_a_required_parameter_to_an_optional_page_parameter_is_reported_at_the_binding()
    {
        using var folder = Folder(
            BoundChart("FilteredStats", """{ "numberFilter": "number" }"""),
            pageParameters: """{ "name": "number", "type": "text" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, OrdersFile, "/widgets/0/parameters/numberFilter"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Chart_leaving_a_required_parameter_unbound_is_reported_at_its_data_source_naming_the_parameter()
    {
        using var folder = Folder(
            BoundChart("FilteredStats", "{}"),
            pageParameters: NumberParameter);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, OrdersFile, "/widgets/0/dataSource"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'numberFilter'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Page_parameter_named_like_an_unbound_parameter_of_its_chart_compiles()
    {
        using var folder = Folder(
            Chart(dataSource: "OptionalStats"),
            pageParameters: """{ "name": "numberFilter", "type": "text" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Chart_over_a_data_source_with_an_optional_parameter_compiles()
    {
        using var folder = Folder(Chart(dataSource: "OptionalStats"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void Chart_naming_no_loaded_data_source_is_reported_at_its_data_source()
    {
        using var folder = Folder(Chart(dataSource: "Missing"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownWidgetDataSource, OrdersFile, "/widgets/0/dataSource"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Chart_title_and_measure_labels_without_texts_are_reported_at_their_text_keys()
    {
        using var folder = Folder(
            """
            { "type": "chart", "kind": "bar", "dataSource": "OrderStats", "category": "status", "title": { "textKey": "chart.noTitle" },
              "measures": [ { "name": "orders", "label": { "textKey": "chart.noLabel" } }, { "name": "quantitySum", "label": { "textKey": "chart.quantity" } },
                            { "name": "orders", "label": { "textKey": "chart.noOther" } } ] }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.MissingTextKey, "/widgets/0/measures/0/label/textKey"),
                (DiagnosticCodes.MissingTextKey, "/widgets/0/measures/2/label/textKey"),
                (DiagnosticCodes.MissingTextKey, "/widgets/0/title/textKey"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "type": "chart", "dataSource": "OrderStats", "category": "status", "title": { "textKey": "chart.title" }, "measures": [ { "name": "orders", "label": { "textKey": "chart.orders" } } ] }""")]
    [InlineData("""{ "type": "chart", "kind": "bar", "category": "status", "title": { "textKey": "chart.title" }, "measures": [ { "name": "orders", "label": { "textKey": "chart.orders" } } ] }""")]
    [InlineData("""{ "type": "chart", "kind": "bar", "dataSource": "OrderStats", "title": { "textKey": "chart.title" }, "measures": [ { "name": "orders", "label": { "textKey": "chart.orders" } } ] }""")]
    [InlineData("""{ "type": "chart", "kind": "bar", "dataSource": "OrderStats", "category": "status", "measures": [ { "name": "orders", "label": { "textKey": "chart.orders" } } ] }""")]
    [InlineData("""{ "type": "chart", "kind": "bar", "dataSource": "OrderStats", "category": "status", "title": { "textKey": "chart.title" } }""")]
    [InlineData("""{ "type": "chart", "kind": "bar", "dataSource": "OrderStats", "category": "status", "title": { "textKey": "chart.title" }, "measures": [] }""")]
    [InlineData("""{ "type": "chart", "kind": "bar", "dataSource": "OrderStats", "category": "status", "title": { "textKey": "chart.title" }, "measures": [ { "name": "orders" } ] }""")]
    [InlineData("""{ "type": "chart", "kind": "area", "dataSource": "OrderStats", "category": "status", "title": { "textKey": "chart.title" }, "measures": [ { "name": "orders", "label": { "textKey": "chart.orders" } } ] }""")]
    [InlineData("""{ "type": "chart", "kind": "bar", "dataSource": "OrderStats", "category": "status", "title": { "textKey": "chart.title" }, "measures": [ { "name": "orders", "label": { "textKey": "chart.orders" } } ], "measure": "orders" }""")]
    [InlineData("""{ "type": "table", "entity": "Order", "kind": "bar" }""")]
    [InlineData("""{ "type": "table", "entity": "Order", "category": "status" }""")]
    [InlineData("""{ "type": "table", "entity": "Order", "title": { "textKey": "chart.title" } }""")]
    [InlineData("""{ "type": "table", "entity": "Order", "measures": [ { "name": "orders", "label": { "textKey": "chart.orders" } } ] }""")]
    public void Chart_with_a_missing_property_or_another_widget_with_a_chart_property_violates_the_page_schema(string widget)
    {
        using var folder = Folder(widget);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, OrdersFile), (diagnostic.Code, diagnostic.File)));
        Assert.Null(result.Model);
    }

    /// <summary>
    /// The presentation folder with data sources over orders and the page <c>Orders</c> holding <paramref name="widgets"/>, with the given page parameters.
    /// </summary>
    private static TemporaryFolder Folder(string widgets, string? pageParameters = null) =>
        PresentationCompilerTests.Folder()
            .With("texts/en.json", TextsEn)
            .With("entities/order.json", OrderEntity)
            .With("data-sources/order-list.json", """
                { "id": "77777777-7777-4777-8777-777777777771", "kind": "dataSource", "name": "OrderList", "formatVersion": 1, "entity": "Order",
                  "fields": [ { "name": "number", "path": "number" } ] }
                """)
            .With("data-sources/order-totals.json", """
                { "id": "77777777-7777-4777-8777-777777777773", "kind": "dataSource", "name": "OrderTotals", "formatVersion": 1, "entity": "Order",
                  "fields": [ { "name": "quantity", "path": "quantity" } ],
                  "aggregate": { "groupBy": [], "measures": [ { "name": "orders", "function": "count" } ] } }
                """)
            .With("data-sources/order-pairs.json", """
                { "id": "77777777-7777-4777-8777-777777777776", "kind": "dataSource", "name": "OrderPairs", "formatVersion": 1, "entity": "Order",
                  "fields": [ { "name": "status", "path": "status" }, { "name": "number", "path": "number" } ],
                  "aggregate": { "groupBy": ["status", "number"], "measures": [ { "name": "orders", "function": "count" } ] } }
                """)
            .With("data-sources/order-stats.json", """
                { "id": "77777777-7777-4777-8777-777777777775", "kind": "dataSource", "name": "OrderStats", "formatVersion": 1, "entity": "Order",
                  "fields": [ { "name": "status", "path": "status" }, { "name": "number", "path": "number" }, { "name": "quantity", "path": "quantity" },
                              { "name": "total", "path": "total" }, { "name": "fee", "path": "fee" }, { "name": "placedOn", "path": "placedOn" } ],
                  "aggregate": { "groupBy": ["status"], "measures": [
                    { "name": "orders", "function": "count" },
                    { "name": "maxFee", "function": "max", "field": "fee" },
                    { "name": "quantitySum", "function": "sum", "field": "quantity" },
                    { "name": "maxQuantity", "function": "max", "field": "quantity" },
                    { "name": "minTotal", "function": "min", "field": "total" },
                    { "name": "firstPlaced", "function": "min", "field": "placedOn" },
                    { "name": "lastPlaced", "function": "max", "field": "placedOn" },
                    { "name": "totalAvg", "function": "avg", "field": "total" },
                    { "name": "distinctQuantities", "function": "countDistinct", "field": "quantity" } ] } }
                """)
            .With("data-sources/filtered-stats.json", """
                { "id": "77777777-7777-4777-8777-777777777774", "kind": "dataSource", "name": "FilteredStats", "formatVersion": 1, "entity": "Order",
                  "fields": [ { "name": "status", "path": "status" } ],
                  "parameters": [ { "name": "numberFilter", "type": "text", "required": true } ],
                  "aggregate": { "groupBy": ["status"], "measures": [ { "name": "orders", "function": "count" } ] } }
                """)
            .With("data-sources/optional-stats.json", """
                { "id": "77777777-7777-4777-8777-777777777777", "kind": "dataSource", "name": "OptionalStats", "formatVersion": 1, "entity": "Order",
                  "fields": [ { "name": "status", "path": "status" } ],
                  "parameters": [ { "name": "numberFilter", "type": "text" } ],
                  "aggregate": { "groupBy": ["status"], "measures": [ { "name": "orders", "function": "count" } ] } }
                """)
            .With(OrdersFile, Page("Orders", widgets, parameters: pageParameters));
}
