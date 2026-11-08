using Axis.Presentation.Texts;

namespace Axis.Server.Presentation;

/// <summary>Chooses the platform texts that a request's <c>Accept-Language</c> header asks for.</summary>
internal static class RequestLocale
{
    private const string FallbackLocale = "en";

    /// <summary>
    /// The texts of the first language of the header, by quality, that the platform has. A language matches by its whole tag,
    /// or else by its primary subtag, so <c>vi-VN</c> gives <c>vi</c>. English when none matches.
    /// </summary>
    internal static TextResources Choose(HttpRequest request, ITextResourceProvider texts)
    {
        var languages = request.GetTypedHeaders().AcceptLanguage
            .Where(language => language.Value.Value != "*" && language.Quality != 0)
            .OrderByDescending(language => language.Quality ?? 1);
        foreach (var language in languages)
        {
            var tag = language.Value.ToString();
            if (texts.GetTexts(tag) is { } whole)
            {
                return whole;
            }

            if (texts.GetTexts(tag.Split('-')[0]) is { } primary)
            {
                return primary;
            }
        }

        return texts.GetTexts(FallbackLocale)!;
    }
}
