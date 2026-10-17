using Axis.Configuration.Storage;

namespace Axis.Data;

/// <summary>
/// The check a retry of a <c>failed</c> process instance makes against the migrations applied since its
/// pinned release. A destructive migration never waits for a failed instance, so the retry must not run old steps
/// against a column that a later migration changed.
/// </summary>
public static class RetryMigrationCheck
{
    /// <summary>
    /// Whether an applied migration changed a column that <paramref name="pinned"/> uses. A migration whose file the
    /// pinned release holds was applied before that release was active, so its model already reflects it, and it is
    /// skipped. Any other migration blocks when its changed columns are unknown (<see langword="null"/>), or when
    /// one of them passes <see cref="ReleaseColumns.Uses"/>. A <see langword="null"/> <paramref name="pinned"/> uses every column.
    /// </summary>
    public static bool Blocks(IReadOnlyList<(Guid MigrationId, IReadOnlyList<ChangedColumn>? Changed)> applied, Release? pinned)
    {
        ArgumentNullException.ThrowIfNull(applied);
        var held = pinned is null ? null : ReleaseColumns.MigrationIds(pinned);
        var fields = pinned is null ? null : ReleaseColumns.FieldsByEntityId(pinned);
        foreach (var (migrationId, changed) in applied)
        {
            if (held?.Contains(migrationId) == true)
            {
                continue;
            }

            if (changed is null || changed.Any(column => ReleaseColumns.Uses(fields, column.EntityId, column.Field)))
            {
                return true;
            }
        }

        return false;
    }
}
