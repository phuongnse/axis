using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;

namespace Axis.Data.Schema;

/// <summary>
/// The storage changes for a release: SQL statements in the order they must run and the
/// provisioning records to write with them. Statements and records are empty whenever any
/// diagnostic exists. Pending migration statements come first.
/// </summary>
public sealed record SchemaPlan(
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<string> Statements,
    IReadOnlyList<ProvisionedEntity> NewEntities,
    IReadOnlyList<ProvisionedEnumValue> NewEnumValues)
{
    /// <summary>The changes to the existing provisioning records that the migration statements imply, in order.</summary>
    public IReadOnlyList<MigrationRecordChange> RecordChanges { get; init; } = [];

    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}

/// <summary>
/// A change to the provisioning records. <see cref="MigrationChangeType.RenameField"/> renames the
/// enum values recorded for <see cref="Field"/> to <see cref="To"/>,
/// <see cref="MigrationChangeType.DropField"/> deletes those of <see cref="Field"/>, and
/// <see cref="MigrationChangeType.DropEntity"/> deletes the entity's record and its enum values.
/// </summary>
public sealed record MigrationRecordChange(MigrationChangeType Type, Guid EntityId, string? Field, string? To);
