using Axis.Configuration.Model;
using Axis.Data.DataSources;
using static Axis.Data.Tests.Models;

namespace Axis.Data.Tests;

public sealed class DataSourceSortTests
{
    private static readonly EntityModel _customer = Entity(
        Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1a02"),
        "Customer",
        "entities/customer.json",
        "name",
        Field("name", FieldType.Text, required: true));

    private static readonly EntityModel _order = Entity(
        Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1a01"),
        "Order",
        "entities/order.json",
        Field("name", FieldType.Text),
        Field("count", FieldType.Integer),
        Field("customer", FieldType.Reference, target: _customer),
        Field("lines", FieldType.ChildCollection, target: _customer));

    private static readonly DataSourceModel _dataSource = new()
    {
        Id = Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1a03"),
        Name = "Orders",
        File = "data-sources/orders.json",
        Entity = new EntityReference(_order.Id, _order.Name),
        Fields =
        [
            new DataSourceFieldModel("title", _order.Fields[0]),
            new DataSourceFieldModel("customer", _order.Fields[2]),

            // The compiler rejects a projected child collection; the parser does not rely on it.
            new DataSourceFieldModel("lines", _order.Fields[3]),
        ],
        Sort = new DataSourceSortModel("title", Descending: true),
    };

    [Theory]
    [InlineData("title", false)]
    [InlineData("-title", true)]
    public void Projected_name_parses_to_its_field_and_a_leading_minus_to_descending(string text, bool descending)
    {
        Assert.True(DataSourceSort.TryParse(text, _dataSource, out var sort));

        Assert.Same(_dataSource.Fields[0], sort.Field);
        Assert.Equal(descending, sort.Descending);
    }

    [Theory]
    [InlineData("Title")]
    [InlineData("name")]
    [InlineData("count")]
    [InlineData("customer")]
    [InlineData("-customer")]
    [InlineData("lines")]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("--title")]
    public void Unprojected_reference_child_collection_or_other_text_does_not_parse(string text)
    {
        Assert.False(DataSourceSort.TryParse(text, _dataSource, out var sort));

        Assert.Null(sort);
    }

    [Fact]
    public void Default_is_the_declared_sort_or_none()
    {
        var sort = DataSourceSort.Default(_dataSource);

        Assert.NotNull(sort);
        Assert.Same(_dataSource.Fields[0], sort.Field);
        Assert.True(sort.Descending);
        Assert.Null(DataSourceSort.Default(_dataSource with { Sort = null }));
    }
}
