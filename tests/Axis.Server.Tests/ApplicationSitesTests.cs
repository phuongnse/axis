using Axis.Configuration.Compilation;
using Axis.Configuration.Tests;
using Axis.Presentation.Sites;

namespace Axis.Server.Tests;

public sealed class ApplicationSitesTests
{
    [Fact]
    public void Table_over_a_grouped_data_source_lists_the_group_fields_then_the_measures()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), SamplePackages.Versions);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);

        var page = ApplicationSites.FindPage(result.Model, "RequestsByDepartment");

        Assert.NotNull(page);
        var widget = Assert.IsType<WidgetMetadata>(Assert.Single(page.Widgets));
        Assert.Null(widget.Entity);
        Assert.NotNull(widget.DataSource);
        Assert.Equal(
            [("department", "reference", "purchaseRequest.department"), ("requests", "integer", null), ("total", "decimal", null)],
            widget.DataSource.Columns.Select(column => (column.Name, column.Type, column.LabelKey)));
    }

    [Fact]
    public void Money_measures_are_typed_money_and_name_their_fixed_currency_or_the_currency_group_column()
    {
        var root = Path.Combine(Path.GetTempPath(), $"axis-sites-{Guid.NewGuid():N}");
        try
        {
            Write(root, "application.json", """{ "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1 }""");
            Write(root, "entities/expense.json", """
                { "id": "55555555-5555-4555-8555-555555555501", "kind": "entity", "name": "Expense", "formatVersion": 1,
                  "fields": [
                    { "name": "currency", "type": "enum", "values": ["USD", "EUR"] },
                    { "name": "amount", "type": "money", "currencyField": "currency" },
                    { "name": "fixedAmount", "type": "money", "currency": "VND" },
                    { "name": "quantity", "type": "decimal" }
                  ] }
                """);
            Write(root, "data-sources/expense-totals.json", """
                { "id": "99999999-9999-4999-8999-999999999991", "kind": "dataSource", "name": "ExpenseTotals", "formatVersion": 1, "entity": "Expense",
                  "fields": [ { "name": "currency", "path": "currency" }, { "name": "amount", "path": "amount" },
                              { "name": "fixedAmount", "path": "fixedAmount" }, { "name": "quantity", "path": "quantity" } ],
                  "aggregate": { "groupBy": ["currency"], "measures": [
                    { "name": "amountSum", "function": "sum", "field": "amount" },
                    { "name": "amountMax", "function": "max", "field": "amount" },
                    { "name": "fixedSum", "function": "sum", "field": "fixedAmount" },
                    { "name": "quantitySum", "function": "sum", "field": "quantity" } ] } }
                """);

            var result = ApplicationCompiler.Compile(root);
            Assert.Empty(result.Diagnostics);
            Assert.NotNull(result.Model);

            var columns = ApplicationSites.DescribeDataSource(result.Model, "ExpenseTotals").Columns;

            Assert.Equal(
                [
                    ("currency", "enum", null, null),
                    ("amountSum", "money", null, "currency"),
                    ("amountMax", "money", null, "currency"),
                    ("fixedSum", "money", "VND", null),
                    ("quantitySum", "decimal", null, null),
                ],
                columns.Select(column => (column.Name, column.Type, column.Currency, column.CurrencyColumn)));
            var json = System.Text.Json.JsonSerializer.Serialize(columns[4], System.Text.Json.JsonSerializerOptions.Web);
            Assert.DoesNotContain("currency", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Money_fields_and_projected_money_columns_carry_their_currency_or_currency_field()
    {
        var root = Path.Combine(Path.GetTempPath(), $"axis-sites-{Guid.NewGuid():N}");
        try
        {
            Write(root, "application.json", """{ "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1 }""");
            Write(root, "entities/expense.json", """
                { "id": "55555555-5555-4555-8555-555555555501", "kind": "entity", "name": "Expense", "formatVersion": 1,
                  "fields": [
                    { "name": "currency", "type": "enum", "values": ["USD", "EUR"] },
                    { "name": "amount", "type": "money", "currencyField": "currency" },
                    { "name": "fixedAmount", "type": "money", "currency": "VND" },
                    { "name": "quantity", "type": "decimal" }
                  ] }
                """);
            Write(root, "data-sources/expenses.json", """
                { "id": "99999999-9999-4999-8999-999999999992", "kind": "dataSource", "name": "Expenses", "formatVersion": 1, "entity": "Expense",
                  "fields": [ { "name": "currency", "path": "currency" }, { "name": "amount", "path": "amount" },
                              { "name": "fixedAmount", "path": "fixedAmount" }, { "name": "quantity", "path": "quantity" } ] }
                """);

            var result = ApplicationCompiler.Compile(root);
            Assert.Empty(result.Diagnostics);
            Assert.NotNull(result.Model);

            var fields = ApplicationSites.DescribeEntity(result.Model, "Expense").Fields;
            var columns = ApplicationSites.DescribeDataSource(result.Model, "Expenses").Columns;

            Assert.Equal(
                [
                    ("currency", null, null),
                    ("amount", null, "currency"),
                    ("fixedAmount", "VND", null),
                    ("quantity", null, null),
                ],
                fields.Select(field => (field.Name, field.Currency, field.CurrencyField)));
            Assert.Equal(
                [
                    ("currency", null, null),
                    ("amount", null, "currency"),
                    ("fixedAmount", "VND", null),
                    ("quantity", null, null),
                ],
                columns.Select(column => (column.Name, column.Currency, column.CurrencyColumn)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void References_field_names_its_target_as_a_reference_does()
    {
        var root = Path.Combine(Path.GetTempPath(), $"axis-sites-{Guid.NewGuid():N}");
        try
        {
            Write(root, "application.json", """{ "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1 }""");
            Write(root, "entities/software.json", """
                { "id": "55555555-5555-4555-8555-555555555511", "kind": "entity", "name": "Software", "formatVersion": 1, "displayField": "name",
                  "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """);
            Write(root, "entities/asset.json", """
                { "id": "55555555-5555-4555-8555-555555555512", "kind": "entity", "name": "Asset", "formatVersion": 1,
                  "fields": [
                    { "name": "name", "type": "text" },
                    { "name": "software", "type": "references", "target": "Software" }
                  ] }
                """);

            var result = ApplicationCompiler.Compile(root);
            Assert.Empty(result.Diagnostics);
            Assert.NotNull(result.Model);

            var field = ApplicationSites.DescribeEntity(result.Model, "Asset").Fields.Single(field => field.Name == "software");

            Assert.Equal("references", field.Type);
            Assert.NotNull(field.Target);
            Assert.Equal(("Software", "name", "/api/apps/Sample/entities/Software/records"), (field.Target.Entity, field.Target.DisplayField, field.Target.RecordsPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Write(string root, string relativePath, string content)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [Fact]
    public void Form_marks_only_the_purchase_request_number_as_a_sequence_field()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), SamplePackages.Versions);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);

        var page = ApplicationSites.FindPage(result.Model, "PurchaseRequestForm");

        Assert.NotNull(page);
        var widget = Assert.IsType<WidgetMetadata>(Assert.Single(page.Widgets));
        Assert.NotNull(widget.Entity);
        Assert.Equal("number", widget.Entity.Fields[0].Name);
        Assert.True(widget.Entity.Fields[0].Sequence);
        Assert.All(widget.Entity.Fields.Skip(1), field => Assert.False(field.Sequence));
        Assert.All(widget.Entity.Fields.SelectMany(field => field.Fields ?? []), field => Assert.False(field.Sequence));
    }

    [Fact]
    public void Unshaped_access_lists_every_entity_action_and_field_and_a_task_inbox_has_none()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), SamplePackages.Versions);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);

        var table = Assert.IsType<WidgetMetadata>(Assert.Single(ApplicationSites.FindPage(result.Model, "PurchaseRequests")!.Widgets));
        var inbox = Assert.IsType<WidgetMetadata>(Assert.Single(ApplicationSites.FindPage(result.Model, "MyTasks")!.Widgets));

        // The table is over a data source, so its access is about the data source's root entity.
        Assert.Null(table.Entity);
        Assert.NotNull(table.Access);
        Assert.Equal(["read", "create", "update", "delete"], table.Access.Actions);
        result.Model.TryGetEntity("PurchaseRequest", out var entity);
        Assert.Equal(entity!.Fields.Select(field => field.Name), table.Access.CreateFields);
        Assert.Equal(entity.Fields.Select(field => field.Name), table.Access.UpdateFields);
        Assert.Null(inbox.Access);
    }
}
