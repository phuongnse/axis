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
}
