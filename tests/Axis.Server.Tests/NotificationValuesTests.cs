using Axis.Processes.Instances;

namespace Axis.Server.Tests;

public sealed class NotificationValuesTests
{
    private static readonly TimeZoneInfo _saigon = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    [Theory]
    [InlineData("vi", "1.234,5")]
    [InlineData("vi-VN", "1.234,5")]
    [InlineData("VI_vn", "1.234,5")]
    [InlineData("en", "1,234.5")]
    [InlineData("de", "1,234.5")]
    [InlineData(null, "1,234.5")]
    public void A_number_uses_the_separators_of_the_locale_and_keeps_every_digit(string? locale, string expected)
    {
        var values = NotificationValues.Format("""{"amount":1234.5}""", """{"amount":"decimal"}""", locale, _saigon);

        Assert.Equal(expected, values["amount"]);
    }

    [Fact]
    public void An_integer_and_a_number_without_a_type_are_formatted_and_keep_their_scale()
    {
        var values = NotificationValues.Format("""{"count":1234567,"price":10.50}""", "{}", "en", _saigon);

        Assert.Equal("1,234,567", values["count"]);
        Assert.Equal("10.50", values["price"]);
    }

    [Theory]
    [InlineData("vi", "01/01/2027 01:00")]
    [InlineData("en", "Jan 1, 2027, 1:00 AM")]
    public void A_date_time_shows_in_the_time_zone(string locale, string expected)
    {
        var values = NotificationValues.Format(
            """{"at":"2026-12-31T18:00:00.000000Z"}""", """{"at":"date-time"}""", locale, _saigon);

        Assert.Equal(expected, values["at"]);
    }

    [Theory]
    [InlineData("vi", "31/12/2026")]
    [InlineData("en", "Dec 31, 2026")]
    public void A_date_shows_as_its_local_date_with_no_shift(string locale, string expected)
    {
        var values = NotificationValues.Format("""{"on":"2026-12-31"}""", """{"on":"date"}""", locale, _saigon);

        Assert.Equal(expected, values["on"]);
    }

    [Fact]
    public void Text_enums_booleans_and_null_show_as_written_and_unparsable_dates_stay_raw()
    {
        var values = NotificationValues.Format(
            """{"a":"2026-12-31","b":"open","c":true,"d":null,"e":"not a date"}""",
            """{"a":"text","b":"enum","c":"boolean","e":"date-time"}""",
            "vi",
            _saigon);

        Assert.Equal(["2026-12-31", "open", "true", "", "not a date"], [values["a"], values["b"], values["c"], values["d"], values["e"]]);
    }
}
