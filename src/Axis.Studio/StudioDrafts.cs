using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibGit2Sharp;

namespace Axis.Studio;

/// <summary>
/// The drafts of one person on one application folder. A draft is the branch <c>refs/heads/drafts/&lt;id&gt;</c> in the
/// repository <see cref="StudioRevisions"/> keeps, and its record is a JSON blob under <c>refs/axis/drafts/&lt;id&gt;</c> in
/// the same repository: the owner, the commit it started at, its creation time and, for each resource it first read or
/// changed, the version that resource had on <c>main</c>, or null when <c>main</c> lacked it. A write or delete commits
/// to the branch from memory, so the working folder, <c>main</c> and other drafts never change. Every operation holds
/// the folder's write lock, except <see cref="Conflicts"/>, <see cref="Apply"/> and <see cref="CommitAccept"/>, which
/// expect the caller to hold it. A draft that is not this person's answers <see cref="StudioWriteRefusal.NotFound"/>, as an
/// unknown id does.
/// </summary>
internal sealed class StudioDrafts(string repository, string userId, string displayName, IStudioWriteLock writeLock, string folder)
{
    private const string Main = "refs/heads/main";

    private const string ManifestFile = "application.json";

    /// <summary>The most bytes a page of a diff holds, as the server serializes it. A single resource larger than this fills a page alone.</summary>
    internal const int DiffPageBytes = 1024 * 1024;

    // Room in a page for the start, the tip, the cursor and the braces around the resources.
    private const int PageEnvelopeBytes = 1024;

    /// <summary>The drafts of the user on <paramref name="folder"/>, or null when <paramref name="revisionsPath"/> is null.</summary>
    public static StudioDrafts? For(string? revisionsPath, string folder, string userId, string displayName, IStudioWriteLock writeLock) =>
        StudioRevisions.RepositoryPath(revisionsPath, folder) is { } repository
            ? new StudioDrafts(repository, userId, displayName, writeLock, folder)
            : null;

    /// <summary>
    /// Makes a draft at the tip of <c>main</c>, after <paramref name="revisions"/> records any outside change in the
    /// folder. Answers <see cref="StudioWriteRefusal.NotFound"/> and makes nothing when <c>main</c> has no commit.
    /// </summary>
    public Task<StudioDraftOutcome<StudioDraft>> CreateAsync(StudioRevisions revisions, CancellationToken ct) =>
        RunAsync(() =>
        {
            revisions.RecordOutside();
            if (!Repository.IsValid(repository))
            {
                return StudioDraftOutcome<StudioDraft>.Refused(StudioWriteRefusal.NotFound);
            }

            using var git = new Repository(repository);
            if (git.Refs[Main]?.ResolveToDirectReference().Target is not Commit tip)
            {
                return StudioDraftOutcome<StudioDraft>.Refused(StudioWriteRefusal.NotFound);
            }

            var id = Guid.NewGuid().ToString("D");
            git.Refs.Add(BranchName(id), tip.Id);
            var draft = new StudioDraft(id, userId, tip.Sha, DateTimeOffset.UtcNow, new Dictionary<Guid, string?>());
            Save(git, draft);
            return StudioDraftOutcome<StudioDraft>.Of(draft);
        }, ct);

    /// <summary>The record of the draft <paramref name="id"/>.</summary>
    public Task<StudioDraftOutcome<StudioDraft>> FindAsync(string id, CancellationToken ct) =>
        InDraftAsync(id, (_, draft, _) => StudioDraftOutcome<StudioDraft>.Of(draft), ct);

    /// <summary>The resources of the draft as its branch holds them. Records no version.</summary>
    public Task<StudioDraftOutcome<IReadOnlyList<StudioFile>>> ListAsync(string id, CancellationToken ct) =>
        InDraftAsync(id, (_, _, tip) => StudioDraftOutcome<IReadOnlyList<StudioFile>>.Of(Resources(tip)), ct);

    /// <summary>
    /// The path and bytes of every <c>*.json</c> file of the draft's branch, as its tree holds them, so a compile reads the draft
    /// as it reads a folder. Records no version.
    /// </summary>
    public Task<StudioDraftOutcome<IReadOnlyList<(string File, byte[] Bytes)>>> FilesAsync(string id, CancellationToken ct) =>
        InDraftAsync(id, (_, _, tip) =>
        {
            var files = new List<(string File, byte[] Bytes)>();
            Walk(tip.Tree, "", files);
            return StudioDraftOutcome<IReadOnlyList<(string File, byte[] Bytes)>>.Of(files);
        }, ct);

