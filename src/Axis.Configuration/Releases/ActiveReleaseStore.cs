using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Configuration.Releases;

/// <summary>
/// The active releases stored through <see cref="ConfigurationDbContext"/>. Reads do not track, and
/// writes are raw SQL statements in one transaction, so the context's change tracker is never
/// affected.
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

    [SuppressMessage("Performance", "CA1862:Use the 'StringComparison' method overloads", Justification = "The query must translate to lower(name) to order like the unique index.")]
    public async Task<IReadOnlyList<ActiveRelease>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.ActiveReleases
            .AsNoTracking()
            .OrderBy(row => row.Name.ToLower())
            .Select(row => new ActiveRelease(row.ApplicationId, row.Name, row.ReleaseId, row.ActivatedAt))
            .ToListAsync(cancellationToken);

    public Task<ActiveRelease?> FindBySitePathAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        return context.ActiveSites
            .AsNoTracking()
            .Where(site => site.Path == path)
            .Join(
                context.ActiveReleases.AsNoTracking(),
                site => site.ApplicationId,
                row => row.ApplicationId,
                (site, row) => new ActiveRelease(row.ApplicationId, row.Name, row.ReleaseId, row.ActivatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Release?> GetReleaseAsync(Guid releaseId, CancellationToken cancellationToken = default)
    {
        var release = await context.Releases
            .AsNoTracking()
            .Include(r => r.Resources)
            .SingleOrDefaultAsync(r => r.Id == releaseId, cancellationToken);

        // Database collation may not be ordinal, so resources are put in path order here.
        release?.Resources.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return release;
    }

    public async Task<SetActiveReleaseResult> TrySetAsync(
        Guid applicationId,
        string name,
        Guid releaseId,
        IReadOnlyList<string> sitePaths,
        DateTimeOffset activatedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(sitePaths);

        var paths = sitePaths.ToArray();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // The release row comes first, so the site rows' foreign key target exists on a first
            // activation. One statement, so two activations of the same application never fail on
            // the primary key.
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO axis.active_releases (application_id, name, release_id, activated_at)
                VALUES ({applicationId}, {name}, {releaseId}, {activatedAt.ToUniversalTime()})
                ON CONFLICT (application_id) DO UPDATE
                SET name = EXCLUDED.name, release_id = EXCLUDED.release_id, activated_at = EXCLUDED.activated_at
                """,
                cancellationToken);

            // Paths the release no longer has are freed; paths it keeps stay as they are.
            await context.Database.ExecuteSqlAsync(
                $"DELETE FROM axis.active_sites WHERE application_id = {applicationId} AND NOT (path = ANY({paths}))",
                cancellationToken);

            // One statement per path, so a refused statement names the taken path.
            foreach (var path in paths)
            {
                try
                {
                    await context.Database.ExecuteSqlAsync(
                        $"""
                        INSERT INTO axis.active_sites (application_id, path)
                        VALUES ({applicationId}, {path})
                        ON CONFLICT (application_id, path) DO NOTHING
                        """,
                        cancellationToken);
                }
                catch (PostgresException exception) when (exception is
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                    ConstraintName: ConfigurationDbContext.ActiveSitePathIndex,
                })
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new SetActiveReleaseResult(ActiveReleaseConflict.SitePath, path);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return SetActiveReleaseResult.Set;
        }
        catch (PostgresException exception) when (exception is
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ConfigurationDbContext.ActiveReleaseNameIndex,
        })
        {
            await transaction.RollbackAsync(cancellationToken);
            return new SetActiveReleaseResult(ActiveReleaseConflict.Name);
        }
    }
}
