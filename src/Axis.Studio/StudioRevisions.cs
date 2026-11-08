using System.Text;
using System.Text.Json.Nodes;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;

namespace Axis.Studio;

/// <summary>
/// Records the revisions of one application folder in a bare git repository outside the folder, named
/// <c>&lt;folder name&gt;.git</c> under <c>Studio:RevisionsPath</c>. A commit is built in memory from the folder's
/// <c>*.json</c> files, as <see cref="StudioFolder.Files"/> lists them, so git never checks anything out and no
/// <c>.git</c> appears in the folder. A write calls <see cref="RecordOutside"/> once it holds the write lock and
/// <see cref="Record"/> once its files are written. Both log a warning and return when git fails, so a failed
/// revision never fails a write: the next <see cref="RecordOutside"/> records the missed change as an outside change.
/// <see cref="List"/>, <see cref="Diff"/> and <see cref="Revert"/> read the history of <c>main</c>, which is linear because
/// only this class writes it.
/// </summary>
internal sealed partial class StudioRevisions(string repository, string folder, string userId, string displayName, string client, ILogger<StudioRevisions> logger)
{
    /// <summary>The client of every revision the authoring API makes.</summary>
    public const string StudioClient = "studio";

    private const string Branch = "refs/heads/main";

    private const string Customizations = "customizations/";

    /// <summary>Loads the native git library, so a server without it fails at start, not on the first save.</summary>
    public static void Start() =>
        // A shared Studio volume may be owned by another user id than the server's. The call loads the library.
        GlobalSettings.SetOwnerValidation(false);

    /// <summary>The revisions of <paramref name="folder"/>, or null when <paramref name="revisionsPath"/> is null.</summary>
    public static StudioRevisions? For(string? revisionsPath, string folder, string userId, string displayName, ILogger<StudioRevisions> logger) =>
        RepositoryPath(revisionsPath, folder) is { } repository
            ? new StudioRevisions(repository, folder, userId, displayName, StudioClient, logger)
            : null;

