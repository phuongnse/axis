namespace Axis.Configuration.Model;

/// <summary>
/// A compiled widget. <see cref="FormPage"/> is set only on a table widget that names the page
/// holding the form for its records.
/// </summary>
public sealed record WidgetModel(WidgetType Type, EntityReference Entity, PageReference? FormPage);
