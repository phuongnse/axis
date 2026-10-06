using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Configuration.Releases;

/// <summary>
/// The active releases stored through <see cref="ConfigurationDbContext"/>. Reads do not track, and
/// writes are single raw SQL statements, so the context's change tracker is never affected.
/// </summary>
public sealed class ActiveReleaseStore(ConfigurationDbContext context) : IActiveReleaseStore
{
    [SuppressMessage("Performance", "CA1862:Use the 'StringComparison' method overloads", Justification = "The query must translate to lower(name) to use the unique index.")]
    public Task<ActiveRelease?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Names are ASCII, so lower() in the database matches a case-insensitive .NET comparison,
        // and the query can use the lower(name) unique index.
        var lowerName = name.ToLowerInvariant();
        return context.ActiveReleases
            .AsNoTracking()
            .Where(row => row.Name.ToLower() == lowerName)
            .Select(row => new ActiveRelease(row.ApplicationId, row.Name, row.ReleaseId, row.ActivatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<ActiveRelease?> FindByApplicationIdAsync(Guid applicationId, CancellationToken cancellationToken = default) =>
        context.ActiveReleases
            .AsNoTracking()
            .Where(row => row.ApplicationId == applicationId)
            .Select(row => new ActiveRelease(row.ApplicationId, row.Name, row.ReleaseId, row.ActivatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> TrySetAsync(
        Guid applicationId,
        string name,
        Guid releaseId,
        DateTimeOffset activatedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        try
        {
            // One statement, so two activations of the same application never fail on the primary key.
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO axis.active_releases (application_id, name, release_id, activated_at)
                VALUES ({applicationId}, {name}, {releaseId}, {activatedAt.ToUniversalTime()})
                ON CONFLICT (application_id) DO UPDATE
                SET name = EXCLUDED.name, release_id = EXCLUDED.release_id, activated_at = EXCLUDED.activated_at
                """,
                cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception is
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ConfigurationDbContext.ActiveReleaseNameIndex,
        })
        {
            return false;
        }
    }
}
