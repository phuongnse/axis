using Microsoft.Net.Http.Headers;

namespace Axis.Server.Http;

/// <summary>Adds <c>charset=utf-8</c> to the text, JSON and XML types that name none, and leaves attachments alone.</summary>
internal static class ResponseCharset
{
    public static string? Apply(string? contentType, bool attachment)
    {
        if (string.IsNullOrEmpty(contentType)
            || attachment
            || !MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
            || !string.IsNullOrEmpty(mediaType.Charset.Value))
        {
            return contentType;
        }

        return IsText(mediaType) ? $"{contentType}; charset=utf-8" : contentType;
    }

    private static bool IsText(MediaTypeHeaderValue mediaType) =>
        mediaType.Type.Equals("text", StringComparison.OrdinalIgnoreCase)
        || mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        || mediaType.MediaType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
        || mediaType.MediaType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase)
        || mediaType.Suffix.Equals("json", StringComparison.OrdinalIgnoreCase)
        || mediaType.Suffix.Equals("xml", StringComparison.OrdinalIgnoreCase);
}
