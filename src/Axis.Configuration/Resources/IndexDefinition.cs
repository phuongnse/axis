namespace Axis.Configuration.Resources;

/// <summary>
/// An index as written in the entity file. Whether <see cref="Fields"/> names fields of the entity
/// that an index can hold, and whether another index lists the same fields in the same order, is
/// checked by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record IndexDefinition
{
    /// <summary>The names of the fields the index covers, in order.</summary>
    public required IReadOnlyList<string> Fields { get; init; }

    /// <summary>Whether no two records may hold the same values in <see cref="Fields"/>. Absent means false.</summary>
    public bool? Unique { get; init; }
}
