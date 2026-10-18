using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Patch;

namespace Axis.Server.Studio;

/// <summary>What a patch did: the new version and its bytes, or the reason it was refused.</summary>
internal sealed record StudioPatchOutcome(StudioPatchRefusal? Refusal, string? Version)
{
    public static StudioPatchOutcome Refused(StudioPatchRefusal refusal) => new(refusal, null);
}

internal enum StudioPatchRefusal
{
    NotFound,
    DoesNotApply,
    ChangesIdOrKind,
    Stale,
}

/// <summary>
/// Writes to the resource files of a Studio folder. One lock covers the whole check-version-then-write step
/// of every write, so two patches that carry the same version cannot both pass the check. The lock covers
/// this server process only.
/// </summary>
internal static class StudioWrites
{
    private static readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonWriterOptions _writer = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Applies <paramref name="patch"/> to the resource <paramref name="id"/> of <paramref name="folder"/> when
    /// <paramref name="ifMatch"/> is its current version, and replaces the file atomically. The checks run in
    /// this order: the resource, whether the patch applies, whether it changes <c>id</c> or <c>kind</c>, then
    /// the version. Nothing is written when one fails.
    /// </summary>
    public static async Task<StudioPatchOutcome> PatchAsync(
        string folder, Guid id, JsonPatch patch, string ifMatch, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (StudioFolder.List(folder).FirstOrDefault(file => file.Id == id) is not { } current
                || Target(folder, current.File) is not { } target)
            {
                return StudioPatchOutcome.Refused(StudioPatchRefusal.NotFound);
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
                return StudioPatchOutcome.Refused(StudioPatchRefusal.DoesNotApply);
            }

            if (patched is not JsonObject patchedObject || original is not JsonObject originalObject)
            {
                return StudioPatchOutcome.Refused(StudioPatchRefusal.DoesNotApply);
            }

            if (!SameString(originalObject, patchedObject, "id") || !SameString(originalObject, patchedObject, "kind"))
            {
                return StudioPatchOutcome.Refused(StudioPatchRefusal.ChangesIdOrKind);
            }

            if (!string.Equals(Unquote(ifMatch), current.Version, StringComparison.Ordinal))
            {
                return StudioPatchOutcome.Refused(StudioPatchRefusal.Stale);
            }

            var bytes = Serialize(patchedObject);
            await ReplaceAsync(target, bytes, cancellationToken);
            return new StudioPatchOutcome(null, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        finally
        {
            _lock.Release();
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
    private static async Task ReplaceAsync(string target, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
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
