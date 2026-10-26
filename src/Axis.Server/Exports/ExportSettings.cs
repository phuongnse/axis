using System.Globalization;

namespace Axis.Server.Exports;

/// <summary>
/// The size limit of an export file, from <c>Data:ExportMaxRows</c>. A missing value takes the default.
/// The time limit of an export is <c>Data:ExportQueryTimeout</c>, see <see cref="Http.QueryTimeoutSettings"/>.
/// </summary>
internal sealed class ExportSettings
{
    /// <summary>The setting of the most data rows one export file holds. The default is 100000.</summary>
    public const string MaxRowsSetting = "Data:ExportMaxRows";

    private const int DefaultMaxRows = 100_000;

    private ExportSettings(int maxRows) => MaxRows = maxRows;

    /// <summary>The most data rows one export file holds, without its header row.</summary>
    public int MaxRows { get; }

    /// <exception cref="InvalidOperationException">The value is not an integer from 1 to one below <see cref="int.MaxValue"/>.</exception>
    public static ExportSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration[MaxRowsSetting] is not { Length: > 0 } text)
        {
            return new ExportSettings(DefaultMaxRows);
        }

        // The read takes one row more than the limit to find out whether the file is cut.
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 1 or int.MaxValue)
        {
            throw new InvalidOperationException($"{MaxRowsSetting} must be an integer from 1 to {int.MaxValue - 1}.");
        }

        return new ExportSettings(value);
    }
}