    /// <summary>The path of the bare repository of <paramref name="folder"/>, or null when <paramref name="revisionsPath"/> is null.</summary>
    public static string? RepositoryPath(string? revisionsPath, string folder) =>
        revisionsPath is null
            ? null
            : Path.Combine(revisionsPath, Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) + ".git");

    /// <summary>
    /// Records the folder as it is. Without a repository this creates it with the revision "Start of history".
    /// Otherwise a folder that differs from the tip of <c>main</c> gets the revision "Outside change". Both name the
    /// server and no user.
    /// </summary>
    public void RecordOutside()
    {
        try
        {
            var server = new Signature("Axis", "axis", DateTimeOffset.UtcNow);
            if (!Repository.IsValid(repository))
            {
                Directory.CreateDirectory(repository);
                Repository.Init(repository, isBare: true);
            }

            using var git = new Repository(repository);
            git.Refs.Add("HEAD", Branch, allowOverwrite: true);
            if (git.Refs[Branch]?.ResolveToDirectReference().Target is not Commit)
            {
                WriteCommit(git, server, "Start of history", onlyWhenChanged: false);
            }
            else
            {
                WriteCommit(git, server, "Outside change", onlyWhenChanged: true);
            }
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or UnauthorizedAccessException)
        {
            Failed(logger, repository, exception.GetType().Name);
        }
    }

    /// <summary>
    /// Records the folder as the revision <paramref name="summary"/> by the user, when the folder differs from the tip
    /// of <c>main</c>. The message holds the summary and the trailers <c>Axis-User-Id</c> and <c>Axis-Client</c>.
    /// </summary>
    public void Record(string summary)
    {
        try
        {
            using var git = new Repository(repository);
            WriteCommit(git, Author(userId, displayName), $"{summary}\n\nAxis-User-Id: {userId}\nAxis-Client: {client}\n", onlyWhenChanged: true);
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or UnauthorizedAccessException)
        {
            Failed(logger, repository, exception.GetType().Name);
        }
    }

    /// <summary>The revisions on <c>main</c>, newest first. Empty when the folder has no history yet.</summary>
    public IReadOnlyList<StudioRevision> List()
    {
        if (!Repository.IsValid(repository))
        {
            return [];
        }

        using var git = new Repository(repository);
        return [.. Chain(git).Select(Describe)];
    }

    /// <summary>Whether <paramref name="id"/> is a commit on <c>main</c>.</summary>
    public bool Has(string id)
    {
        if (!Repository.IsValid(repository))
        {
            return false;
        }

        using var git = new Repository(repository);
        return Find(git, id) is not null;
    }

    /// <summary>The files revision <paramref name="id"/> added, changed or removed, or null when it is not a commit on <c>main</c>.</summary>
    public StudioRevisionDiff? Diff(string id)
    {
        if (!Repository.IsValid(repository))
        {
            return null;
        }

        using var git = new Repository(repository);
        if (Find(git, id) is not { } commit)
        {
            return null;
        }

        return new StudioRevisionDiff(Describe(commit), Compare(git, commit.Parents.FirstOrDefault()?.Tree, commit.Tree));
    }

    /// <summary>
    /// What undoing revision <paramref name="id"/> takes: the later revisions that changed any of its files, newest
    /// first, and otherwise each file with the bytes its parent had, or null bytes when the parent lacked it. Null
    /// when <paramref name="id"/> is not a commit on <c>main</c>.
    /// </summary>
    public StudioRevert? Revert(string id)
    {
        if (!Repository.IsValid(repository))
        {
            return null;
        }

        using var git = new Repository(repository);
        if (Find(git, id) is not { } commit)
        {
            return null;
        }

        var tip = Chain(git).First();
        var summary = commit.MessageShort;
        if (commit.Parents.FirstOrDefault() is not { } parent)
        {
            return new StudioRevert(true, summary, tip.Sha, [], []);
        }

        var paths = Changes(git, commit).Select(change => change.Path).ToHashSet(StringComparer.Ordinal);
        var blockers = new List<StudioRevision>();
        foreach (var later in Chain(git).TakeWhile(later => later.Sha != commit.Sha))
        {
            if (Changes(git, later).Any(change => paths.Contains(change.Path)))
            {
                blockers.Add(Describe(later));
            }
        }

        var files = blockers.Count > 0
            ? []
            : paths.Order(StringComparer.Ordinal).Select(path => new StudioRevertFile(path, Bytes(parent.Tree, path))).ToList();
        return new StudioRevert(false, summary, tip.Sha, blockers, files);
    }

    // The commits of main from the tip along the first parent.
    private static IEnumerable<Commit> Chain(Repository git)
    {
        for (var commit = git.Refs[Branch]?.ResolveToDirectReference().Target as Commit; commit is not null; commit = commit.Parents.FirstOrDefault())
        {
            yield return commit;
        }
    }

    private static Commit? Find(Repository git, string id) =>
        Chain(git).FirstOrDefault(commit => string.Equals(commit.Sha, id, StringComparison.OrdinalIgnoreCase));

    // Rename detection is off, so a moved file is one removed and one added.
    private static TreeChanges Changes(Repository git, Commit commit) =>
        git.Diff.Compare<TreeChanges>(commit.Parents.FirstOrDefault()?.Tree, commit.Tree, new CompareOptions { Similarity = SimilarityOptions.None });

    /// <summary>
    /// The files that differ between two trees, sorted by file. A tree of null is empty. A moved file is one removed and
    /// one added. A draft's diff uses it too.
    /// </summary>
    internal static List<StudioRevisionChange> Compare(Repository git, Tree? before, Tree after)
    {
        var resources = new List<StudioRevisionChange>();
        foreach (var change in git.Diff.Compare<TreeChanges>(before, after, new CompareOptions { Similarity = SimilarityOptions.None }))
        {
            var was = Text(before, change.Path);
            var now = Text(after, change.Path);
            var (resourceId, kind, name) = Identity(now ?? was);
            resources.Add(new StudioRevisionChange(
                change.Path,
                change.Status switch { ChangeKind.Added => "added", ChangeKind.Deleted => "removed", _ => "changed" },
                resourceId,
                kind,
                name,
                was,
                now,
                Target(change.Path)));
        }

        resources.Sort((left, right) => string.CompareOrdinal(left.File, right.File));
        return resources;
    }

    // The module resource a layer file changes, <module id>/<name>, named by its path under customizations/.
    private static string? Target(string file) =>
        file.StartsWith(Customizations, StringComparison.Ordinal) && file.EndsWith(".json", StringComparison.Ordinal) && file.Length > Customizations.Length + ".json".Length
            ? file[Customizations.Length..^".json".Length]
            : null;

    private static StudioRevision Describe(Commit commit)
    {
        string? Trailer(string name) =>
            commit.Message.Split('\n').FirstOrDefault(line => line.StartsWith(name + ": ", StringComparison.Ordinal)) is { } line
                ? line[(name.Length + 2)..].Trim()
                : null;

        return new StudioRevision(
            commit.Sha, commit.MessageShort, commit.Author.Name, Trailer("Axis-User-Id"), Trailer("Axis-Client"), null, commit.Author.When);
    }

    private static string? Text(Tree? tree, string path) => (tree?[path]?.Target as Blob)?.GetContentText();

    private static byte[]? Bytes(Tree tree, string path)
    {
        if (tree[path]?.Target is not Blob blob)
        {
            return null;
        }

        using var stream = blob.GetContentStream();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // The id, kind and name of a resource file, each null when the text does not parse or lacks it.
    private static (string? Id, string? Kind, string? Name) Identity(string? text)
    {
        if (text is null)
        {
            return (null, null, null);
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonObject content)
            {
                return (null, null, null);
            }

            string? Property(string name) => content[name] is JsonValue value && value.TryGetValue<string>(out var read) ? read : null;
            return (Property("id"), Property("kind"), Property("name"));
        }
        catch (System.Text.Json.JsonException)
        {
            return (null, null, null);
        }
    }

    // Commits the files of the folder to main. With onlyWhenChanged, nothing is committed when the tree is the tip's.
    private void WriteCommit(Repository git, Signature signature, string message, bool onlyWhenChanged)
    {
        var tip = git.Refs[Branch]?.ResolveToDirectReference().Target as Commit;
        var definition = new TreeDefinition();
        foreach (var (file, bytes) in StudioFolder.Files(folder))
        {
            using var content = new MemoryStream(bytes);
            definition.Add(file, git.ObjectDatabase.CreateBlob(content), Mode.NonExecutableFile);
        }

        var tree = git.ObjectDatabase.CreateTree(definition);
        if (onlyWhenChanged && tip is not null && tip.Tree.Sha == tree.Sha)
        {
            return;
        }

        var commit = git.ObjectDatabase.CreateCommit(
            signature, signature, message, tree, tip is null ? [] : [tip], prettifyMessage: false);
        git.Refs.Add(Branch, commit.Id, allowOverwrite: true);
    }

    /// <summary>The git signature of a commit by the user, named by the display name or else the user id.</summary>
    internal static Signature Author(string userId, string displayName) =>
        new(Clean(string.IsNullOrWhiteSpace(displayName) ? userId : displayName), Clean(userId), DateTimeOffset.UtcNow);

    // A git signature cannot hold angle brackets or a line break.
    private static string Clean(string value)
    {
        var cleaned = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is not ('<' or '>' or '\r' or '\n'))
            {
                cleaned.Append(character);
            }
        }

        var text = cleaned.ToString().Trim();
        return text.Length == 0 ? "axis" : text;
    }

    [LoggerMessage(Microsoft.Extensions.Logging.LogLevel.Warning, "The revision of the folder could not be recorded in the repository {Repository}: {Exception}. The next write records it as an outside change.")]
    private static partial void Failed(ILogger logger, string repository, string exception);
}