    /// <summary>
    /// The files the draft added, changed or removed since the <c>main</c> commit it started at, as a page of whole entries
    /// that serializes to at most <see cref="DiffPageBytes"/>, or one larger entry alone. Without a <paramref name="cursor"/>
    /// the page starts at the first entry and reads the branch tip. A cursor names the commit the first page was read at and
    /// the next entry, so later writes to the draft do not shift later pages. A cursor this draft did not give is refused as
    /// <see cref="StudioWriteRefusal.DoesNotApply"/>. Records no version.
    /// </summary>
    public Task<StudioDraftOutcome<StudioDraftDiff>> DiffAsync(string id, string? cursor, CancellationToken ct) =>
        InDraftAsync(id, (git, draft, tip) =>
        {
            var at = tip;
            var from = 0;
            if (cursor is not null)
            {
                if (ReadCursor(cursor) is not var (sha, index) || OnBranch(draft, tip, sha) is not { } pinned)
                {
                    return StudioDraftOutcome<StudioDraftDiff>.Refused(StudioWriteRefusal.DoesNotApply);
                }

                at = pinned;
                from = index;
            }

            var start = git.Lookup<Commit>(draft.Start);
            var entries = StudioRevisions.Compare(git, start?.Tree, at.Tree);
            if (cursor is not null && from >= entries.Count)
            {
                return StudioDraftOutcome<StudioDraftDiff>.Refused(StudioWriteRefusal.DoesNotApply);
            }

            var size = PageEnvelopeBytes;
            var next = from;
            while (next < entries.Count)
            {
                size += JsonSerializer.SerializeToUtf8Bytes(entries[next], JsonSerializerOptions.Web).Length + 1;
                if (next > from && size > DiffPageBytes)
                {
                    break;
                }

                next++;
            }

            var after = next < entries.Count ? WriteCursor(at.Sha, next) : null;
            return StudioDraftOutcome<StudioDraftDiff>.Of(new StudioDraftDiff(draft.Start, at.Sha, entries.GetRange(from, next - from), after));
        }, ct);

    /// <summary>The resource as the draft holds it. The draft records the version the resource has on <c>main</c> on its first use.</summary>
    public Task<StudioDraftOutcome<StudioFile>> ReadAsync(string id, Guid resource, CancellationToken ct) =>
        InDraftAsync(id, (git, draft, tip) =>
        {
            if (Resources(tip).FirstOrDefault(file => file.Id == resource) is not { } current)
            {
                return StudioDraftOutcome<StudioFile>.Refused(StudioWriteRefusal.NotFound);
            }

            Remember(git, draft, resource);
            return StudioDraftOutcome<StudioFile>.Of(current);
        }, ct);

    /// <summary>
    /// Commits the full <paramref name="content"/> of a resource to the draft. It replaces the file when the draft has
    /// the content's id, and otherwise puts it at the path a create would use. A write may not change the kind of a
    /// resource the draft has, which is refused as <see cref="StudioWriteRefusal.ChangesIdOrKind"/>.
    /// </summary>
    public Task<StudioDraftOutcome<StudioFile>> WriteAsync(string id, JsonObject content, CancellationToken ct) =>
        InDraftAsync(id, (git, draft, tip) =>
        {
            if (!IsResource(content, out var resource))
            {
                return StudioDraftOutcome<StudioFile>.Refused(StudioWriteRefusal.DoesNotApply);
            }

            var files = Resources(tip);
            var existing = files.FirstOrDefault(file => file.Id == resource);
            string path;
            string summary;
            if (existing is not null)
            {
                if (!string.Equals(existing.Kind, (string?)content["kind"], StringComparison.Ordinal))
                {
                    return StudioDraftOutcome<StudioFile>.Refused(StudioWriteRefusal.ChangesIdOrKind);
                }

                path = existing.File;
                summary = StudioWrites.ResourceSummary("Change", existing);
            }
            else
            {
                if (StudioFileNames.NewFile(content, out _, out _) is not { } created)
                {
                    return StudioDraftOutcome<StudioFile>.Refused(StudioWriteRefusal.UnsafePath);
                }

                if (tip.Tree[created] is not null)
                {
                    return StudioDraftOutcome<StudioFile>.Refused(StudioWriteRefusal.Exists);
                }

                path = created;
                summary = StudioWrites.CreateSummary(content, created);
            }

            Remember(git, draft, resource);
            var bytes = StudioWrites.Serialize(content);
            var definition = TreeDefinition.From(tip.Tree);
            definition.Add(path, CreateBlob(git, bytes), Mode.NonExecutableFile);
            Commit(git, draft, tip, definition, summary);
            return StudioDraftOutcome<StudioFile>.Of(StudioFolder.Resources([(path, bytes)]).Single());
        }, ct);

