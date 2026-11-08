using Axis.Server.Studio;
using Axis.Studio;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axis.Integration.Tests;

/// <summary>
/// Gives an application folder in a tenant's Studio folder a real revisions repository in <c>.revisions</c>, as Studio makes it.
/// </summary>
internal static class TenantRevisions
{
    /// <summary>
    /// Writes <c>&lt;tenantFolder&gt;/&lt;app&gt;/application.json</c> and records two revisions: "Start of history" and one change.
    /// Returns the commit SHAs of <c>main</c>, oldest first.
    /// </summary>
    public static async Task<List<string>> BuildAsync(string tenantFolder, string app, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(tenantFolder, app);
        var manifest = Path.Combine(folder, "application.json");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(manifest, $$"""{"name":"{{app}}","version":1}""", cancellationToken);
        var revisions = For(tenantFolder, app);
        revisions.RecordOutside();
        await File.WriteAllTextAsync(manifest, $$"""{"name":"{{app}}","version":2}""", cancellationToken);
        revisions.Record("Change application");
        return CommitShas(Path.Combine(tenantFolder, ".revisions", app + ".git"));
    }

    /// <summary>The revisions of the application folder <paramref name="app"/> in the Studio folder <paramref name="tenantFolder"/>.</summary>
    public static StudioRevisions For(string tenantFolder, string app) =>
        StudioRevisions.For(
            Path.Combine(tenantFolder, ".revisions"),
            Path.Combine(tenantFolder, app),
            "author",
            "Author User",
            NullLogger<StudioRevisions>.Instance)!;

    /// <summary>The commit SHAs of <c>main</c> in the repository at <paramref name="repository"/>, oldest first.</summary>
    public static List<string> CommitShas(string repository)
    {
        using var git = new Repository(repository);
        return [.. Commits(git).Select(commit => commit.Sha)];
    }

    /// <summary>The commits of <c>main</c>, oldest first.</summary>
    public static List<Commit> Commits(Repository repository) =>
        [.. repository.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = "refs/heads/main", SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Reverse })];
}
