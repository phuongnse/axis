using Axis.Tenancy;

namespace Axis.Server.Http;

/// <summary>
/// The time limits of database reads and writes, from <c>Data:QueryTimeout</c> for interactive reads,
/// <c>Data:ExportQueryTimeout</c> for exports and <c>Data:WriteTimeout</c> for writes. A missing value
/// takes its default.
/// </summary>
internal sealed class QueryTimeoutSettings
{
    /// <summary>The setting of the limit on interactive reads. The default is 10 seconds.</summary>
    public const string Interactive = "Data:QueryTimeout";

    /// <summary>The setting of the limit on exports. The default is one minute.</summary>
    public const string Export = "Data:ExportQueryTimeout";

    /// <summary>The setting of the limit on the write transaction of a record, process start, task or inbound call. The default is 15 seconds.</summary>
    public const string Write = "Data:WriteTimeout";

    private readonly TimeSpan _interactive;
    private readonly TimeSpan _export;
    private readonly TimeSpan _write;

    private QueryTimeoutSettings(TimeSpan interactive, TimeSpan export, TimeSpan write)
    {
        _interactive = interactive;
        _export = export;
        _write = write;
    }

    /// <exception cref="InvalidOperationException">A value is not a duration of at least one millisecond that PostgreSQL accepts.</exception>
    public static QueryTimeoutSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new QueryTimeoutSettings(
            StatementTimeout.Read(configuration, Interactive, TimeSpan.FromSeconds(10)),
            StatementTimeout.Read(configuration, Export, TimeSpan.FromMinutes(1)),
            StatementTimeout.Read(configuration, Write, TimeSpan.FromSeconds(15)));
    }

    /// <summary>The limit of the setting named <paramref name="setting"/>, <see cref="Interactive"/>, <see cref="Export"/> or <see cref="Write"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="setting"/> names no limit.</exception>
    public TimeSpan For(string setting) => setting switch
    {
        Interactive => _interactive,
        Export => _export,
        Write => _write,
        _ => throw new ArgumentException($"'{setting}' is not a query timeout setting.", nameof(setting)),
    };
}
