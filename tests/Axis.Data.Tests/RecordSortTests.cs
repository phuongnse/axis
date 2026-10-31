using Axis.Configuration.Model;
using Axis.Data.Records;
using static Axis.Data.Tests.Models;

namespace Axis.Data.Tests;

public sealed class RecordSortTests
{
    private static readonly EntityModel _entity = Entity(
        Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1a01"),
        "Order",
        "entities/order.json",
        Field("name", FieldType.Text),
        Field("count", FieldType.Integer),
        Field("lines", FieldType.ChildCollection, target: Entity(Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1a02"), "Line", "entities/line.json")));

    [Theory]
    [InlineData("name", false)]
    [InlineData("-name", true)]
    public void Declared_name_parses_to_its_field_and_a_leading_minus_to_descending(string text, bool descending)
    {
        Assert.True(RecordSort.TryParse(text, _entity, out var sort));

        Assert.Same(_entity.Fields[0], sort.Field);
        Assert.Equal(descending, sort.Descending);
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("--name")]
    [InlineData("missing")]
    [InlineData("lines")]
    public void Other_text_does_not_parse(string text)
    {
        Assert.False(RecordSort.TryParse(text, _entity, out var sort));

        Assert.Null(sort);
    }

    [Theory]
    [InlineData("secret", false)]
    [InlineData("-secret", true)]
    public void Sensitive_field_does_not_parse_and_the_same_field_that_is_not_sensitive_does(string text, bool descending)
    {
        var plain = Entity(
            Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1a03"), "Order", "entities/order.json", Field("secret", FieldType.Text));
        var sensitive = plain with { Fields = [plain.Fields[0] with { Sensitive = true }] };

        Assert.False(RecordSort.TryParse(text, sensitive, out var refused));
        Assert.Null(refused);
        Assert.True(RecordSort.TryParse(text, plain, out var accepted));
        Assert.Equal(descending, accepted.Descending);
    }
}
