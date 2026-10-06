namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>text</c> resource: the texts of one locale, as a map from text key to text. Keys are
/// compared ordinally, as the SPA does.
/// </summary>
public sealed record TextResource : Resource
{
    public required string Locale { get; init; }

    public required IReadOnlyDictionary<string, string> Texts { get; init; }
}
