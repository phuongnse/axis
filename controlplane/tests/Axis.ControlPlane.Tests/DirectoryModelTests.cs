using Axis.ControlPlane.RegionDirectory;
using Microsoft.EntityFrameworkCore;

namespace Axis.ControlPlane.Tests;

public sealed class DirectoryModelTests
{
    private static readonly string[] _personalWords = ["name", "email", "plan", "payment"];

    [Fact]
    public void The_model_has_the_five_directory_tables_and_no_other()
    {
        using var context = CreateContext();

        var tables = context.Model.GetRelationalModel().Tables;

        Assert.All(tables, table => Assert.Equal("directory", table.Schema));
        Assert.Equal(["cells", "held", "hosts", "regions", "tenants"], tables.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void No_column_holds_name_email_plan_or_payment()
    {
        using var context = CreateContext();

        var columns = context.Model.GetRelationalModel().Tables
            .SelectMany(table => table.Columns)
            .Select(column => column.Name)
            .ToList();

        Assert.NotEmpty(columns);
        Assert.All(columns, column =>
            Assert.DoesNotContain(_personalWords, word => column.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    private static DirectoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DirectoryDbContext>().UseNpgsql("Host=localhost;Database=x").Options);
}
