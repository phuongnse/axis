namespace Axis.Configuration.Resources;

/// <summary>A field of a form section as written: the entity field it shows and whether it is read-only.</summary>
public sealed record FormFieldDefinition(string Field, bool? ReadOnly);
