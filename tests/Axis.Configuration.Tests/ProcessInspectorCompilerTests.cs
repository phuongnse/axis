using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using static Axis.Configuration.Tests.PresentationCompilerTests;

namespace Axis.Configuration.Tests;

public sealed class ProcessInspectorCompilerTests
{
    private const string ProcessId = "99999999-9999-4999-8999-999999999991";
    private const string PageFile = "pages/orders.json";

    [Fact]
    public void Process_inspector_compiles_to_a_widget_holding_the_process_as_declared_and_no_other_reference()
    {
        using var folder = Folder(Inspector("orderreview"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var orders = Assert.Single(result.Model.Pages, page => page.Name == "Orders");
        Assert.Equal(
            new WidgetModel(WidgetType.ProcessInspector, null, null, Process: new ProcessReference(Guid.Parse(ProcessId), "OrderReview")),
            Assert.Single(orders.Widgets));
    }

    [Fact]
    public void Process_inspector_naming_no_loaded_process_is_reported_at_its_process()
    {
        using var folder = Folder(Inspector("Missing"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownWidgetProcess, PageFile, "/widgets/0/process"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Missing'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Process_inspector_naming_a_process_that_failed_to_load_is_not_reported_again()
    {
        using var folder = Folder(Inspector("OrderReview"))
            .With("processes/order-review.json", $$"""{ "id": "{{ProcessId}}", "kind": "process", "name": "OrderReview", "formatVersion": 1 }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("processes/order-review.json", diagnostic.File));
    }

    [Theory]
    [InlineData("entity", "\"Order\"", DiagnosticCodes.InvalidWidgetBinding)]
    [InlineData("dataSource", "\"OrderList\"", DiagnosticCodes.InvalidWidgetBinding)]
    [InlineData("form", "\"OrderEdit\"", DiagnosticCodes.InvalidWidgetBinding)]
    [InlineData("actions", "[]", DiagnosticCodes.InvalidWidgetBinding)]
    [InlineData("formPage", "\"OrderForm\"", DiagnosticCodes.InvalidFormPage)]
    public void Process_inspector_naming_a_binding_a_form_page_or_actions_is_reported_at_that_property(string property, string value, string code)
    {
        using var folder = Folder(Inspector("OrderReview", $$""", "{{property}}": {{value}}"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, PageFile, $"/widgets/0/{property}"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains($"'{property}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "type": "processInspector" }""")]
    [InlineData("""{ "type": "table", "entity": "Order", "process": "OrderReview" }""")]
    public void Process_missing_on_an_inspector_or_set_on_another_widget_violates_the_page_schema(string widget)
    {
        using var folder = Folder(widget);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, PageFile), (diagnostic.Code, diagnostic.File)));
        Assert.Null(result.Model);
    }

    /// <summary>The presentation folder with the process <c>OrderReview</c> and an orders page holding <paramref name="widget"/>.</summary>
    private static TemporaryFolder Folder(string widget) =>
        PresentationCompilerTests.Folder()
            .With("data-sources/order-list.json", """
                { "id": "77777777-7777-4777-8777-777777777771", "kind": "dataSource", "name": "OrderList", "formatVersion": 1, "entity": "Order",
                  "fields": [ { "name": "number", "path": "number" } ] }
                """)
            .With("processes/order-review.json", $$"""
                { "id": "{{ProcessId}}", "kind": "process", "name": "OrderReview", "formatVersion": 1,
                  "entity": "Order", "start": "done", "steps": [ { "name": "done", "type": "end" } ] }
                """)
            .With(PageFile, Page("Orders", widget));

    private static string Inspector(string process, string extra = "") =>
        $$"""{ "type": "processInspector", "process": "{{process}}"{{extra}} }""";
}
