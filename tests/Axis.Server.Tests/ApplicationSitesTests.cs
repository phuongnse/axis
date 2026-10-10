using Axis.Configuration.Compilation;
using Axis.Presentation.Sites;

namespace Axis.Server.Tests;

public sealed class ApplicationSitesTests
{
    [Fact]
    public void Table_over_a_grouped_data_source_lists_the_group_fields_then_the_measures()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"));
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);

        var page = ApplicationSites.FindPage(result.Model, "RequestsByDepartment");

        Assert.NotNull(page);
        var widget = Assert.Single(page.Widgets);
        Assert.Null(widget.FormPage);
        Assert.Null(widget.Entity);
        Assert.NotNull(widget.DataSource);
        Assert.Equal(
            [("department", "reference", "purchaseRequest.department"), ("requests", "integer", null), ("total", "decimal", null)],
            widget.DataSource.Columns.Select(column => (column.Name, column.Type, column.LabelKey)));
    }

    [Fact]
    public void Form_marks_only_the_purchase_request_number_as_a_sequence_field()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"));
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);

        var page = ApplicationSites.FindPage(result.Model, "PurchaseRequestForm");

        Assert.NotNull(page);
        var widget = Assert.Single(page.Widgets);
        Assert.NotNull(widget.Entity);
        Assert.Equal("number", widget.Entity.Fields[0].Name);
        Assert.True(widget.Entity.Fields[0].Sequence);
        Assert.All(widget.Entity.Fields.Skip(1), field => Assert.False(field.Sequence));
        Assert.All(widget.Entity.Fields.SelectMany(field => field.Fields ?? []), field => Assert.False(field.Sequence));
    }
}
