namespace Axis.Data.Storage;

/// <summary>A row of <c>axis.applied_migrations</c>: a migration that activation applied to the tenant database once.</summary>
public sealed class AppliedMigrationRow
{
    /// <summary>The <c>id</c> of the migration file.</summary>
    public required Guid MigrationId { get; init; }

    /// <summary>The <c>id</c> of the application manifest the migration belongs to.</summary>
    public required Guid ApplicationId { get; init; }

    /// <summary>The release whose activation applied the migration.</summary>
    public required Guid ReleaseId { get; init; }

    /// <summary>The SHA-256 of the migration file's canonical JSON as lower-case hex.</summary>
    public required string ContentHash { get; init; }

    public required DateTimeOffset AppliedAt { get; init; }

    /// <summary>
    /// The columns the migration changed, as a JSON array of <see cref="ChangedColumn"/>. It is <see langword="null"/>
    /// for a row applied before the columns were recorded.
    /// </summary>
    public string? ChangedColumns { get; init; }
}