    /// <summary>
    /// Commits the layer of the module resource <paramref name="name"/> of <paramref name="moduleId"/> to the draft, holding
    /// <paramref name="changes"/>. It replaces the draft's layer for that resource when there is one, and otherwise puts a new
    /// one at <c>customizations/&lt;module&gt;/&lt;name&gt;.json</c>. Null <paramref name="changes"/> removes the draft's layer,
    /// or commits nothing when it has none, and answers no file and no version.
    /// </summary>
    public Task<StudioDraftOutcome<StudioDraftLayer>> WriteLayerAsync(
        string id, string moduleId, string name, JsonObject? changes, CancellationToken ct) =>
        InDraftAsync(id, (git, draft, tip) =>
        {
            var existing = StudioWrites.FindLayer(Resources(tip), moduleId, name);
            var path = existing?.File.File ?? $"customizations/{moduleId}/{name}.json";
            if (changes is null)
            {
                if (existing is not null)
                {
                    Remember(git, draft, existing.File.Id);
                    var removal = TreeDefinition.From(tip.Tree);
                    removal.Remove(path);
                    Commit(git, draft, tip, removal, StudioWrites.LayerSummary(moduleId, name, null));
                }

                return StudioDraftOutcome<StudioDraftLayer>.Of(new StudioDraftLayer(null, null));
            }

            if (existing is null && tip.Tree[path] is not null)
            {
                return StudioDraftOutcome<StudioDraftLayer>.Refused(StudioWriteRefusal.Exists);
            }

            var layer = existing?.File.Id ?? Guid.NewGuid();
            Remember(git, draft, layer);
            var bytes = StudioWrites.Serialize(StudioWrites.LayerObject(layer, existing?.Target ?? $"{moduleId}/{name}", changes));
            var definition = TreeDefinition.From(tip.Tree);
            definition.Add(path, CreateBlob(git, bytes), Mode.NonExecutableFile);
            Commit(git, draft, tip, definition, StudioWrites.LayerSummary(moduleId, name, changes));
            return StudioDraftOutcome<StudioDraftLayer>.Of(
                new StudioDraftLayer(path, StudioFolder.Resources([(path, bytes)]).Single().Version));
        }, ct);

    /// <summary>Commits the removal of a resource's file to the draft. The manifest cannot be deleted.</summary>
    public Task<StudioDraftOutcome<bool>> DeleteAsync(string id, Guid resource, CancellationToken ct) =>
        InDraftAsync(id, (git, draft, tip) =>
        {
            if (Resources(tip).FirstOrDefault(file => file.Id == resource) is not { } current)
            {
                return StudioDraftOutcome<bool>.Refused(StudioWriteRefusal.NotFound);
            }

            if (current.File == ManifestFile)
            {
                return StudioDraftOutcome<bool>.Refused(StudioWriteRefusal.Manifest);
            }

            Remember(git, draft, resource);
            var definition = TreeDefinition.From(tip.Tree);
            definition.Remove(current.File);
            Commit(git, draft, tip, definition, StudioWrites.ResourceSummary("Delete", current));
            return StudioDraftOutcome<bool>.Of(true);
        }, ct);

    /// <summary>Deletes the draft's branch and its record.</summary>
    public Task<StudioDraftOutcome<bool>> DropAsync(string id, CancellationToken ct) =>
        InDraftAsync(id, (git, draft, _) =>
        {
            git.Refs.Remove(BranchName(draft.Id));
            git.Refs.Remove(RecordName(draft.Id));
            return StudioDraftOutcome<bool>.Of(true);
        }, ct);

