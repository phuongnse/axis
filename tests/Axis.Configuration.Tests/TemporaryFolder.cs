using System.Runtime.Versioning;

namespace Axis.Configuration.Tests;

/// <summary>An application folder written for one test and deleted afterwards.</summary>
internal sealed class TemporaryFolder : IDisposable
{
    private readonly List<(string Path, UnixFileMode Mode)> _deniedFolders = [];

    public TemporaryFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"axis-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public TemporaryFolder With(string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return this;
    }

    /// <summary>
    /// Removes every permission from a folder (the root when <paramref name="relativePath"/> is
    /// empty) so it cannot be listed. The permissions are restored on dispose. Unix only.
    /// </summary>
    [UnsupportedOSPlatform("windows")]
    public TemporaryFolder DenyAccess(string relativePath)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        _deniedFolders.Add((fullPath, File.GetUnixFileMode(fullPath)));
        File.SetUnixFileMode(fullPath, UnixFileMode.None);
        return this;
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            foreach (var (path, mode) in _deniedFolders)
            {
                File.SetUnixFileMode(path, mode);
            }
        }

        Directory.Delete(Path, recursive: true);
    }
}
