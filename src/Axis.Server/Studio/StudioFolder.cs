using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Axis.Server.Studio;

/// <summary>The <c>id</c> and <c>name</c> of a folder's manifest. Each is null when the manifest has no usable value.</summary>
internal sealed record StudioManifest(Guid? Id, string? Name);

/// <summary>One file of a Studio snapshot: its path, the SHA-256 of its bytes and its text.</summary>
internal sealed record StudioSnapshotFile(string File, string Version, string Content);

/// <summary>One resource file of a Studio folder, with the bytes its version was computed from.</summary>
internal sealed record StudioFile(Guid Id, string Kind, string? Name, string File, string Version, byte[] Bytes);

/// <summary>
/// Finds a Studio folder by application name and lists its resource files. Only real
/// <c>*.json</c> files under the folder are listed. A symbolic link, a hidden file and a file
/// whose resolved path leaves the folder are never listed, so no request reads outside the folder.
/// </summary>
internal static class StudioFolder
{
    private const string ManifestFile = "application.json";

    private static readonly EnumerationOptions _enumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    /// <summary>
    /// The first listed folder whose manifest <c>name</c> is <paramref name="application"/>, ignoring
    /// letter case, or null. The manifest is read on every call, so an edited name takes effect at once.
    /// </summary>
    public static string? Find(StudioOptions options, string application) =>
        options.Folders.FirstOrDefault(folder =>
            string.Equals(ReadName(folder), application, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The manifest <c>name</c> of each listed folder, in list order. A folder without a readable manifest
    /// name is left out. Of names equal ignoring letter case, the first is kept, as <see cref="Find"/> takes it.
    /// </summary>
    public static IReadOnlyList<string> Applications(StudioOptions options)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return [.. options.Folders.Select(ReadName).OfType<string>().Where(seen.Add)];
    }

    private static string? ReadName(string folder) => ReadManifest(folder)?.Name;

    /// <summary>
    /// The manifest <c>id</c> and <c>name</c> of <paramref name="folder"/>, or null when the manifest cannot be
    /// read or is not a JSON object.
    /// </summary>
    public static StudioManifest? ReadManifest(string folder)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, ManifestFile)));
            if (document.RootElement is not { ValueKind: JsonValueKind.Object } root)
            {
                return null;
            }

            var name = root.TryGetProperty("name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String
                ? nameValue.GetString()
                : null;
            Guid? id = root.TryGetProperty("id", out var idValue)
                && idValue.ValueKind == JsonValueKind.String
                && Guid.TryParse(idValue.GetString(), out var guid)
                    ? guid
                    : null;
            return new StudioManifest(id, name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A folder without a readable manifest has no application name.
            return null;
        }
    }

    /// <summary>
    /// Every <c>*.json</c> file under <paramref name="folder"/>, sorted by file, as a publish request stores it. A file
    /// that is not a valid resource is kept, so the publisher reports it. The same files as <see cref="List"/> are
    /// reachable: no link, no hidden file and nothing that leaves the folder.
    /// </summary>
    public static IReadOnlyList<StudioSnapshotFile> Snapshot(string folder)
    {
        var root = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        var files = new List<StudioSnapshotFile>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json", _enumeration))
        {
            if (!Path.GetFullPath(path).StartsWith(root, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var bytes = File.ReadAllBytes(path);
                files.Add(new StudioSnapshotFile(
                    Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/'),
                    Convert.ToHexStringLower(SHA256.HashData(bytes)),
                    Encoding.UTF8.GetString(bytes)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file that cannot be read is left out, as List leaves it out.
            }
        }

        files.Sort((left, right) => string.CompareOrdinal(left.File, right.File));
        return files;
    }

    /// <summary>
    /// The resources of <paramref name="folder"/> sorted by file. A file that is not a JSON object with a
    /// UUID <c>id</c> and a string <c>kind</c> is left out. Of two files with one id, the first by path wins.
    /// </summary>
    public static IReadOnlyList<StudioFile> List(string folder)
    {
        var root = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        var files = new List<StudioFile>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json", _enumeration))
        {
            if (!Path.GetFullPath(path).StartsWith(root, StringComparison.Ordinal))
            {
                continue;
            }

            if (Read(path, Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/')) is { } file)
            {
                files.Add(file);
            }
        }

        files.Sort((left, right) => string.CompareOrdinal(left.File, right.File));
        var seen = new HashSet<Guid>();
        return [.. files.Where(file => seen.Add(file.Id))];
    }

    private static StudioFile? Read(string path, string relative)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement is not { ValueKind: JsonValueKind.Object } content
                || !content.TryGetProperty("id", out var id)
                || id.ValueKind != JsonValueKind.String
                || !Guid.TryParse(id.GetString(), out var guid)
                || !content.TryGetProperty("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var name = content.TryGetProperty("name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String
                ? nameValue.GetString()
                : null;
            return new StudioFile(guid, kind.GetString()!, name, relative, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
