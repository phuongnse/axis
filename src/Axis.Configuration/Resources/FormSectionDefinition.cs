namespace Axis.Configuration.Resources;

/// <summary>
/// A form section as written: its title, its fields in display order, its optional name, which a
/// step uses, and an optional condition that says when it is visible.
/// </summary>
public sealed record FormSectionDefinition(
    TextReference Title,
    IReadOnlyList<FormFieldDefinition> Fields,
    string? Name = null,
    string? VisibleWhen = null);
