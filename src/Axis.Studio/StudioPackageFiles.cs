using System.Security.Cryptography;
using Axis.Configuration.Loading;

namespace Axis.Studio;

/// <summary>
/// The package files of a Studio folder, the files under <c>packages/</c>, see
/// <c>docs/reference/studio-packages.md#the-authoring-api</c>. One mechanism decides which files these are: the files that
/// <see cref="StudioFolder.Files"/> lists, so a package file that a revision or a snapshot holds is one that the list holds.
/// </summary>
internal static class StudioPackageFiles
{
    /// <summary>
    /// The path relative to the folder of the file <paramref name="path"/> of the package <paramref name="package"/>, that is
    /// <c>packages/&lt;package&gt;/&lt;path&gt;</c>, or null when it names no writable file. The package must be a plain name, see
    /// <see cref="StudioOptions.IsPlainName"/>, and no segment of either may be empty, <c>.</c> or <c>..</c>, start with <c>.</c> or hold
    /// <c>\</c>. A hidden file is left out of revisions and snapshots, so a write must never create one.
    /// </summary>
    public static string? File(string package, string path) =>
        StudioOptions.IsPlainName(package) && IsPlainSegment(package) && path.Split('/').All(IsPlainSegment)
            ? $"{ApplicationLoader.PackagesFolderPrefix}{package}/{path}"
            : null;

    /// <summary>
    /// Every file of <paramref name="folder"/> under <c>packages/</c>, sorted by file, with the SHA-256 of its bytes as lower-case hex.
    /// A file directly under <c>packages/</c>, which is in no package, is left out.
    /// </summary>
    public static IReadOnlyList<(string Package, string Path, string Version)> List(string folder) => List(StudioFolder.Files(folder));

    /// <summary>
    /// The files of <paramref name="files"/>, as <see cref="StudioFolder.Files"/> or a draft's branch lists them, that are under
    /// <c>packages/</c>, in their order, split into package and path, with the SHA-256 of their bytes as lower-case hex.
    /// A file directly under <c>packages/</c>, which is in no package, is left out.
    /// </summary>
    public static IReadOnlyList<(string Package, string Path, string Version)> List(IEnumerable<(string File, byte[] Bytes)> files)
    {
        var items = new List<(string Package, string Path, string Version)>();
        foreach (var (file, bytes) in files)
        {
            if (!file.StartsWith(ApplicationLoader.PackagesFolderPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = file[ApplicationLoader.PackagesFolderPrefix.Length..];
            var slash = rest.IndexOf('/', StringComparison.Ordinal);
            if (slash > 0)
            {
                items.Add((rest[..slash], rest[(slash + 1)..], Convert.ToHexStringLower(SHA256.HashData(bytes))));
            }
        }

        return items;
    }

    /// <summary>
    /// The bytes of the package file <paramref name="file"/>, a path that <see cref="File"/> made, or null when the folder has none.
    /// It reads the file through the list of <see cref="StudioFolder.Files"/>, so a link or a hidden file is not one.
    /// </summary>
    public static byte[]? Read(string folder, string file)
    {
        foreach (var (path, bytes) in StudioFolder.Files(folder))
        {
            if (string.Equals(path, file, StringComparison.Ordinal))
            {
                return bytes;
            }
        }

        return null;
    }

    private static bool IsPlainSegment(string segment) =>
        segment.Length > 0 && !segment.StartsWith('.') && !segment.Contains('\\') && !segment.Contains('\0');
}
