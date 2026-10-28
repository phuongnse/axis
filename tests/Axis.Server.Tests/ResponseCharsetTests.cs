using Axis.Server.Http;

namespace Axis.Server.Tests;

public sealed class ResponseCharsetTests
{
    [Theory]
    [InlineData("text/html", false, "text/html; charset=utf-8")]
    [InlineData("application/problem+json", false, "application/problem+json; charset=utf-8")]
    [InlineData("application/json; charset=utf-8", false, "application/json; charset=utf-8")]
    [InlineData("text/html; charset=iso-8859-1", false, "text/html; charset=iso-8859-1")]
    [InlineData("image/png", false, "image/png")]
    [InlineData(null, false, null)]
    [InlineData("text/plain", true, "text/plain")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", false, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public void The_charset_is_added_only_to_text_json_and_xml_that_name_none(string? contentType, bool attachment, string? expected) =>
        Assert.Equal(expected, ResponseCharset.Apply(contentType, attachment));
}
