using System.Collections.Frozen;

namespace Axis.Data.Files;

/// <summary>The check of the first bytes of a file against the start of its media type.</summary>
public delegate bool ContentCheck(ReadOnlySpan<byte> head);

/// <summary>A media type that Axis checks: the file name extensions that belong to it and the check of a file's first bytes.</summary>
public sealed record FileType(IReadOnlyList<string> Extensions, ContentCheck Content);

/// <summary>
/// Checks an uploaded file against its declared media type, see <c>docs/reference/file-fields.md#upload</c>. The
/// <see cref="Types"/> table lists every media type Axis checks. A file of another media type is refused, because
/// neither its extension nor its content can be checked. A file name must end in an extension of the declared type, and the
/// first bytes of the file must start as that type does. A file that starts like an executable is refused for every
/// declared media type. This is a check on the declared type, not a scan of the content.
/// </summary>
public static class FileSignatures
{
    /// <summary>The number of leading bytes the checks read.</summary>
    public const int HeadLength = 512;

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
    private static readonly byte[] _bmp = "BM"u8.ToArray();
    private static readonly byte[][] _tiffStarts = [[0x49, 0x49, 0x2A, 0x00], [0x4D, 0x4D, 0x00, 0x2A]];
    private static readonly byte[][] _zipStarts = [[0x50, 0x4B, 0x03, 0x04], [0x50, 0x4B, 0x05, 0x06], [0x50, 0x4B, 0x07, 0x08]];
    private static readonly byte[] _compoundFile = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>The media types Axis checks, by lower-case media type. An extension is lower-case and starts with a dot.</summary>
    public static IReadOnlyDictionary<string, FileType> Types { get; } = new Dictionary<string, FileType>
    {
        ["application/pdf"] = new([".pdf"], head => head.StartsWith(_pdf)),
        ["image/png"] = new([".png"], head => head.StartsWith(_png)),
        ["image/jpeg"] = new([".jpg", ".jpeg"], head => head.StartsWith(_jpeg)),
        ["image/gif"] = new([".gif"], head => head.StartsWith(_gif87) || head.StartsWith(_gif89)),
        ["image/webp"] = new([".webp"], head => head.Length >= 12 && head.StartsWith(_riff) && head[8..12].SequenceEqual(_webp)),
        ["image/bmp"] = new([".bmp"], head => head.StartsWith(_bmp)),
        ["image/tiff"] = new([".tif", ".tiff"], head => StartsWithAny(head, _tiffStarts)),
        ["application/zip"] = new([".zip"], IsZip),
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = new([".docx"], IsZip),
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = new([".xlsx"], IsZip),
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = new([".pptx"], IsZip),
        ["application/vnd.oasis.opendocument.text"] = new([".odt"], IsZip),
        ["application/vnd.oasis.opendocument.spreadsheet"] = new([".ods"], IsZip),
        ["application/vnd.oasis.opendocument.presentation"] = new([".odp"], IsZip),
        ["application/msword"] = new([".doc"], IsCompoundFile),
        ["application/vnd.ms-excel"] = new([".xls"], IsCompoundFile),
        ["application/vnd.ms-powerpoint"] = new([".ppt"], IsCompoundFile),
        ["text/plain"] = new([".txt"], IsText),
        ["text/csv"] = new([".csv"], IsText),
        ["application/json"] = new([".json"], IsText),
        ["application/xml"] = new([".xml"], IsText),
        ["text/xml"] = new([".xml"], IsText),
        ["text/html"] = new([".html", ".htm"], IsText),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="head"/>, the first bytes of a file, starts a Windows, Linux or macOS executable.</summary>
    public static bool IsExecutable(ReadOnlySpan<byte> head) => StartsWithAny(head, _executableStarts);

    /// <summary>Whether Axis checks files of <paramref name="mediaType"/>, a bare type compared without regard to letter case.</summary>
    public static bool IsChecked(string mediaType)
    {
        ArgumentNullException.ThrowIfNull(mediaType);

        return Types.ContainsKey(mediaType);
    }

    /// <summary>
    /// Whether the extension of <paramref name="fileName"/> belongs to <paramref name="mediaType"/>, compared without
    /// regard to letter case. A name without an extension, and a media type that Axis does not check, do not match.
    /// </summary>
    public static bool HasExtension(string mediaType, string fileName)
    {
        ArgumentNullException.ThrowIfNull(mediaType);
        ArgumentNullException.ThrowIfNull(fileName);

        var extension = Path.GetExtension(fileName);
        return extension.Length > 1
            && Types.TryGetValue(mediaType, out var type)
            && type.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether <paramref name="head"/> starts as a file of <paramref name="mediaType"/> does. The media type is
    /// a bare type, compared without regard to letter case. A media type that Axis does not check matches no bytes.
    /// </summary>
    public static bool Matches(string mediaType, ReadOnlySpan<byte> head)
    {
        ArgumentNullException.ThrowIfNull(mediaType);

        return Types.TryGetValue(mediaType, out var type) && type.Content(head);
    }

    private static bool IsZip(ReadOnlySpan<byte> head) => StartsWithAny(head, _zipStarts);

    private static bool IsCompoundFile(ReadOnlySpan<byte> head) => head.StartsWith(_compoundFile);

    // Text has no NUL and no control byte but tab, line feed, form feed and carriage return. It need not be UTF-8.
    private static bool IsText(ReadOnlySpan<byte> head)
    {
        foreach (var value in head)
        {
            if (value < 0x20 && value is not (0x09 or 0x0A or 0x0C or 0x0D))
            {
                return false;
            }
        }

        return true;
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
