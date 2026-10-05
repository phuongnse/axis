using Axis.Configuration.Diagnostics;

namespace Axis.Data.Schema;

/// <summary>
/// The storage changes for a release: SQL statements in the order they must run and the
/// provisioning records to write with them. Statements and records are empty whenever any
/// diagnostic exists.
/// </summary>
public sealed record SchemaPlan(
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<string> Statements,
    IReadOnlyList<ProvisionedEntity> NewEntities,
    IReadOnlyList<ProvisionedEnumValue> NewEnumValues)
{
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
