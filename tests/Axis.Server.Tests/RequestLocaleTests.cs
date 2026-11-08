using Axis.Presentation.Texts;
using Axis.Server.Presentation;
using Microsoft.AspNetCore.Http;

namespace Axis.Server.Tests;

public sealed class RequestLocaleTests
{
    private readonly EmbeddedTextResourceProvider _texts = new();

    [Theory]
    [InlineData("vi", "vi")]
    [InlineData("vi-VN", "vi")]
    [InlineData("fr, vi;q=0.5", "vi")]
    [InlineData("en;q=0.2, vi;q=0.9", "vi")]
    [InlineData("vi;q=0, en;q=0.1", "en")]
    [InlineData("*, vi;q=0.5", "vi")]
    [InlineData("fr", "en")]
    [InlineData("*", "en")]
    public void The_first_language_by_quality_that_the_platform_has_chooses_the_texts(string header, string expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = header;

        Assert.Equal(expected, RequestLocale.Choose(context.Request, _texts).Locale);
    }

    [Fact]
    public void No_header_chooses_english()
    {
        Assert.Equal("en", RequestLocale.Choose(new DefaultHttpContext().Request, _texts).Locale);
    }
}
