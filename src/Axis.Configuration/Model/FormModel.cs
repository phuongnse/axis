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

    /// <summary>The wizard steps in order. Empty when the form is not a wizard.</summary>
    public IReadOnlyList<FormStepModel> Steps { get; init; } = [];

    /// <summary>Whether any field of the form declares a <c>requiredWhen</c> condition.</summary>
    public bool HasRequiredWhen => Sections.Any(section => section.Fields.Any(formField => formField.RequiredWhen is not null));
}

/// <summary>
/// A compiled form section: the text key of its title, its fields in display order, its name when
/// it has one, and the condition that says when it is visible.
/// </summary>
public sealed record FormSectionModel(
    TextReference Title,
    IReadOnlyList<FormFieldModel> Fields,
    string? Name = null,
    ExpressionModel? VisibleWhen = null);

/// <summary>
/// A compiled form field: the entity field's declared name and whether the form shows it read-only.
/// A computed or numbered field is read-only in any case; <see cref="ReadOnly"/> holds only what the
/// form declares. The conditions are boolean expressions over the form's entity.
/// </summary>
public sealed record FormFieldModel(
    string Field,
    bool ReadOnly,
    ExpressionModel? VisibleWhen = null,
    ExpressionModel? RequiredWhen = null,
    ExpressionModel? ReadOnlyWhen = null);

/// <summary>A compiled wizard step: the text key of its title and the names of its sections as the sections declare them.</summary>
public sealed record FormStepModel(TextReference Title, IReadOnlyList<string> Sections);
