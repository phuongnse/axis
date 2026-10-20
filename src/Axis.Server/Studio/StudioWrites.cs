using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Patch;

namespace Axis.Server.Studio;

/// <summary>What a write did: the file's new version, or the reason it was refused.</summary>
internal sealed record StudioWriteOutcome(StudioWriteRefusal? Refusal, string? Version)
{
    public static StudioWriteOutcome Refused(StudioWriteRefusal refusal) => new(refusal, null);
}

internal enum StudioWriteRefusal
{
    NotFound,
    DoesNotApply,
    ChangesIdOrKind,
    Stale,
    Exists,
    Manifest,
    UnsafePath,
    Unavailable,
}

/// <summary>
/// Writes to the resource files of a Studio folder. One lock covers the whole check-version-then-write step
/// of every write, so two patches that carry the same version cannot both pass the check. The lock is a
/// PostgreSQL advisory lock in the platform database, keyed by the folder, so it holds across server replicas
/// that share the folder. A write answers <see cref="StudioWriteRefusal.Unavailable"/> and writes nothing when
/// the lock cannot be taken. A create refuses to replace a file, so it holds even when another process writes
/// the folder.
/// </summary>
internal static class StudioWrites
{
    private static readonly JsonWriterOptions _writer = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The files of <paramref name="folder"/> as a publish request stores them. It holds the write lock, so the
    /// snapshot never sees a write half done and includes every write that finished before the call. It throws
    /// <see cref="StudioWriteLockUnavailableException"/> when the lock cannot be taken.
    /// </summary>
    public static async Task<IReadOnlyList<StudioSnapshotFile>> SnapshotAsync(
        IStudioWriteLock writeLock, string folder, CancellationToken cancellationToken)
    {
        await using var held = await writeLock.TakeAsync(folder, cancellationToken);
        return StudioFolder.Snapshot(folder);
    }

