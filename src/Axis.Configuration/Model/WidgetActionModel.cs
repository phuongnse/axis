using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled action of a widget: a button with a label. Its kind is its runtime type.
/// <see cref="VisibleWhen"/> is the boolean expression over the row that decides whether a row action shows,
/// or null when the button always shows.
/// </summary>
public abstract record WidgetActionModel(TextReference Label)
{
    public string? VisibleWhen { get; init; }
}
