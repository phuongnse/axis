using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Expressions.Sql;

namespace Axis.Data.Schema;

/// <summary>
/// The storage changes for a release: commands in the order they must run and the
/// provisioning records to write with them. Commands and records are empty whenever any
/// diagnostic exists. Pending migration commands come first.
/// </summary>
public sealed record SchemaPlan(
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<SchemaCommand> Commands,
    IReadOnlyList<ProvisionedEntity> NewEntities,
    IReadOnlyList<ProvisionedEnumValue> NewEnumValues)
{
    /// <summary>The changes to the existing provisioning records that the migration statements imply, in order.</summary>
    public IReadOnlyList<MigrationRecordChange> RecordChanges { get; init; } = [];

    /// <summary>
    /// The destructive changes the commands make: a rename, a drop or a type change of a column, or a dropped table.
    /// Activation checks them against the work that is still running on older releases.
    /// </summary>
    public IReadOnlyList<DestructiveChange> DestructiveChanges { get; init; } = [];

    /// <summary>
    /// The entities whose rows a <c>fill</c> or <c>convert</c> changes, or whose child rows it changes. The commands clear
    /// the release stamp of those rows, and activation recomputes the rows with a cleared stamp after the commands.
    /// </summary>
    public IReadOnlyList<Guid> RecomputedEntityIds { get; init; } = [];

    /// <summary>The SQL text of each command, in order.</summary>
    public IReadOnlyList<string> Statements => [.. Commands.Select(command => command.Sql)];

    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}

/// <summary>
/// A change to the provisioning records. <see cref="MigrationChangeType.RenameField"/> renames the
/// enum values recorded for <see cref="Field"/> to <see cref="To"/>,
/// <see cref="MigrationChangeType.DropField"/> deletes those of <see cref="Field"/>, and
/// <see cref="MigrationChangeType.DropEntity"/> deletes the entity's record and its enum values, and
/// <see cref="MigrationChangeType.Convert"/> deletes the enum <see cref="Values"/> recorded for <see cref="Field"/>.
/// </summary>
public sealed record MigrationRecordChange(MigrationChangeType Type, Guid EntityId, string? Field, string? To)
{
    /// <summary>The enum values a convert removes from the records.</summary>
    public IReadOnlyList<string> Values { get; init; } = [];
}

/// <summary>
/// A change of a pending migration that renames, drops or converts a column of the entity with id <see cref="EntityId"/>
/// (<see cref="Field"/> names the column's field), or drops its table (<see cref="Field"/> is <see langword="null"/>).
/// </summary>
public sealed record DestructiveChange(MigrationModel Migration, Guid EntityId, string? Field);

/// <summary>One SQL statement of a plan, with the parameters its text names.</summary>
public sealed record SchemaCommand(string Sql)
{
    public IReadOnlyList<SqlValue> Parameters { get; init; } = [];

    /// <summary>Set on a fill: the command returns the number of rows it left null.</summary>
    public FillTarget? Fill { get; init; }

    /// <summary>Set on a statement of a convert: a failing statement, or a count above zero, names the change.</summary>
    public ConvertTarget? Convert { get; init; }
}

/// <summary>The change of a pending <c>fill</c> migration, which names it in a diagnostic.</summary>
public sealed record FillTarget(Guid MigrationId, string File, int ChangeIndex, string Field);

/// <summary>
/// The change of a pending <c>convert</c> migration, which names it in a diagnostic. When
/// <see cref="CountsDisallowed"/> is set, the command returns the number of rows left with a value the field does not list.
/// </summary>
public sealed record ConvertTarget(Guid MigrationId, string File, int ChangeIndex, string Field, bool CountsDisallowed);
