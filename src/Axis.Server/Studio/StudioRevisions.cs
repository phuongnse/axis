using System.Text;
using LibGit2Sharp;

namespace Axis.Server.Studio;

/// <summary>
/// Records the revisions of one application folder in a bare git repository outside the folder, named
/// <c>&lt;folder name&gt;.git</c> under <c>Studio:RevisionsPath</c>. A commit is built in memory from the folder's
/// <c>*.json</c> files, as <see cref="StudioFolder.Files"/> lists them, so git never checks anything out and no
/// <c>.git</c> appears in the folder. A write calls <see cref="RecordOutside"/> once it holds the write lock and
/// <see cref="Record"/> once its files are written. Both log a warning and return when git fails, so a failed
/// revision never fails a write: the next <see cref="RecordOutside"/> records the missed change as an outside change.
/// </summary>
internal sealed partial class StudioRevisions(string repository, string folder, string userId, string displayName, string client, ILogger<StudioRevisions> logger)
{
    /// <summary>The client of every revision the authoring API makes.</summary>
    public const string StudioClient = "studio";

    private const string Branch = "refs/heads/main";

    /// <summary>Loads the native git library, so a server without it fails at start, not on the first save.</summary>
    public static void Start() =>
        // A shared Studio volume may be owned by another user id than the server's. The call loads the library.
        GlobalSettings.SetOwnerValidation(false);

    /// <summary>The revisions of <paramref name="folder"/>, or null when <paramref name="revisionsPath"/> is null.</summary>
    public static StudioRevisions? For(string? revisionsPath, string folder, string userId, string displayName, ILogger<StudioRevisions> logger) =>
        revisionsPath is null
            ? null
            : new StudioRevisions(
                Path.Combine(revisionsPath, Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) + ".git"),
                folder,
                userId,
                displayName,
                StudioClient,
                logger);

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
            var name = Clean(string.IsNullOrWhiteSpace(displayName) ? userId : displayName);
            var author = new Signature(name, Clean(userId), DateTimeOffset.UtcNow);
            WriteCommit(git, author, $"{summary}\n\nAxis-User-Id: {userId}\nAxis-Client: {client}\n", onlyWhenChanged: true);
        }
        catch (Exception exception) when (exception is LibGit2SharpException or IOException or UnauthorizedAccessException)
        {
            Failed(logger, repository, exception.GetType().Name);
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
