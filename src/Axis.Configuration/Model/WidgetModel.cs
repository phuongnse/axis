namespace Axis.Configuration.Model;

/// <summary>
/// A compiled widget. Exactly one of <see cref="Entity"/> and <see cref="DataSource"/> is set: a
/// widget shows all records of an entity, or the rows of a data source. <see cref="Form"/> is set
/// only on a form widget that names a form, and <see cref="Entity"/> is then the form's entity.
/// <see cref="FormPage"/> is set only on a table widget that names the page holding the form for
/// its records.
/// </summary>
public sealed record WidgetModel(
    WidgetType Type,
    EntityReference? Entity,
    PageReference? FormPage,
    DataSourceReference? DataSource = null,
    FormReference? Form = null);