    /// <summary>
    /// The resources the draft read or changed whose version on <c>main</c> now differs from the one it recorded, sorted by
    /// resource id. Each comes with its version on <c>main</c>, or null when <c>main</c> no longer has it. A resource
    /// recorded as absent that now exists is one too. The caller holds the folder's write lock. Records no version.
    /// </summary>
    public StudioDraftOutcome<IReadOnlyList<StudioDraftConflict>> Conflicts(string id) =>
        InDraft(id, (git, draft, _) =>
        {
            var main = git.Refs[Main]?.ResolveToDirectReference().Target as Commit;
            var current = main is null ? [] : Resources(main);
            var conflicts = new List<StudioDraftConflict>();
            foreach (var (resource, recorded) in draft.Versions)
            {
                var version = current.FirstOrDefault(file => file.Id == resource)?.Version;
                if (!string.Equals(version, recorded, StringComparison.Ordinal))
                {
                    conflicts.Add(new StudioDraftConflict(resource, version));
                }
            }

            conflicts.Sort((left, right) => left.Id.CompareTo(right.Id));
            return StudioDraftOutcome<IReadOnlyList<StudioDraftConflict>>.Of(conflicts);
        });

    /// <summary>
    /// The <paramref name="folderFiles"/> with the files the draft added or changed since its start put in and the files it
    /// removed taken out, sorted by file. Nothing is written, so the folder, <c>main</c> and the draft stay as they were.
    /// The caller holds the folder's write lock.
    /// </summary>
    public StudioDraftOutcome<IReadOnlyList<(string File, byte[] Bytes)>> Apply(string id, IReadOnlyList<(string File, byte[] Bytes)> folderFiles) =>
        InDraft(id, (git, draft, tip) =>
        {
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var (file, bytes) in folderFiles)
            {
                files[file] = bytes;
            }

            var start = git.Lookup<Commit>(draft.Start);
            foreach (var change in git.Diff.Compare<TreeChanges>(start?.Tree, tip.Tree, new LibGit2Sharp.CompareOptions { Similarity = SimilarityOptions.None }))
            {
                if (change.Status == ChangeKind.Deleted)
                {
                    files.Remove(change.Path);
                }
                else if (tip.Tree[change.Path]?.Target is Blob blob)
                {
                    using var stream = blob.GetContentStream();
                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);
                    files[change.Path] = buffer.ToArray();
                }
            }

