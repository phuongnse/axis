using Axis.Tenancy.Platform;

namespace Axis.Tenancy.Keys;

/// <summary>
/// A key file: 32 bytes in base64 in a file, never on the command line or in a setting. It is the form of the backup key of
/// <c>tenant backup</c> and <c>tenant restore</c>, of the cell's backup key, <c>Cell:BackupKeyFile</c>, and of the key file of the key
/// service, <c>KeyService:KeyFile</c>. The operator keeps it outside the installation. A message names where the path came from, for
/// example the setting, and never holds the path or the content of the file.
/// </summary>
public static class KeyFile
{
    // A key in base64 is 44 characters. More than this is not a key.
    private const long MaxFileBytes = 4096;

    /// <summary>Returns the key, or null with the reason in <paramref name="problem"/> when the file cannot be read or does not hold a key.</summary>
    public static byte[]? Read(string path, out string problem, string source)
    {
        string text;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes)
            {
                problem = info.Exists ? Malformed(source) : Unreadable(source);
                return null;
            }

            // openssl and editors add a line break, so the text is trimmed.
            text = File.ReadAllText(path).Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            problem = Unreadable(source);
            return null;
        }

        var buffer = new byte[TenantSecretCipher.KeySize + 4];
        if (!Convert.TryFromBase64String(text, buffer, out var length) || length != TenantSecretCipher.KeySize)
        {
            problem = Malformed(source);
            return null;
        }

        problem = "";
        return buffer[..TenantSecretCipher.KeySize];
    }

    private static string Unreadable(string source) => $"The {source} file cannot be read.";

    private static string Malformed(string source) => $"The {source} file does not hold a key of 32 bytes in base64.";
}
