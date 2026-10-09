using Axis.Configuration.Model;
using Axis.Data.DataSources;

namespace Axis.Data.Tests;

public sealed class DataSourceParameterValuesTests
{
    private const string Id = "6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7";

    [Theory]
    [InlineData(FieldType.Text, "abc")]
    [InlineData(FieldType.Text, "O'Brien; --100%")]
    [InlineData(FieldType.Integer, "-5")]
    [InlineData(FieldType.Integer, "9223372036854775807")]
    [InlineData(FieldType.Decimal, "1250.50")]
    [InlineData(FieldType.Decimal, "-3")]
    [InlineData(FieldType.Boolean, "true")]
    [InlineData(FieldType.Boolean, "false")]
    [InlineData(FieldType.Date, "2026-10-08")]
    [InlineData(FieldType.DateTime, "2026-10-08T09:30:00+07:00")]
    [InlineData(FieldType.Enum, "open")]
    [InlineData(FieldType.Reference, Id)]
    public void Each_type_accepts_its_text_form(FieldType type, string text)
    {
        Assert.True(DataSourceParameterValues.TryParse(Parameter(type), text, out var value));
        Assert.NotNull(value);
    }

    [Fact]
    public void Values_have_the_clr_type_of_the_parameter_type()
    {
        Assert.Equal("O'Brien", Parse(FieldType.Text, "O'Brien"));
        Assert.Equal(-5L, Parse(FieldType.Integer, "-5"));
        Assert.Equal(1250.50m, Parse(FieldType.Decimal, "1250.50"));
        Assert.Equal(true, Parse(FieldType.Boolean, "true"));
        Assert.Equal(false, Parse(FieldType.Boolean, "false"));
        Assert.Equal(new DateOnly(2026, 10, 8), Parse(FieldType.Date, "2026-10-08"));
        Assert.Equal("open", Parse(FieldType.Enum, "open"));
        Assert.Equal(Guid.Parse(Id), Parse(FieldType.Reference, Id));

        // A date-time is the UTC instant.
        var dateTime = Assert.IsType<DateTimeOffset>(Parse(FieldType.DateTime, "2026-10-08T09:30:00+07:00"));
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 2, 30, 0, TimeSpan.Zero), dateTime);
        Assert.Equal(TimeSpan.Zero, dateTime.Offset);
    }

    [Theory]
    [InlineData(FieldType.Text, "a\0b")]
    [InlineData(FieldType.Integer, "+5")]
    [InlineData(FieldType.Integer, "5.0")]
    [InlineData(FieldType.Integer, " 5")]
    [InlineData(FieldType.Integer, "1e3")]
    [InlineData(FieldType.Integer, "9223372036854775808")]
    [InlineData(FieldType.Integer, "٥")]
    [InlineData(FieldType.Decimal, "1e3")]
    [InlineData(FieldType.Decimal, "1.")]
    [InlineData(FieldType.Decimal, ".5")]
    [InlineData(FieldType.Decimal, "+1.5")]
    [InlineData(FieldType.Decimal, "1,5")]
    [InlineData(FieldType.Boolean, "yes")]
    [InlineData(FieldType.Boolean, "TRUE")]
    [InlineData(FieldType.Boolean, "1")]
    [InlineData(FieldType.Date, "2026-13-01")]
    [InlineData(FieldType.Date, "2026-10-8")]
    [InlineData(FieldType.Date, "2026-10-08T00:00:00Z")]
    [InlineData(FieldType.DateTime, "2026-10-08T09:30:00")]
    [InlineData(FieldType.DateTime, "2026-10-08")]
    [InlineData(FieldType.Enum, "closed")]
    [InlineData(FieldType.Enum, "Open")]
    [InlineData(FieldType.Reference, "6f1c2a3b4d5e4f608a7192b3c4d5e6f7")]
    [InlineData(FieldType.Reference, "{6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7}")]
    [InlineData(FieldType.Reference, "nope")]
    public void Each_type_rejects_other_text(FieldType type, string text)
    {
        Assert.False(DataSourceParameterValues.TryParse(Parameter(type), text, out var value));
        Assert.Null(value);
    }

    private static object? Parse(FieldType type, string text)
    {
        Assert.True(DataSourceParameterValues.TryParse(Parameter(type), text, out var value));
        return value;
    }

    private static DataSourceParameterModel Parameter(FieldType type) =>
        new("p", type, Required: false, Label: null, Values: type == FieldType.Enum ? ["open"] : null, Target: null);
}