/// <summary>One revision: its commit id, summary, author name, the user id and client of its trailers, the authoring request (always null for now) and its time.</summary>
internal sealed record StudioRevision(string Id, string Summary, string Author, string? UserId, string? Client, string? Request, DateTimeOffset Time);

/// <summary>
/// One file a revision changed. <c>Change</c> is <c>added</c>, <c>changed</c> or <c>removed</c>. <c>Before</c> and <c>After</c>
/// are the file's text, null on the side where it does not exist. <c>Id</c>, <c>Kind</c> and <c>Name</c> are read from the file.
/// <c>Target</c> is the module resource a layer file changes, <c>&lt;module id&gt;/&lt;name&gt;</c>, and null for any other file.
/// </summary>
internal sealed record StudioRevisionChange(string File, string Change, string? Id, string? Kind, string? Name, string? Before, string? After, string? Target);

/// <summary>A revision and the files it changed, sorted by file.</summary>
internal sealed record StudioRevisionDiff(StudioRevision Revision, IReadOnlyList<StudioRevisionChange> Resources);

/// <summary>A file an undo puts back: its bytes before the revision, or null bytes when the file did not exist.</summary>
internal sealed record StudioRevertFile(string File, byte[]? Bytes);

/// <summary>
/// What undoing a revision takes. <c>IsStart</c> is set for the first revision, which cannot be undone. <c>Blockers</c> are
/// the later revisions that changed the same files, newest first. <c>Tip</c> is the id of the tip of <c>main</c>.
/// </summary>
internal sealed record StudioRevert(bool IsStart, string Summary, string Tip, IReadOnlyList<StudioRevision> Blockers, IReadOnlyList<StudioRevertFile> Files);
