using System.Globalization;
using Axis.Configuration.Compilation;

namespace Axis.Server.Files;

/// <summary>
/// The file limits of the host, from <c>Files:MaxSize</c>: the largest size in bytes of an uploaded file.
/// A file field's own <c>maxSize</c> can only lower it. A missing value takes the default of 25 MB.
/// </summary>
internal sealed class FileSettings
{
    /// <summary>The setting of the largest size of an uploaded file, in bytes.</summary>
    public const string MaxSizeSetting = "Files:MaxSize";

    private FileSettings(long maxSize) => MaxSize = maxSize;

    /// <summary>The largest size in bytes of an uploaded file.</summary>
    public long MaxSize { get; }

    /// <exception cref="InvalidOperationException">The value is not a whole number of at least 1.</exception>
    public static FileSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration[MaxSizeSetting] is not { Length: > 0 } text)
        {
            return new FileSettings(ApplicationCompiler.DefaultMaxFileSize);
        }

        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 1)
        {
            throw new InvalidOperationException($"{MaxSizeSetting} must be a whole number of bytes, at least 1.");
        }

        return new FileSettings(value);
    }
}
