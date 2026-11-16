using Axis.Studio;

namespace Axis.Server.Studio;

/// <summary>Writes the files of a new application. A test replaces it to make a write fail.</summary>
internal interface IStudioFileWriter
{
    Task WriteAsync(string path, byte[] bytes, CancellationToken cancellationToken);
}

internal sealed class StudioFileWriter : IStudioFileWriter
{
    public Task WriteAsync(string path, byte[] bytes, CancellationToken cancellationToken) =>
        File.WriteAllBytesAsync(path, bytes, cancellationToken);
}

internal enum StudioCreateOutcome
{
    Created,
    Exists,
    Unavailable,
}

/// <summary>
/// Creates a new application folder in <c>Studio:ApplicationsPath</c>, blank or as a copy of an installed template.
/// The files are written to a hidden temporary folder under the write lock, and the folder is then renamed to the
/// kebab-case name, so a reader never sees half an application and a failed create leaves nothing behind.
/// </summary>
internal static class StudioApplications
{
    /// <summary>
    /// Creates the folder of <paramref name="name"/> from <paramref name="files"/>. It answers
    /// <see cref="StudioCreateOutcome.Exists"/> and writes nothing when an application already has the name, ignoring
    /// letter case, or the target folder exists. A failed write deletes the temporary folder and the exception goes on.
    /// </summary>
    public static async Task<StudioCreateOutcome> CreateAsync(
        IStudioWriteLock writeLock,
        IStudioFileWriter writer,
        StudioOptions options,
        string name,
        IReadOnlyList<(string File, byte[] Bytes)> files,
        CancellationToken cancellationToken)
    {
        var root = Path.TrimEndingDirectorySeparator(options.ApplicationsPath!);
        IAsyncDisposable held;
        try
        {
            held = await writeLock.TakeAsync(root, cancellationToken);
        }
        catch (StudioWriteLockUnavailableException)
        {
            return StudioCreateOutcome.Unavailable;
        }

        await using var _ = held;

        var kebab = StudioFileNames.Kebab(name);
        var target = Path.Combine(root, kebab);
        if (StudioFolder.Applications(options).Contains(name, StringComparer.OrdinalIgnoreCase) || Path.Exists(target))
        {
            return StudioCreateOutcome.Exists;
        }

        var temporary = Path.Combine(root, $".{kebab}-{Guid.NewGuid():N}");
        var prefix = temporary + Path.DirectorySeparatorChar;
        try
        {
            foreach (var (file, bytes) in files)
            {
                var path = Path.GetFullPath(Path.Combine(temporary, file));
                if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(".json", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A file of a new application is outside its folder.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await writer.WriteAsync(path, bytes, cancellationToken);
            }

            Directory.Move(temporary, target);
        }
        catch
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }

            throw;
        }

        return StudioCreateOutcome.Created;
    }
}
