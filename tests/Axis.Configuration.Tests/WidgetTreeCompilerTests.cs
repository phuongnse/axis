using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using static Axis.Configuration.Tests.PresentationCompilerTests;

namespace Axis.Configuration.Tests;

public sealed class WidgetTreeCompilerTests
{
    private const string PageFile = "pages/overview.json";

    [Fact]
    public void Columns_holding_a_table_and_tabs_with_a_section_compile_to_a_tree_in_file_order()
    {
        var tree = Columns("columns", ("left", 8, Table("table")), ("right", 4, Tabs("tabs", ("tab1", "orders.title", Section("section", "orderForm.title", true, Inbox("taskInbox"))))));
        using var folder = Folder(tree);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var overview = Assert.Single(result.Model.Pages, page => page.Name == "Overview");
        var columns = Assert.Single(overview.Widgets);
        Assert.Equal(WidgetType.Columns, columns.Type);
        Assert.Equal([8, 4], columns.Columns.Select(column => column.Width));
        var table = Assert.Single(columns.Columns[0].Widgets);
        Assert.Equal((WidgetType.Table, "Order"), (table.Type, table.Entity!.Name));
        var tabs = Assert.Single(columns.Columns[1].Widgets);
        var tab = Assert.Single(tabs.Tabs);
        Assert.Equal("orders.title", tab.Label.TextKey);
        var section = Assert.Single(tab.Widgets);
        Assert.Equal((WidgetType.Section, "orderForm.title", true), (section.Type, section.Title!.TextKey, section.Collapsed));
        Assert.Equal(WidgetType.TaskInbox, Assert.Single(section.Widgets).Type);
        Assert.Equal([WidgetType.Table, WidgetType.TaskInbox], overview.Leaves().Select(widget => widget.Type));
    }

    [Fact]
    public void Several_widgets_at_the_top_of_a_page_compile_and_a_section_is_expanded_by_default()
    {
        using var folder = Folder($"{Section("section", "orders.title", null, Table("table"))}, {Inbox("taskInbox")}");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var overview = Assert.Single(result.Model!.Pages, page => page.Name == "Overview");
        Assert.Equal([WidgetType.Section, WidgetType.TaskInbox], overview.Widgets.Select(widget => widget.Type));
        Assert.False(overview.Widgets[0].Collapsed);
    }

