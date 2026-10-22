using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>A compiled <c>delete</c> row action of a table widget: a button that deletes the row's record after a confirmation.</summary>
public sealed record DeleteActionModel(TextReference Label) : WidgetActionModel(Label);
