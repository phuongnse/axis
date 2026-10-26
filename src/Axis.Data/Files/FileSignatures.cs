namespace Axis.Data.Files;

/// <summary>
/// Checks the first bytes of an uploaded file. A file that starts like an executable is refused for
/// every declared media type, and a file of a type with a known signature must start with it. A
/// media type without a known signature matches any bytes. This is a check on the declared type, not a scan of the content.
/// </summary>
public static class FileSignatures
{
    /// <summary>The number of leading bytes the checks read.</summary>
    public const int HeadLength = 16;

    private static readonly byte[][] _executableStarts =
    [
        [0x4D, 0x5A], // MZ: a Windows executable.
        [0x7F, 0x45, 0x4C, 0x46], // ELF: a Linux executable.
        [0xFE, 0xED, 0xFA, 0xCE], // Mach-O, 32-bit.
        [0xFE, 0xED, 0xFA, 0xCF], // Mach-O, 64-bit.
        [0xCE, 0xFA, 0xED, 0xFE], // Mach-O, 32-bit, little-endian.
        [0xCF, 0xFA, 0xED, 0xFE], // Mach-O, 64-bit, little-endian.
        [0xCA, 0xFE, 0xBA, 0xBE], // Mach-O universal binary.
    ];

    private static readonly byte[] _pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] _jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] _gif87 = "GIF87a"u8.ToArray();
    private static readonly byte[] _gif89 = "GIF89a"u8.ToArray();
    private static readonly byte[] _riff = "RIFF"u8.ToArray();
    private static readonly byte[] _webp = "WEBP"u8.ToArray();
    private static readonly byte[][] _zipStarts = [[0x50, 0x4B, 0x03, 0x04], [0x50, 0x4B, 0x05, 0x06], [0x50, 0x4B, 0x07, 0x08]];

    /// <summary>Whether <paramref name="head"/>, the first bytes of a file, starts a Windows, Linux or macOS executable.</summary>
    public static bool IsExecutable(ReadOnlySpan<byte> head) => StartsWithAny(head, _executableStarts);

    /// <summary>
    /// Whether <paramref name="head"/> starts as a file of <paramref name="mediaType"/> does. The media type is
    /// a bare type, compared without regard to letter case. A type that is not PDF, PNG, JPEG, GIF, WebP or ZIP matches.
    /// </summary>
    public static bool Matches(string mediaType, ReadOnlySpan<byte> head)
    {
        ArgumentNullException.ThrowIfNull(mediaType);

        return mediaType.ToLowerInvariant() switch
        {
            "application/pdf" => head.StartsWith(_pdf),
            "image/png" => head.StartsWith(_png),
            "image/jpeg" => head.StartsWith(_jpeg),
            "image/gif" => head.StartsWith(_gif87) || head.StartsWith(_gif89),
            "image/webp" => head.Length >= 12 && head.StartsWith(_riff) && head[8..12].SequenceEqual(_webp),
            "application/zip" => StartsWithAny(head, _zipStarts),
            _ => true,
        };
    }

    private static bool StartsWithAny(ReadOnlySpan<byte> head, byte[][] starts)
    {
        foreach (var start in starts)
        {
            if (head.StartsWith(start))
            {
                return true;
            }
        }

        return false;
    }
}
