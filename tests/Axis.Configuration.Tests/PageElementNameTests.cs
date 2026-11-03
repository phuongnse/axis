using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Tests;

/// <summary>
/// A widget, action, row action, toolbar action, column and tab of a page requires a name, so a customization
/// layer can find it. A missing name is a schema violation at the element.
/// </summary>
public sealed class PageElementNameTests
{
    [Theory]
    [InlineData("""{ "type": "table", "entity": "Order" }""", "/widgets/0")]
    [InlineData("""{ "type": "form", "name": "form", "entity": "Order", "actions": [ { "type": "navigate", "page": "Orders", "label": { "textKey": "orders.title" } } ] }""", "/widgets/0/actions/0")]
    [InlineData("""{ "type": "table", "name": "table", "entity": "Order", "rowActions": [ { "type": "navigate", "page": "OrderForm", "label": { "textKey": "orders.title" }, "record": "id" } ] }""", "/widgets/0/rowActions/0")]
    [InlineData("""{ "type": "table", "name": "table", "entity": "Order", "toolbarActions": [ { "type": "navigate", "page": "OrderForm", "label": { "textKey": "orders.title" } } ] }""", "/widgets/0/toolbarActions/0")]
    [InlineData("""{ "type": "columns", "name": "columns", "columns": [ { "width": 6, "widgets": [ { "type": "table", "name": "table", "entity": "Order" } ] } ] }""", "/widgets/0/columns/0")]
    [InlineData("""{ "type": "tabs", "name": "tabs", "tabs": [ { "name": "tab1", "label": { "textKey": "orders.title" }, "widgets": [ { "type": "table", "name": "table", "entity": "Order" } ] }, { "label": { "textKey": "orders.title" }, "widgets": [ { "type": "table", "name": "table2", "entity": "Order" } ] } ] }""", "/widgets/0/tabs/1")]
    [InlineData("""{ "type": "tabs", "name": "tabs", "tabs": [ { "name": "tab1", "label": { "textKey": "orders.title" }, "widgets": [ { "type": "table", "entity": "Order" } ] } ] }""", "/widgets/0/tabs/0/widgets/0")]
    public void A_page_element_without_a_name_is_a_schema_violation_at_the_element(string widget, string path)
    {
        using var folder = PresentationCompilerTests.Folder()
            .With("pages/orders.json", PresentationCompilerTests.Page("Orders", widget));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Contains(result.Diagnostics, diagnostic => (diagnostic.Code, diagnostic.Path, diagnostic.File) == (DiagnosticCodes.SchemaViolation, path, "pages/orders.json"));
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(DiagnosticCodes.SchemaViolation, diagnostic.Code));
        Assert.Null(result.Model);
    }
}
