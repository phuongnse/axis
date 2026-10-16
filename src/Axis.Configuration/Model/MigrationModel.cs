namespace Axis.Configuration.Model;

/// <summary>The kinds of change a migration holds.</summary>
public enum MigrationChangeType
{
    /// <summary>Rename a field's column, keeping its values.</summary>
    RenameField,

    /// <summary>Drop a field's column and its values.</summary>
    DropField,

    /// <summary>Drop the entity's table, its child tables and its provisioning records.</summary>
    DropEntity,

    /// <summary>Fill every null of a field with an expression over the record.</summary>
    Fill,

    /// <summary>Convert a field's stored values to its new type, or map removed enum values to kept ones.</summary>
    Convert,
}

/// <summary>
/// A compiled migration: an ordered list of changes to the storage of one entity. Nothing applies
/// it yet. A change's value stays the expression text, because translating it to SQL needs the
/// stored schema, which is known only when a release is activated.
/// </summary>
public sealed record MigrationModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The migration's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    /// <summary>
    /// The entity's name as the file writes it. The entity may be absent from the release, as when an
    /// earlier migration dropped it.
    /// </summary>
    public required string Entity { get; init; }

    /// <summary>The changes, in file order.</summary>
    public required IReadOnlyList<MigrationChangeModel> Changes { get; init; }
}

/// <summary>
/// One change of a compiled migration. Only the properties of its <see cref="Type"/> are set:
/// <see cref="From"/> and <see cref="To"/> for a rename, <see cref="Field"/> for a drop, and
/// <see cref="Field"/> and <see cref="Value"/> for a fill or convert.
/// </summary>
public sealed record MigrationChangeModel(MigrationChangeType Type, string? Field, string? From, string? To, string? Value);
