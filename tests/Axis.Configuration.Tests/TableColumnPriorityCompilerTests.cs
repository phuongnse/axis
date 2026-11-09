using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using static Axis.Configuration.Tests.PresentationCompilerTests;

namespace Axis.Configuration.Tests;

public sealed class TableColumnPriorityCompilerTests
{
    private const string OrdersFile = "pages/orders.json";

    private const string OrderEntity = """
        { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1,
          "fields": [ { "name": "number", "type": "text", "required": true }, { "name": "status", "type": "text" },
                      { "name": "lines", "type": "child-collection", "target": "LineItem" },
                      { "name": "customers", "type": "references", "target": "Client" } ] }
        """;

    private const string LineItemEntity = """
        { "id": "11111111-1111-4111-8111-111111111112", "kind": "entity", "name": "LineItem", "formatVersion": 1,
          "fields": [ { "name": "description", "type": "text" } ] }
        """;

    private const string OrderEntityReference = "\"entity\": \"Order\"";
    private const string OrderListReference = "\"dataSource\": \"OrderList\"";
    private const string OrderStatsReference = "\"dataSource\": \"OrderStats\"";

    private static string Table(string priorities, string reference = OrderEntityReference) =>
        $$"""{ "type": "table", "name": "table", {{reference}}, "priorities": {{priorities}} }""";

    [Theory]
    [InlineData("high", ColumnPriority.High)]
    [InlineData("low", ColumnPriority.Low)]
    public void Table_over_an_entity_compiles_priorities_under_the_declared_column_names(string priority, ColumnPriority expected)
    {
        using var folder = Folder(Table($$"""{ "NUMBER": "{{priority}}" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var widget = Assert.Single(Assert.Single(result.Model!.Pages, page => page.Name == "Orders").Widgets);
        Assert.Equal(new Dictionary<string, ColumnPriority> { ["number"] = expected }, widget.Priorities);
    }

    [Fact]
    public void Table_over_a_data_source_compiles_a_priority_on_a_column()
    {
        using var folder = Folder(Table("""{ "STATUS": "low" }""", OrderListReference));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var widget = Assert.Single(Assert.Single(result.Model!.Pages, page => page.Name == "Orders").Widgets);
        Assert.Equal(new Dictionary<string, ColumnPriority> { ["status"] = ColumnPriority.Low }, widget.Priorities);
    }

    [Fact]
    public void Table_over_a_grouped_data_source_compiles_a_priority_on_a_group_field_and_on_a_measure()
    {
        using var folder = Folder(Table("""{ "STATUS": "high", "ORDERS": "low" }""", OrderStatsReference));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var widget = Assert.Single(Assert.Single(result.Model!.Pages, page => page.Name == "Orders").Widgets);
        Assert.Equal(
            new Dictionary<string, ColumnPriority> { ["status"] = ColumnPriority.High, ["orders"] = ColumnPriority.Low },
            widget.Priorities);
    }

    [Fact]
    public void Table_without_priorities_and_other_widgets_have_none()
    {
        using var folder = Folder("""{ "type": "table", "name": "table", "entity": "Order" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.All(result.Model!.Pages.SelectMany(page => page.Widgets), widget => Assert.Same(WidgetModel.NoPriorities, widget.Priorities));
    }

    [Theory]
    [InlineData("""{ "number": "medium" }""")]
    [InlineData("""{ "number": "" }""")]
    [InlineData("""{ "number": "High" }""")]
    [InlineData("""{ "number": 1 }""")]
    [InlineData("{}")]
    public void Table_priority_other_than_high_or_low_violates_the_page_schema(string priorities)
    {
        using var folder = Folder(Table(priorities));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, OrdersFile), (diagnostic.Code, diagnostic.File)));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "type": "form", "name": "form", "entity": "Order", "priorities": { "number": "low" } }""")]
    [InlineData("""{ "type": "taskInbox", "name": "inbox", "priorities": { "number": "low" } }""")]
    public void Priorities_on_a_widget_that_is_not_a_table_violate_the_page_schema(string widget)
    {
        using var folder = Folder(widget);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, OrdersFile), (diagnostic.Code, diagnostic.File)));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("lines")]
    [InlineData("customers")]
    public void Priority_on_a_column_the_table_does_not_show_is_reported_at_that_column(string key)
    {
        using var folder = Folder(Table($$"""{ "number": "low", "{{key}}": "high" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, OrdersFile, $"/widgets/0/priorities/{key}"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains($"'{key}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Priority_on_a_field_the_data_source_does_not_show_is_reported_at_that_column()
    {
        using var folder = Folder(Table("""{ "number": "high" }""", OrderListReference));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, OrdersFile, "/widgets/0/priorities/number"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Priority_on_a_field_a_grouped_data_source_does_not_group_by_is_reported_at_that_column()
    {
        using var folder = Folder(Table("""{ "number": "high" }""", OrderStatsReference));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, OrdersFile, "/widgets/0/priorities/number"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_column_named_twice_ignoring_letter_case_is_reported_at_the_second_key()
    {
        using var folder = Folder(Table("""{ "number": "high", "NUMBER": "low" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, OrdersFile, "/widgets/0/priorities/NUMBER"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("more than once", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    private static TemporaryFolder Folder(string widgets) =>
        PresentationCompilerTests.Folder()
            .With("entities/order.json", OrderEntity)
            .With("entities/line-item.json", LineItemEntity)
            .With("entities/client.json", Client)
            .With("data-sources/order-list.json", """
                { "id": "77777777-7777-4777-8777-777777777771", "kind": "dataSource", "name": "OrderList", "formatVersion": 1, "entity": "Order",
                  "fields": [ { "name": "status", "path": "status" } ] }
                """)
            .With("data-sources/order-stats.json", """
                { "id": "77777777-7777-4777-8777-777777777775", "kind": "dataSource", "name": "OrderStats", "formatVersion": 1, "entity": "Order",
                  "fields": [ { "name": "status", "path": "status" }, { "name": "number", "path": "number" } ],
                  "aggregate": { "groupBy": ["status"], "measures": [ { "name": "orders", "function": "count" } ] } }
                """)
            .With(OrdersFile, Page("Orders", widgets));
}