    [Fact]
    public void Widget_checks_apply_at_any_depth_and_are_reported_at_the_nested_pointer()
    {
        using var folder = Folder(Columns("columns", ("left", 12, """{ "type": "table", "name": "table", "entity": "Invoice" }""")));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownWidgetEntity, PageFile, "/widgets/0/columns/0/widgets/0/entity"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Widget_list_on_level_4_is_allowed_and_a_container_on_level_4_is_reported_at_its_pointer()
    {
        // Levels 1 to 3 are containers, so the innermost widget list is on level 4.
        var allowed = Columns("columns", ("left", 12, Tabs("tabs", ("tab1", "orders.title", Section("section", "orders.title", false, Inbox("taskInbox"))))));
        using (var folder = Folder(allowed))
        {
            Assert.Empty(ApplicationCompiler.Compile(folder.Path).Diagnostics);
        }

        // A fourth container would hold a widget list on level 5.
        var tooDeep = Columns("columns", ("left", 12, Tabs("tabs", ("tab1", "orders.title", Section("section", "orders.title", false, Columns("columns2", ("left", 12, Inbox("taskInbox"))))))));
        using var deep = Folder(tooDeep);

        var result = ApplicationCompiler.Compile(deep.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.TooDeepWidgetTree, PageFile, "/widgets/0/columns/0/widgets/0/tabs/0/widgets/0/widgets/0"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData(6, 5, 11)]
    [InlineData(8, 8, 16)]
    public void Column_widths_that_do_not_add_up_to_12_are_reported_at_the_columns(int first, int second, int sum)
    {
        using var folder = Folder(Columns("columns", ("left", first, Table("table")), ("right", second, Inbox("taskInbox"))));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidColumnWidths, PageFile, "/widgets/0/columns"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains($"add up to {sum}", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Second_table_anywhere_on_the_page_is_reported_at_the_second_table()
    {
        using var folder = Folder($"{Table("table")}, {Columns("columns", ("left", 12, """{ "type": "table", "name": "table2", "entity": "Customer" }"""))}");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicatePageTable, PageFile, "/widgets/1/columns/0/widgets/0"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'/widgets/0'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Form_with_a_task_inbox_is_reported_at_the_second_record_widget()
    {
        using var folder = Folder($"{OrderFormWidget("form")}, {Section("section", "orders.title", false, Inbox("taskInbox"))}");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicatePageRecordWidget, PageFile, "/widgets/1/widgets/0"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'/widgets/0'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Table_with_a_form_and_an_inbox_in_other_containers_is_reported_once_for_the_inbox()
    {
        using var folder = Folder($"{Table("table")}, {OrderFormWidget("form")}, {Inbox("taskInbox")}");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.DuplicatePageRecordWidget, "/widgets/2"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void Record_navigate_finds_the_form_in_containers_and_checks_its_entity()
    {
        const string openOrder = """
            "rowActions": [ { "type": "navigate", "name": "navigate", "page": "OrderForm", "label": { "textKey": "orders.title" }, "record": "id" } ]
            """;
        using var folder = Folder($$"""{ "type": "table", "name": "table", "entity": "Order", {{openOrder}} }""")
            .With("pages/order-form.json", Page(
                "OrderForm",
                Columns("columns", ("left", 12, Section("section", "orders.title", false, OrderFormWidget("form")))),
                "55555555-5555-4555-8555-555555555552",
                "orderForm.title"))
            .With("pages/customer-widget.json", Page(
                "CustomerWidget",
                Section("section", "orders.title", false, $$"""{ "type": "table", "name": "table", "entity": "Customer", {{openOrder}} }"""),
                "55555555-5555-4555-8555-555555555554",
                "orders.title"));

        var result = ApplicationCompiler.Compile(folder.Path);

        // The orders table opens a form page that holds its form in containers and is fine. The form is over Order, so the
        // customer table's record is reported where it is written.
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidNavigateRecord, "pages/customer-widget.json", "/widgets/0/widgets/0/rowActions/0/record"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Missing_text_key_on_a_tab_label_or_a_section_title_is_reported_at_its_nested_pointer()
    {
        using var folder = Folder(Tabs("tabs", ("tab1", "missing.label", Section("section", "missing.title", false, Inbox("taskInbox")))));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.MissingTextKey, "/widgets/0/tabs/0/label/textKey"),
                (DiagnosticCodes.MissingTextKey, "/widgets/0/tabs/0/widgets/0/title/textKey"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)).Order());
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "type": "columns", "name": "columns", "entity": "Order", "columns": [ { "name": "left", "width": 12, "widgets": [ { "type": "taskInbox", "name": "taskInbox" } ] } ] }""")]
    [InlineData("""{ "type": "columns", "name": "columns" }""")]
    [InlineData("""{ "type": "columns", "name": "columns", "columns": [] }""")]
    [InlineData("""{ "type": "columns", "name": "columns", "columns": [ { "name": "left", "width": 13, "widgets": [ { "type": "taskInbox", "name": "taskInbox" } ] } ] }""")]
    [InlineData("""{ "type": "columns", "name": "columns", "columns": [ { "name": "left", "width": 0, "widgets": [ { "type": "taskInbox", "name": "taskInbox" } ] } ] }""")]
    [InlineData("""{ "type": "columns", "name": "columns", "columns": [ { "name": "left", "width": 12, "widgets": [] } ] }""")]
    [InlineData("""{ "type": "tabs", "name": "tabs" }""")]
    [InlineData("""{ "type": "tabs", "name": "tabs", "tabs": [ { "name": "tab1", "widgets": [ { "type": "taskInbox", "name": "taskInbox" } ] } ] }""")]
    [InlineData("""{ "type": "section", "name": "section", "widgets": [ { "type": "taskInbox", "name": "taskInbox" } ] }""")]
    [InlineData("""{ "type": "section", "name": "section", "title": { "textKey": "orders.title" }, "widgets": [] }""")]
    [InlineData("""{ "type": "section", "name": "section", "title": { "textKey": "orders.title" } }""")]
    [InlineData("""{ "type": "section", "name": "section", "title": { "textKey": "orders.title" }, "entity": "Order", "widgets": [ { "type": "taskInbox", "name": "taskInbox" } ] }""")]
    [InlineData("""{ "type": "taskInbox", "name": "taskInbox", "widgets": [ { "type": "taskInbox", "name": "taskInbox2" } ] }""")]
    [InlineData("""{ "type": "taskInbox", "name": "taskInbox", "collapsed": true }""")]
    [InlineData("""{ "type": "table", "name": "table", "entity": "Order", "columns": [ { "name": "left", "width": 12, "widgets": [ { "type": "taskInbox", "name": "taskInbox" } ] } ] }""")]
    public void Malformed_containers_violate_the_page_schema(string widget)
    {
        using var folder = Folder(widget);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, PageFile), (diagnostic.Code, diagnostic.File)));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Empty_widgets_list_at_the_top_of_a_page_violates_the_page_schema()
    {
        using var folder = PresentationCompilerTests.Folder().With(PageFile, Page("Overview", "", "55555555-5555-4555-8555-555555555560"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, PageFile), (diagnostic.Code, diagnostic.File)));
    }

    [Fact]
    public void Widget_name_repeated_in_another_section_ignoring_case_is_reported_at_the_later_widget()
    {
        var first = """{ "type": "table", "name": "Summary", "entity": "Order" }""";
        var second = """{ "type": "taskInbox", "name": "summary" }""";
        using var folder = Folder(Columns("columns", ("left", 6, Section("section", "orders.title", false, first)), ("right", 6, Section("section2", "orders.title", false, second))));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicatePageElementName, PageFile, "/widgets/0/columns/1/widgets/0/widgets/0/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'/widgets/0/columns/0/widgets/0/widgets/0/name'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Row_action_name_repeated_in_its_table_is_reported_at_the_later_action()
    {
        using var folder = Folder("""
            { "type": "table", "name": "table", "entity": "Order", "rowActions": [
              { "type": "delete", "name": "Remove", "label": { "textKey": "orders.title" } },
              { "type": "delete", "name": "remove", "label": { "textKey": "orders.title" } }
            ] }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicatePageElementName, PageFile, "/widgets/0/rowActions/1/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Column_name_repeated_in_its_columns_widget_is_reported_at_the_later_column()
    {
        using var folder = Folder("""
            { "type": "columns", "name": "columns", "columns": [
              { "width": 6, "name": "Left", "widgets": [ { "type": "table", "name": "table", "entity": "Order" } ] },
              { "width": 6, "name": "left", "widgets": [ { "type": "taskInbox", "name": "taskInbox" } ] }
            ] }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicatePageElementName, PageFile, "/widgets/0/columns/1/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Tab_name_repeated_in_its_tabs_widget_is_reported_at_the_later_tab()
    {
        using var folder = Folder("""
            { "type": "tabs", "name": "tabs", "tabs": [
              { "name": "Details", "label": { "textKey": "orders.title" }, "widgets": [ { "type": "table", "name": "table", "entity": "Order" } ] },
              { "name": "DETAILS", "label": { "textKey": "orders.title" }, "widgets": [ { "type": "taskInbox", "name": "taskInbox" } ] }
            ] }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicatePageElementName, PageFile, "/widgets/0/tabs/1/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Row_action_and_toolbar_action_may_share_a_name()
    {
        using var folder = Folder("""
            { "type": "table", "name": "table", "entity": "Order",
              "rowActions": [ { "type": "delete", "name": "Open", "label": { "textKey": "orders.title" } } ],
              "toolbarActions": [ { "type": "navigate", "name": "Open", "page": "OrderForm", "label": { "textKey": "orders.title" } } ] }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void Tab_name_may_repeat_across_two_different_tabs_widgets()
    {
        using var folder = Folder(Columns(
            "columns",
            ("left", 6, """{ "type": "tabs", "name": "tabs", "tabs": [ { "name": "Details", "label": { "textKey": "orders.title" }, "widgets": [ { "type": "table", "name": "table", "entity": "Order" } ] } ] }"""),
            ("right", 6, """{ "type": "tabs", "name": "tabs2", "tabs": [ { "name": "Details", "label": { "textKey": "orders.title" }, "widgets": [ { "type": "form", "name": "form", "entity": "Order" } ] } ] }""")));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    /// <summary>The presentation folder with an overview page, not in the site's navigation, holding <paramref name="widgets"/>.</summary>
    private static TemporaryFolder Folder(string widgets) =>
        PresentationCompilerTests.Folder().With(PageFile, Page("Overview", widgets, "55555555-5555-4555-8555-555555555560"));

    private static string Table(string name) => $$"""{ "type": "table", "name": "{{name}}", "entity": "Order" }""";

    private static string Inbox(string name) => $$"""{ "type": "taskInbox", "name": "{{name}}" }""";

    private static string OrderFormWidget(string name) => $$"""{ "type": "form", "name": "{{name}}", "entity": "Order" }""";

    private static string Columns(string name, params (string Name, int Width, string Widgets)[] columns) =>
        $$"""{ "type": "columns", "name": "{{name}}", "columns": [ {{string.Join(", ", columns.Select(column => $$"""{ "name": "{{column.Name}}", "width": {{column.Width}}, "widgets": [ {{column.Widgets}} ] }"""))}} ] }""";

    private static string Tabs(string name, params (string Name, string LabelKey, string Widgets)[] tabs) =>
        $$"""{ "type": "tabs", "name": "{{name}}", "tabs": [ {{string.Join(", ", tabs.Select(tab => $$"""{ "name": "{{tab.Name}}", "label": { "textKey": "{{tab.LabelKey}}" }, "widgets": [ {{tab.Widgets}} ] }"""))}} ] }""";

    private static string Section(string name, string titleKey, bool? collapsed, string widgets) =>
        $$"""{ "type": "section", "name": "{{name}}", "title": { "textKey": "{{titleKey}}" }{{(collapsed is { } value ? $", \"collapsed\": {(value ? "true" : "false")}" : "")}}, "widgets": [ {{widgets}} ] }""";
}
