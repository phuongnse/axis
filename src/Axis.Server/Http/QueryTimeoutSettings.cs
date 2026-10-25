using System.Globalization;

namespace Axis.Server.Http;

/// <summary>
/// The time limits of database reads, from <c>Data:QueryTimeout</c> for interactive reads and
/// <c>Data:ExportQueryTimeout</c> for exports. A missing value takes its default.
/// </summary>
internal sealed class QueryTimeoutSettings
{
    /// <summary>The setting of the limit on interactive reads. The default is 10 seconds.</summary>
    public const string Interactive = "Data:QueryTimeout";

    /// <summary>The setting of the limit on exports. The default is one minute.</summary>
    public const string Export = "Data:ExportQueryTimeout";

    private readonly TimeSpan _interactive;
    private readonly TimeSpan _export;

    private QueryTimeoutSettings(TimeSpan interactive, TimeSpan export)
    {
        _interactive = interactive;
        _export = export;
    }

    /// <exception cref="InvalidOperationException">A value is not a duration of at least one millisecond that PostgreSQL accepts.</exception>
    public static QueryTimeoutSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new QueryTimeoutSettings(
            Read(configuration, Interactive, TimeSpan.FromSeconds(10)),
            Read(configuration, Export, TimeSpan.FromMinutes(1)));
    }

    /// <summary>The limit of the setting named <paramref name="setting"/>, <see cref="Interactive"/> or <see cref="Export"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="setting"/> names no limit.</exception>
    public TimeSpan For(string setting) => setting switch
    {
        Interactive => _interactive,
        Export => _export,
        _ => throw new ArgumentException($"'{setting}' is not a query timeout setting.", nameof(setting)),
    };

    private static TimeSpan Read(IConfiguration configuration, string setting, TimeSpan fallback)
    {
        if (configuration[setting] is not { Length: > 0 } text)
        {
            return fallback;
        }

        // A limit of 0 ms means no limit to PostgreSQL, and statement_timeout holds an int of milliseconds.
        if (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var value)
            || value < TimeSpan.FromMilliseconds(1)
            || value.TotalMilliseconds > int.MaxValue)
        {
            throw new InvalidOperationException($"{setting} must be a duration of at least one millisecond, such as 00:00:10.");
        }

        return value;
    }
}