            var applied = files.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => (entry.Key, entry.Value)).ToList();
            return StudioDraftOutcome<IReadOnlyList<(string File, byte[] Bytes)>>.Of(applied);
        });

    /// <summary>
    /// Records the folder as one revision on <c>main</c> that names the user, the <paramref name="client"/> and the authoring
    /// request <paramref name="requestId"/>, even when the folder already matches <c>main</c>, then deletes the draft's
    /// branch and record. Returns the revision. The caller holds the folder's write lock and has written the applied files
    /// to the folder.
    /// </summary>
    public StudioDraftOutcome<StudioRevision> CommitAccept(
        string id, string summary, string byUserId, string byDisplayName, string client, Guid requestId) =>
        InDraft(id, (git, draft, _) =>
        {
            var commit = StudioRevisions.WriteCommit(
                git,
                folder,
                StudioRevisions.Author(byUserId, byDisplayName),
                StudioRevisions.Message(summary, byUserId, client, requestId),
                onlyWhenChanged: false)!;
            git.Refs.Remove(BranchName(draft.Id));
            git.Refs.Remove(RecordName(draft.Id));
            return StudioDraftOutcome<StudioRevision>.Of(StudioRevisions.Describe(commit));
        });

    // A cursor is the base64url of "<commit id>:<index of the next entry>". Null when it is not that, or the index is below 1.
    private static (string Sha, int Index)? ReadCursor(string cursor)
    {
        try
        {
            var text = Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars(cursor));
            var parts = text.Split(':');
            return parts.Length == 2
                && parts[0].Length == 40
                && parts[0].All(char.IsAsciiHexDigitLower)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index >= 1
                    ? (parts[0], index)
                    : null;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static string WriteCursor(string sha, int index) =>
        System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{sha}:{index}")));

    // The commit with this id on the draft's own branch, from the tip back to the start, or null.
    private static Commit? OnBranch(StudioDraft draft, Commit tip, string sha)
    {
        for (var commit = tip; commit is not null; commit = commit.Parents.FirstOrDefault())
        {
            if (commit.Sha == sha)
            {
                return commit;
            }

            if (commit.Sha == draft.Start)
            {
                break;
            }
        }

        return null;
    }

    private static string BranchName(string id) => $"refs/heads/drafts/{id}";

    private static string RecordName(string id) => $"refs/axis/drafts/{id}";

    // Takes the write lock, then runs the action, which is refused as Unavailable when the lock cannot be taken.
    private async Task<StudioDraftOutcome<T>> RunAsync<T>(Func<StudioDraftOutcome<T>> action, CancellationToken ct)
    {
        if (await StudioWrites.TryTakeAsync(writeLock, folder, ct) is not { } held)
        {
            return StudioDraftOutcome<T>.Refused(StudioWriteRefusal.Unavailable);
        }

        await using var _ = held;
        return action();
    }

    // Runs the action on a draft of this user. An id that is no draft of this user is NotFound, so another person's draft is never touched.
    private Task<StudioDraftOutcome<T>> InDraftAsync<T>(
        string id, Func<Repository, StudioDraft, Commit, StudioDraftOutcome<T>> action, CancellationToken ct) =>
        RunAsync(() => InDraft(id, action), ct);

    // As InDraftAsync, without taking the write lock: the caller holds it.
    private StudioDraftOutcome<T> InDraft<T>(string id, Func<Repository, StudioDraft, Commit, StudioDraftOutcome<T>> action)
    {
        if (!Repository.IsValid(repository))
        {
            return StudioDraftOutcome<T>.Refused(StudioWriteRefusal.NotFound);
        }

        using var git = new Repository(repository);
        return Load(git, id) is var (draft, tip)
            ? action(git, draft, tip)
            : StudioDraftOutcome<T>.Refused(StudioWriteRefusal.NotFound);
    }

    // The record and the branch tip of this user's draft, or null.
    private (StudioDraft Draft, Commit Tip)? Load(Repository git, string id)
    {
        if (!Guid.TryParseExact(id, "D", out var parsed))
        {
            return null;
        }

        var name = parsed.ToString("D");
        if (git.Refs[RecordName(name)]?.ResolveToDirectReference().Target is not Blob blob
            || Parse(blob.GetContentText()) is not { } draft
            || !string.Equals(draft.UserId, userId, StringComparison.Ordinal)
            || git.Refs[BranchName(name)]?.ResolveToDirectReference().Target is not Commit tip)
        {
            return null;
        }

        return (draft, tip);
    }

    private static StudioDraft? Parse(string text)
    {
        try
        {
            if (JsonNode.Parse(text) is not JsonObject record
                || record["id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id)
                || record["userId"] is not JsonValue userValue || !userValue.TryGetValue<string>(out var user)
                || record["start"] is not JsonValue startValue || !startValue.TryGetValue<string>(out var start)
                || record["created"] is not JsonValue createdValue || !createdValue.TryGetValue<string>(out var created)
                || !DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
                || record["versions"] is not JsonObject versions)
            {
                return null;
            }

            var read = new Dictionary<Guid, string?>();
            foreach (var (key, value) in versions)
            {
                if (!Guid.TryParseExact(key, "D", out var resource))
                {
                    return null;
                }

                read[resource] = value is JsonValue version && version.TryGetValue<string>(out var hex) ? hex : null;
            }

            return new StudioDraft(id, user, start, time, read);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static void Save(Repository git, StudioDraft draft)
    {
        var versions = new JsonObject();
        foreach (var (resource, version) in draft.Versions.OrderBy(entry => entry.Key))
        {
            versions[resource.ToString("D")] = version;
        }

        var record = new JsonObject
        {
            ["id"] = draft.Id,
            ["userId"] = draft.UserId,
            ["start"] = draft.Start,
            ["created"] = draft.Created.ToString("O", CultureInfo.InvariantCulture),
            ["versions"] = versions,
        };
        git.Refs.Add(RecordName(draft.Id), CreateBlob(git, Encoding.UTF8.GetBytes(record.ToJsonString())).Id, allowOverwrite: true);
    }

    // Records the version of the resource on main when the draft has none for it. An existing entry is never changed.
    private static void Remember(Repository git, StudioDraft draft, Guid resource)
    {
        if (draft.Versions.ContainsKey(resource))
        {
            return;
        }

        var main = git.Refs[Main]?.ResolveToDirectReference().Target as Commit;
        var version = main is null ? null : Resources(main).FirstOrDefault(file => file.Id == resource)?.Version;
        Save(git, draft with { Versions = new Dictionary<Guid, string?>(draft.Versions) { [resource] = version } });
    }

    // Adds a commit to the branch, unless the tree is the tip's.
    private void Commit(Repository git, StudioDraft draft, Commit tip, TreeDefinition definition, string summary)
    {
        var tree = git.ObjectDatabase.CreateTree(definition);
        if (tree.Sha == tip.Tree.Sha)
        {
            return;
        }

        var author = StudioRevisions.Author(userId, displayName);
        var message = StudioRevisions.Message(summary, userId, StudioRevisions.StudioClient, null);
        var commit = git.ObjectDatabase.CreateCommit(author, author, message, tree, [tip], prettifyMessage: false);
        git.Refs.Add(BranchName(draft.Id), commit.Id, allowOverwrite: true);
    }

    private static Blob CreateBlob(Repository git, byte[] bytes)
    {
        using var content = new MemoryStream(bytes);
        return git.ObjectDatabase.CreateBlob(content);
    }

    private static IReadOnlyList<StudioFile> Resources(Commit commit)
    {
        var files = new List<(string File, byte[] Bytes)>();
        Walk(commit.Tree, "", files);
        return StudioFolder.Resources(files);
    }

    // The *.json files of a tree, as a folder lists them.
    private static void Walk(Tree tree, string prefix, List<(string File, byte[] Bytes)> files)
    {
        foreach (var entry in tree)
        {
            var path = prefix + entry.Name;
            switch (entry.Target)
            {
                case Tree subtree:
                    Walk(subtree, path + "/", files);
                    break;
                case Blob blob when path.EndsWith(".json", StringComparison.Ordinal):
                    using (var stream = blob.GetContentStream())
                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        files.Add((path, buffer.ToArray()));
                    }

                    break;
            }
        }
    }

    // Whether the content has a UUID id and a string kind, as a resource file does.
    private static bool IsResource(JsonObject content, out Guid id)
    {
        id = Guid.Empty;
        return content["id"] is JsonValue idValue
            && idValue.TryGetValue<string>(out var text)
            && Guid.TryParseExact(text, "D", out id)
            && content["kind"] is JsonValue kind
            && kind.TryGetValue<string>(out _);
    }
}

/// <summary>What a draft operation did: the refusal, or the value.</summary>
internal sealed record StudioDraftOutcome<T>(StudioWriteRefusal? Refusal, T? Value)
{
    public static StudioDraftOutcome<T> Of(T value) => new(null, value);

    public static StudioDraftOutcome<T> Refused(StudioWriteRefusal refusal) => new(refusal, default);
}

/// <summary>
/// A draft's record: its id, the user id of its owner, the commit of <c>main</c> it started at, its creation time and the
/// version each resource it used had on <c>main</c> at first use, null for a resource <c>main</c> lacked.
/// </summary>
internal sealed record StudioDraft(string Id, string UserId, string Start, DateTimeOffset Created, IReadOnlyDictionary<Guid, string?> Versions);

/// <summary>
/// One page of a draft's diff: the commit it started at, the commit the page was read at, the files in this page sorted by
/// file, and the cursor of the next page, null on the last page.
/// </summary>
internal sealed record StudioDraftDiff(string Start, string Tip, IReadOnlyList<StudioRevisionChange> Resources, string? Cursor);

/// <summary>A resource a draft used whose version on <c>main</c> has changed: its id and the new version, null when <c>main</c> lacks it.</summary>
internal sealed record StudioDraftConflict(Guid Id, string? Version);

/// <summary>The layer file a draft write left: its path and version, both null when the write left no layer.</summary>
internal sealed record StudioDraftLayer(string? File, string? Version);