    /// <summary>
    /// Applies <paramref name="patch"/> to the resource <paramref name="id"/> of <paramref name="folder"/> when
    /// <paramref name="ifMatch"/> is its current version, and replaces the file atomically. The checks run in
    /// this order: the resource (a resource that has gone since the route found it counts as a stale version), whether the patch applies, whether it changes <c>id</c> or <c>kind</c>, then
    /// the version. Nothing is written when one fails.
    /// </summary>
    public static async Task<StudioWriteOutcome> PatchAsync(
        IStudioWriteLock writeLock, string folder, Guid id, JsonPatch patch, string ifMatch, CancellationToken cancellationToken)
    {
        if (await TryTakeAsync(writeLock, folder, cancellationToken) is not { } held)
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.Unavailable);
        }

        await using var _ = held;

        // The route found the resource before this lock was held. When it is gone now, another write removed it, so
        // the version the author holds is stale.
        if (StudioFolder.List(folder).FirstOrDefault(file => file.Id == id) is not { } current)
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.Stale);
        }

        if (Target(folder, current.File) is not { } target)
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.NotFound);
        }

        JsonNode? original;
        JsonNode? patched;
        try
        {
            original = JsonNode.Parse(current.Bytes);
            var result = patch.Apply(JsonNode.Parse(current.Bytes));
            patched = result.IsSuccess ? result.Result : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.DoesNotApply);
        }

        if (patched is not JsonObject patchedObject || original is not JsonObject originalObject)
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.DoesNotApply);
        }

        if (!SameString(originalObject, patchedObject, "id") || !SameString(originalObject, patchedObject, "kind"))
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.ChangesIdOrKind);
        }

        if (!string.Equals(Unquote(ifMatch), current.Version, StringComparison.Ordinal))
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.Stale);
        }

        var bytes = Serialize(patchedObject);
        await ReplaceAsync(target, bytes, cancellationToken);
        return new StudioWriteOutcome(null, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Writes a new resource file <paramref name="file"/> into <paramref name="folder"/>, creating its kind
    /// folder when it is missing. It refuses when the folder already has a resource with <paramref name="id"/>
    /// or the target file exists, and it never replaces a file. Nothing is written when it refuses.
    /// </summary>
    public static async Task<StudioWriteOutcome> CreateAsync(
        IStudioWriteLock writeLock, string folder, Guid id, string file, JsonObject content, CancellationToken cancellationToken)
    {
        if (await TryTakeAsync(writeLock, folder, cancellationToken) is not { } held)
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.Unavailable);
        }

        await using var _ = held;

        if (StudioFolder.List(folder).Any(existing => existing.Id == id))
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.Exists);
        }

        if (Target(folder, file) is not { } target)
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.UnsafePath);
        }

        if (File.Exists(target) || Directory.Exists(target))
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.Exists);
        }

        var bytes = Serialize(content);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = TemporaryName(target);
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, target, overwrite: false);
        }
        catch (IOException) when (File.Exists(target))
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.Exists);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return new StudioWriteOutcome(null, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Deletes the file of the resource <paramref name="id"/> when <paramref name="ifMatch"/> is its current
    /// version. The checks run in this order: the resource (a resource that has gone since the route found it counts as a stale version), whether it is the manifest, then the version.
    /// </summary>
    public static async Task<StudioWriteOutcome> DeleteAsync(
        IStudioWriteLock writeLock, string folder, Guid id, string ifMatch, CancellationToken cancellationToken)
    {
        if (await TryTakeAsync(writeLock, folder, cancellationToken) is not { } held)
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.Unavailable);
        }

        await using var _ = held;

        // The route found the resource before this lock was held. When it is gone now, another write removed it, so
        // the version the author holds is stale.
        if (StudioFolder.List(folder).FirstOrDefault(file => file.Id == id) is not { } current)
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.Stale);
        }

        if (Target(folder, current.File) is not { } target)
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.NotFound);
        }

        if (current.File == "application.json")
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.Manifest);
        }

        if (!string.Equals(Unquote(ifMatch), current.Version, StringComparison.Ordinal))
        {
            return StudioWriteOutcome.Refused(StudioWriteRefusal.Stale);
        }

        File.Delete(target);
        return new StudioWriteOutcome(null, null);
    }

    // The held lock, or null when it cannot be taken.
    private static async Task<IAsyncDisposable?> TryTakeAsync(IStudioWriteLock writeLock, string folder, CancellationToken cancellationToken)
    {
        try
        {
            return await writeLock.TakeAsync(folder, cancellationToken);
        }
        catch (StudioWriteLockUnavailableException)
        {
            return null;
        }
    }

    private static bool SameString(JsonObject original, JsonObject patched, string name) =>
        original[name] is JsonValue before && before.TryGetValue<string>(out var oldValue)
        && patched[name] is JsonValue after && after.TryGetValue<string>(out var newValue)
        && string.Equals(oldValue, newValue, StringComparison.Ordinal);

    // The quoted ETag or the bare hex version. Anything else, such as * or a weak tag, matches no version.
    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    private static byte[] Serialize(JsonObject content)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, _writer))
        {
            content.WriteTo(writer);
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    // The full path of the file, or null when it is not a real *.json file inside the folder: it leaves the
    // folder, or the file or a folder on the way to it is a link.
    private static string? Target(string folder, string file)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var target = Path.GetFullPath(Path.Combine(root, file));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !target.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || new FileInfo(target).LinkTarget is not null)
        {
            return null;
        }

        for (var directory = Path.GetDirectoryName(target); directory is not null && directory.Length > root.Length; directory = Path.GetDirectoryName(directory))
        {
            if (new DirectoryInfo(directory).LinkTarget is not null)
            {
                return null;
            }
        }

        return target;
    }

    // A hidden temporary name that is not *.json, so neither the list nor the dev script's watcher sees it.
    private static string TemporaryName(string target) =>
        Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");

    private static async Task ReplaceAsync(string target, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporary = TemporaryName(target);
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
