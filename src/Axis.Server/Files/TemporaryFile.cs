namespace Axis.Server.Files;

/// <summary>Temporary files for bytes that are too large to hold in memory, such as an export or an upload.</summary>
internal static class TemporaryFile
{
    /// <summary>
    /// A new temporary file in the system temp folder, named <c>{prefix}-{id}.tmp</c>, that is deleted when it is
    /// closed. Only its owner can read it.
    /// </summary>
    public static FileStream Create(string prefix)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.DeleteOnClose,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.tmp"), options);
    }
}
