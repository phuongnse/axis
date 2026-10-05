using Axis.Configuration.Model;
using Axis.Data.Schema;
using static Axis.Data.Tests.Models;

namespace Axis.Data.Tests;

public sealed class ColumnTypesTests
{
    [Theory]
    [InlineData(FieldType.Text, null, null, null, "text")]
    [InlineData(FieldType.Text, 200, null, null, "character varying(200)")]
    [InlineData(FieldType.Integer, null, null, null, "bigint")]
    [InlineData(FieldType.Decimal, null, null, null, "numeric")]
    [InlineData(FieldType.Decimal, null, 18, null, "numeric(18,0)")]
    [InlineData(FieldType.Decimal, null, 18, 2, "numeric(18,2)")]
    [InlineData(FieldType.Boolean, null, null, null, "boolean")]
    [InlineData(FieldType.Date, null, null, null, "date")]
    [InlineData(FieldType.DateTime, null, null, null, "timestamp with time zone")]
    public void Field_types_render_as_format_type_spells_them(FieldType type, int? maxLength, int? precision, int? scale, string expected)
    {
        Assert.Equal(expected, ColumnTypes.Render(Field("f", type, maxLength: maxLength, precision: precision, scale: scale)));
    }

    [Fact]
    public void Enum_renders_as_text_and_reference_as_uuid()
    {
        var target = Entity(Guid.NewGuid(), "Target", "target.json", Field("name", FieldType.Text));

        Assert.Equal("text", ColumnTypes.Render(Field("f", FieldType.Enum, values: ["a", "b"])));
        Assert.Equal("uuid", ColumnTypes.Render(Field("f", FieldType.Reference, target: target)));
    }

    [Theory]
    [InlineData("character varying(1)", true, 1)]
    [InlineData("character varying(10485760)", true, 10485760)]
    [InlineData("character varying", false, 0)]
    [InlineData("character varying()", false, 0)]
    [InlineData("character varying(-1)", false, 0)]
    [InlineData("text", false, 0)]
    [InlineData("character(10)", false, 0)]
    public void Character_varying_lengths_are_parsed(string type, bool parsed, int length)
    {
        Assert.Equal((parsed, length), (ColumnTypes.TryParseVarying(type, out var actual), actual));
    }
}
