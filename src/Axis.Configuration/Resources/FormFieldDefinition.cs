namespace Axis.Configuration.Resources;

/// <summary>
/// A field of a form section as written: the entity field it shows, whether it is read-only, and
/// the optional conditions, boolean expressions over the form's entity, that say when it is
/// visible, required or read-only.
/// </summary>
public sealed record FormFieldDefinition(
    string Field,
    bool? ReadOnly,
    string? VisibleWhen = null,
    string? RequiredWhen = null,
    string? ReadOnlyWhen = null);
