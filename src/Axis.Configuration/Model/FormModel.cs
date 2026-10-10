using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>A compiled form: one entity's fields laid out in titled sections.</summary>
public sealed record FormModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The form's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    public required EntityReference Entity { get; init; }

    /// <summary>The sections in display order.</summary>
    public required IReadOnlyList<FormSectionModel> Sections { get; init; }
}

/// <summary>A compiled form section: the text key of its title and its fields in display order.</summary>
public sealed record FormSectionModel(TextReference Title, IReadOnlyList<FormFieldModel> Fields);

/// <summary>
/// A compiled form field: the entity field's declared name and whether the form shows it read-only.
/// A computed or numbered field is read-only in any case; <see cref="ReadOnly"/> holds only what the
/// form declares.
/// </summary>
public sealed record FormFieldModel(string Field, bool ReadOnly);
