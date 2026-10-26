using System.Globalization;
using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Data.DataSources;
using Axis.Data.Naming;
using Axis.Expressions.Evaluation;
using Axis.Expressions.Typing;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Data source filters that use <c>any</c> and <c>count</c> over a child collection and over a
/// references field run as SQL against real PostgreSQL. The SQL must keep exactly the rows the
/// interpreter evaluates to <c>true</c> for the same records.
/// </summary>
public sealed class DataSourceSetFilterTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // An order's lines, as the product's name and the quantity. A line can have no product.
    private static readonly (string Title, (string? Product, long Quantity)[] Lines)[] _orders =
    [
        ("One", [("Desk", 1), ("Chair", 1)]),
        ("Two", [("Lamp", 3)]),
        ("Three", [("Desk", 1), ("Desk", 2)]),
        ("Four", []),
        ("Five", [(null, 1)]),
    ];

    // A workstation's software, by product name.
    private static readonly (string Name, string[] Software)[] _workstations =
    [
        ("W1", ["Desk", "Chair", "Lamp"]),
        ("W2", ["Desk", "Chair"]),
        ("W3", ["Chair", "Lamp"]),
        ("W4", []),
        ("W5", ["Desk", "Pen"]),
    ];

    // The Pen has no price, so a comparison with it is never true.
    private static readonly (string Name, decimal? Price)[] _products =
    [
        ("Desk", 5.00m),
        ("Chair", 15.00m),
        ("Lamp", 20.00m),
        ("Pen", null),
    ];

    public static TheoryData<string> OrderFilters =>
    [
        "any(lines, product.name == 'Desk')",
        "count(lines) >= 2",
        "count(lines, product.name != 'Desk') >= 1",
        "count(lines, product.name == 'Desk') == 2",
        "not any(lines, quantity > 1)",
        "any(lines, null)",
        "title == 'One' or any(lines, product.price > 18)",
    ];

    public static TheoryData<string> WorkstationFilters =>
    [
        "count(software, price >= 10) >= 2",
        "any(software, name == 'Pen')",
        "count(software) >= 2",
        "not any(software, price < 10)",
        "any(software, price >= 10) and count(software, price >= 10) < 3",
    ];

    [Theory]
    [MemberData(nameof(OrderFilters))]
    public async Task Filter_over_a_child_collection_keeps_exactly_the_rows_the_interpreter_evaluates_to_true(string filter)
    {
        var seeded = await SeedAsync();

        await AssertSameRowsAsync("Order", "title", filter, seeded);
    }

    [Theory]
    [MemberData(nameof(WorkstationFilters))]
    public async Task Filter_over_a_references_field_keeps_exactly_the_rows_the_interpreter_evaluates_to_true(string filter)
    {
        var seeded = await SeedAsync();

        await AssertSameRowsAsync("Workstation", "name", filter, seeded);
    }

    private async Task AssertSameRowsAsync(string entityName, string fieldName, string filter, Seeded seeded)
    {
        Assert.True(fixture.Model.TryGetEntity(entityName, out var entity));
        Assert.True(entity.TryGetField(fieldName, out var field));
        var compiled = ExpressionModel.Compile(filter, ExpressionScopes.ForDataSource(entity.Fields, [], FindEntity), ExpressionType.Boolean);
        var dataSource = new DataSourceModel
        {
            Id = Guid.NewGuid(),
            Name = "Filtered",
            File = "data-sources/filtered.json",
            Entity = new EntityReference(entity.Id, entity.Name),
            Fields = [new DataSourceFieldModel(fieldName, field)],
            Filter = compiled,
        };

        var records = seeded.Rows(entityName);
        var expected = new List<Guid>();
        foreach (var (id, values) in records)
        {
            var evaluated = ExpressionInterpreter.Evaluate(compiled.Syntax, compiled.Check, new ExpressionValues(values), seeded.Product);
            Assert.True(evaluated.Succeeded, evaluated.Error?.Message);
            if (evaluated.Value is true)
            {
                expected.Add(id);
            }
        }

        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        var page = await DataSourceQueries.ListAsync(connection, null, fixture.Model, dataSource, 1, 100, null, [], cancellationToken: CancellationToken);

        // Every filter but the one that is never true keeps some rows and drops some, so it tells the two back ends apart.
        Assert.InRange(expected.Count, filter == "any(lines, null)" ? 0 : 1, records.Count - 1);
        Assert.NotNull(page);
        Assert.Equal([.. expected.Order()], [.. page.Items.Select(row => row.Id!.Value).Order()]);
        Assert.Equal(expected.Count, page.TotalCount);
    }

    private EntityModel? FindEntity(string name) => fixture.Model.TryGetEntity(name, out var entity) ? entity : null;

    /// <summary>Empties both tenants, then inserts the products, the orders with their lines and the workstations with their software in tenant A.</summary>
    private async Task<Seeded> SeedAsync()
    {
        await fixture.ResetAsync();
        var products = new Dictionary<Guid, Dictionary<string, object?>>();
        var productIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var (name, price) in _products)
        {
            var id = await fixture.InsertAsync(
                TenantA,
                "Product",
                new Dictionary<string, string?> { ["name"] = name, ["price"] = price?.ToString(CultureInfo.InvariantCulture) });
            productIds[name] = id;
            products[id] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = name, ["price"] = price };
        }

        Assert.True(fixture.Model.TryGetEntity("Order", out var order));
        Assert.True(fixture.Model.TryGetEntity("OrderLine", out var orderLine));
        Assert.True(fixture.Model.TryGetEntity("Workstation", out var workstation));
        var lineTable = EntityNaming.QualifiedTable(EntityNaming.Table(orderLine.Id));
        var junction = EntityNaming.QualifiedTable(EntityNaming.Junction(workstation.Id, "software"));

        var orders = new List<(Guid, Dictionary<string, object?>)>();
        foreach (var (title, lines) in _orders)
        {
            var id = await fixture.InsertAsync(TenantA, "Order", new Dictionary<string, string?> { ["title"] = title });
            var rows = new List<ExpressionValues>();
            for (var position = 0; position < lines.Length; position++)
            {
                var (product, quantity) = lines[position];
                var productId = product is null ? (Guid?)null : productIds[product];
                await fixture.ExecuteAsync(
                    TenantA,
                    $"""
                    INSERT INTO {lineTable} ("id", "owner_id", "position", "f_product", "f_quantity")
                    VALUES ('{Guid.NewGuid():D}', '{id:D}', {position}, {(productId is { } value ? $"'{value:D}'" : "NULL")}, {quantity})
                    """);
                rows.Add(new ExpressionValues(new Dictionary<string, object?>
                {
                    ["product"] = productId,
                    ["quantity"] = quantity,
                    ["unitPrice"] = null,
                    ["amount"] = null,
                }));
            }

            orders.Add((id, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["title"] = title,
                ["lines"] = rows,
                ["total"] = null,
                ["lineCount"] = null,
            }));
        }

        var workstations = new List<(Guid, Dictionary<string, object?>)>();
        foreach (var (name, software) in _workstations)
        {
            var id = await fixture.InsertAsync(TenantA, "Workstation", new Dictionary<string, string?> { ["name"] = name });
            for (var position = 0; position < software.Length; position++)
            {
                await fixture.ExecuteAsync(
                    TenantA,
                    $"""INSERT INTO {junction} ("owner_id", "target_id", "position") VALUES ('{id:D}', '{productIds[software[position]]:D}', {position})""");
            }

            workstations.Add((id, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["seats"] = null,
                ["software"] = software.Select(product => new ExpressionValues(products[productIds[product]])).ToList(),
            }));
        }

        return new Seeded(products, orders, workstations);
    }

    /// <summary>The values of the seeded records, as the interpreter reads them.</summary>
    private sealed class Seeded(
        Dictionary<Guid, Dictionary<string, object?>> products,
        List<(Guid Id, Dictionary<string, object?> Values)> orders,
        List<(Guid Id, Dictionary<string, object?> Values)> workstations)
    {
        public List<(Guid Id, Dictionary<string, object?> Values)> Rows(string entity) => entity == "Order" ? orders : workstations;

        public ExpressionValues? Product(string entity, Guid id) =>
            entity.Equals("Product", StringComparison.OrdinalIgnoreCase) && products.TryGetValue(id, out var values)
                ? new ExpressionValues(values)
                : null;
    }
}
