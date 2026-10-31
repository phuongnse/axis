namespace Axis.Admin;

/// <summary>
/// The backup key of <c>tenant backup</c> and <c>tenant restore</c>: 32 bytes in base64 in a file, never on the command line or in a setting.
/// The operator keeps it outside the installation. A message never holds the content of the file.
/// </summary>
internal static class BackupKeyFile
{
    public const string Option = "--backup-key-file";

    // A key in base64 is 44 characters. More than this is not a key.
    private const long MaxFileBytes = 4096;

    /// <summary>Returns the key, or null with the reason in <paramref name="problem"/> when the file cannot be read or does not hold a key.</summary>
    public static byte[]? Read(string path, out string problem)
    {
        string text;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes)
            {
                problem = info.Exists ? Malformed : Unreadable;
                return null;
            }

            // openssl and editors add a line break, so the text is trimmed.
            text = File.ReadAllText(path).Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            problem = Unreadable;
            return null;
        }

        var buffer = new byte[BackupArchive.KeySize + 4];
        if (!Convert.TryFromBase64String(text, buffer, out var length) || length != BackupArchive.KeySize)
        {
            problem = Malformed;
            return null;
        }

        problem = "";
        return buffer[..BackupArchive.KeySize];
    }

    private static string Unreadable => $"The {Option} file cannot be read.";

    private static string Malformed => $"The {Option} file does not hold a key of 32 bytes in base64.";
}
