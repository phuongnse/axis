namespace Axis.Configuration.Resources;

/// <summary>A form section as written: its title and its fields in display order.</summary>
public sealed record FormSectionDefinition(TextReference Title, IReadOnlyList<FormFieldDefinition> Fields);
