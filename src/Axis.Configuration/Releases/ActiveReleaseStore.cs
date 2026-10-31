using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Axis.Configuration.Releases;

/// <summary>
/// The active releases stored through <see cref="ConfigurationDbContext"/>. Reads do not track, and
/// writes are raw SQL statements in one transaction, either its own or the caller's, so the context's change tracker is never
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
            .Include(r => r.Packages)
            .SingleOrDefaultAsync(r => r.Id == releaseId, cancellationToken);

        // Database collation may not be ordinal, so resources are put in path order here.
        release?.Resources.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return release;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetComputedFingerprintsAsync(Guid releaseId, CancellationToken cancellationToken = default) =>
        await context.ReleaseComputedFingerprints
            .AsNoTracking()
            .Where(row => row.ReleaseId == releaseId)
            .ToDictionaryAsync(row => row.EntityId, row => row.Fingerprint, cancellationToken);

    public async Task<SetActiveReleaseResult> TrySetAsync(
        Guid applicationId,
        string name,
        Guid releaseId,
        IReadOnlyList<string> sitePaths,
        DateTimeOffset activatedAt,
        ActivationActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(sitePaths);
        ArgumentNullException.ThrowIfNull(actor);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var result = await WriteAsync(applicationId, name, releaseId, sitePaths, activatedAt, actor, cancellationToken);
        if (result.Conflict != ActiveReleaseConflict.None)
        {
            await transaction.RollbackAsync(cancellationToken);
            return result;
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<SetActiveReleaseResult> TrySetInTransactionAsync(
        DbTransaction transaction,
        Guid applicationId,
        string name,
        Guid releaseId,
        IReadOnlyList<string> sitePaths,
        DateTimeOffset activatedAt,
        ActivationActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(sitePaths);
        ArgumentNullException.ThrowIfNull(actor);

        // The caller already enlisted this context, so the transaction stays as it is after the write.
        if (context.Database.CurrentTransaction?.GetDbTransaction() == transaction)
        {
            return await WriteAsync(applicationId, name, releaseId, sitePaths, activatedAt, actor, cancellationToken);
        }

        // EF throws InvalidOperationException here when the transaction is on another connection.
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        try
        {
            return await WriteAsync(applicationId, name, releaseId, sitePaths, activatedAt, actor, cancellationToken);
        }
        finally
        {
            await context.Database.UseTransactionAsync(null, cancellationToken);
        }
    }

    // Runs the statements in the context's current transaction. A conflict is returned, never rolled back.
    private async Task<SetActiveReleaseResult> WriteAsync(
        Guid applicationId,
        string name,
        Guid releaseId,
        IReadOnlyList<string> sitePaths,
        DateTimeOffset activatedAt,
        ActivationActor actor,
        CancellationToken cancellationToken)
    {
        var paths = sitePaths.ToArray();
        try
        {
            // Read before the upsert replaces it: activating the release that is already active adds no history row.
            var previous = await context.Database
                .SqlQuery<Guid>($"SELECT release_id AS \"Value\" FROM axis.active_releases WHERE application_id = {applicationId}")
                .ToListAsync(cancellationToken);

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
                    return new SetActiveReleaseResult(ActiveReleaseConflict.SitePath, path);
                }
            }

            // After the site paths, so a refused path never leaves a row. A release keeps the number of its
            // first activation, and a new one takes the next. The caller's schema lock keeps two activations
            // of one application from taking the same number.
            if (!previous.Contains(releaseId))
            {
                await context.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO axis.release_activations
                        (id, release_id, application_id, number, content_hash, activated_at, actor, label, note, request_id)
                    SELECT {Guid.CreateVersion7()}, r.id, {applicationId},
                        COALESCE(
                            (SELECT h.number FROM axis.release_activations h WHERE h.application_id = {applicationId} AND h.release_id = r.id LIMIT 1),
                            (SELECT COALESCE(max(h.number), 0) + 1 FROM axis.release_activations h WHERE h.application_id = {applicationId})),
                        r.content_hash, {activatedAt.ToUniversalTime()}, {actor.Actor}, {actor.Label}, {actor.Note}, {actor.RequestId}
                    FROM axis.releases r
                    WHERE r.id = {releaseId}
                    """,
                    cancellationToken);
            }

            return SetActiveReleaseResult.Set;
        }
        catch (PostgresException exception) when (exception is
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ConfigurationDbContext.ActiveReleaseNameIndex,
        })
        {
            return new SetActiveReleaseResult(ActiveReleaseConflict.Name);
        }
    }

    public async Task<IReadOnlyList<ReleaseActivation>> ListActivationsAsync(Guid applicationId, CancellationToken cancellationToken = default) =>
        await context.ReleaseActivations
            .AsNoTracking()
            .Where(row => row.ApplicationId == applicationId)
            .OrderByDescending(row => row.ActivatedAt)
            .ThenByDescending(row => row.Id)
            .Select(row => new ReleaseActivation(
                row.Id, row.ReleaseId, row.ApplicationId, row.Number, row.ContentHash, row.ActivatedAt, row.Actor, row.ActorName, row.Label, row.Note, row.RequestId))
            .ToListAsync(cancellationToken);
}
