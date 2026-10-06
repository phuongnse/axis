using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Configuration.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Configuration.Releases;

/// <summary>
/// Compiles an application folder into an immutable release in the tenant database. A folder with
/// errors produces no release. An unchanged folder returns the release already stored for its
/// application id and content hash. A stored release can be compiled back into its model.
/// </summary>
public static class ReleaseCompiler
{
    /// <summary>
    /// Compiles <paramref name="folderPath"/> and stores the release through
    /// <paramref name="context"/>, which the caller connects to the tenant database. Call it outside
    /// an explicit transaction: when a concurrent compile stores the same release first, the
    /// context's change tracker is cleared and the stored release is returned.
    /// </summary>
    public static async Task<ReleaseCompilationResult> CompileAsync(
        string folderPath,
        ConfigurationDbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var compiled = ApplicationCompiler.Compile(folderPath);
        if (compiled.Model is null || compiled.ContentHash is null)
        {
            return new ReleaseCompilationResult(null, null, compiled.Diagnostics);
        }

        var applicationId = compiled.Model.Manifest.Id;
        var contentHash = compiled.ContentHash;
        var existing = await FindAsync(context, applicationId, contentHash, cancellationToken);
        if (existing is not null)
        {
            return new ReleaseCompilationResult(existing, compiled.Model, compiled.Diagnostics);
        }

        var releaseId = Guid.CreateVersion7();
        var release = new Release
        {
            Id = releaseId,
            ApplicationId = applicationId,
            ContentHash = contentHash,
            CreatedAt = DateTimeOffset.UtcNow,
            Resources = compiled.Resources
                .Select(resource => new ReleaseResource { ReleaseId = releaseId, Path = resource.Path, Content = resource.Content })
                .ToList(),
        };
        context.Releases.Add(release);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new ReleaseCompilationResult(release, compiled.Model, compiled.Diagnostics);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ConfigurationDbContext.ReleaseIdentityIndex,
        })
        {
            // Another compile stored the same release first. Forget the failed insert so the
            // context stays usable, and return the stored release.
            context.ChangeTracker.Clear();
            existing = await FindAsync(context, applicationId, contentHash, cancellationToken)
                ?? throw new InvalidOperationException("The release that violated the unique index could not be found.", exception);
            return new ReleaseCompilationResult(existing, compiled.Model, compiled.Diagnostics);
        }
    }

    /// <summary>
    /// Rebuilds the model of <paramref name="release"/> from its stored resources, without the
    /// folder. A release is stored only when it compiles clean, so any diagnostic, another content
    /// hash or another application id means the stored rows are wrong.
    /// </summary>
    /// <exception cref="InvalidOperationException">The stored release does not compile back into the same application.</exception>
    public static ApplicationModel BuildModel(Release release)
    {
        ArgumentNullException.ThrowIfNull(release);

        var compiled = ApplicationCompiler.Compile(
            release.Resources.Select(resource => new ResourceContent(resource.Path, resource.Content)).ToList());
        if (compiled.Diagnostics.Count > 0 || compiled.Model is null)
        {
            var code = compiled.Diagnostics.Count > 0 ? compiled.Diagnostics[0].Code : "none";
            throw new InvalidOperationException(
                $"The stored release '{release.Id}' no longer compiles (first diagnostic: {code}).");
        }

        if (compiled.ContentHash != release.ContentHash)
        {
            throw new InvalidOperationException(
                $"The stored release '{release.Id}' compiles to another content hash than the one stored with it.");
        }

        if (compiled.Model.Manifest.Id != release.ApplicationId)
        {
            throw new InvalidOperationException(
                $"The stored release '{release.Id}' compiles to another application id than the one stored with it.");
        }

        return compiled.Model;
    }

    private static async Task<Release?> FindAsync(
        ConfigurationDbContext context,
        Guid applicationId,
        string contentHash,
        CancellationToken cancellationToken)
    {
        var release = await context.Releases
            .Include(r => r.Resources)
            .SingleOrDefaultAsync(r => r.ApplicationId == applicationId && r.ContentHash == contentHash, cancellationToken);

        // Database collation may not be ordinal, so resources are put in path order here.
        release?.Resources.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return release;
    }
}
