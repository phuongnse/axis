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

    /// <summary>The SQL text of each command, in order.</summary>
    public IReadOnlyList<string> Statements => [.. Commands.Select(command => command.Sql)];

    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}

/// <summary>
/// A change to the provisioning records. <see cref="MigrationChangeType.RenameField"/> renames the
/// enum values recorded for <see cref="Field"/> to <see cref="To"/>,
/// <see cref="MigrationChangeType.DropField"/> deletes those of <see cref="Field"/>, and
/// <see cref="MigrationChangeType.DropEntity"/> deletes the entity's record and its enum values.
/// </summary>
public sealed record MigrationRecordChange(MigrationChangeType Type, Guid EntityId, string? Field, string? To);

/// <summary>One SQL statement of a plan, with the parameters its text names.</summary>
public sealed record SchemaCommand(string Sql)
{
    public IReadOnlyList<SqlValue> Parameters { get; init; } = [];

    /// <summary>Set on a fill: the command returns the number of rows it left null.</summary>
    public FillTarget? Fill { get; init; }
}

/// <summary>The change of a pending <c>fill</c> migration, which names it in a diagnostic.</summary>
public sealed record FillTarget(Guid MigrationId, string File, int ChangeIndex, string Field);
