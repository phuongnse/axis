namespace Axis.Presentation.Texts;

/// <summary>The texts of one locale, as a flat map from text key to text.</summary>
public sealed record TextResources(string Locale, IReadOnlyDictionary<string, string> Texts);
