using System.Globalization;

namespace Axis.Server.Records;

/// <summary>
/// The size limit of an import upload, from <c>Data:ImportMaxBytes</c>. A missing value takes the default of
/// 20 MB. The worker reads <c>Data:ImportMaxRows</c> itself.
/// </summary>
internal sealed class RecordImportSettings
{
    /// <summary>The setting of the largest import file.</summary>
    public const string MaxBytesSetting = "Data:ImportMaxBytes";

    private RecordImportSettings(long maxBytes)
    {
        MaxBytes = maxBytes;
    }

    /// <summary>The most bytes one import file may hold.</summary>
    public long MaxBytes { get; }

    /// <exception cref="InvalidOperationException">The value is not a whole number of at least 1.</exception>
    public static RecordImportSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration[MaxBytesSetting] is not { Length: > 0 } text)
        {
            return new RecordImportSettings(20 * 1024 * 1024);
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) && bytes >= 1
            ? new RecordImportSettings(bytes)
            : throw new InvalidOperationException($"{MaxBytesSetting} must be a whole number of bytes, at least 1.");
    }
}
