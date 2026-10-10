using Microsoft.Net.Http.Headers;

namespace Axis.Server.Http;

/// <summary>The content type rule for endpoints that take a JSON body.</summary>
internal static class JsonRequest
{
    /// <summary>
    /// Whether the content type is <c>application/json</c> with at most a <c>utf-8</c> charset.
    /// A cross-site page can send other types, such as <c>text/plain</c>, without a preflight.
    /// </summary>
    public static bool IsJson(HttpRequest request) =>
        MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
        && contentType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        && contentType.Parameters.All(parameter =>
            parameter.Name.Equals("charset", StringComparison.OrdinalIgnoreCase)
            && HeaderUtilities.RemoveQuotes(parameter.Value).Equals("utf-8", StringComparison.OrdinalIgnoreCase));

    public static IResult UnsupportedMediaType() =>
        Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "The request body must be application/json.");
}
