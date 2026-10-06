namespace Axis.Presentation.Texts;

/// <summary>Provides the text resources of a locale.</summary>
public interface ITextResourceProvider
{
    /// <summary>Returns the texts of <paramref name="locale"/>, ignoring letter case, or null when the locale has none.</summary>
    TextResources? GetTexts(string locale);
}
