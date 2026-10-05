using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>A compiled entity with typed fields.</summary>
public sealed record EntityModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public TextReference? Label { get; init; }

    /// <summary>The entity's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    public required IReadOnlyList<FieldModel> Fields { get; init; }

    /// <summary>Finds a field by name, ignoring letter case.</summary>
    public bool TryGetField(string name, [NotNullWhen(true)] out FieldModel? field)
    {
        field = Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return field is not null;
    }
}
