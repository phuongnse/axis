using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>A compiled action of a widget: a button with a label. Its kind is its runtime type.</summary>
public abstract record WidgetActionModel(TextReference Label);
