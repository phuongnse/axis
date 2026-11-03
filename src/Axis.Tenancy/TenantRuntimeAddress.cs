using System.Globalization;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.Tenancy;

/// <summary>
/// The address a cell's server and worker open tenant runtime connections at, such as PgBouncer, set by
/// <c>Cell:RuntimeDatabaseHost</c> and <c>Cell:RuntimeDatabasePort</c>. It is read apart from <see cref="CellOptions"/>,
/// because <c>Axis.Admin</c> loads that class and must never read or check these settings. The schema role, the
/// publisher, the worker's LISTEN connection and the platform connections keep connecting to PostgreSQL directly.
/// </summary>
public sealed record TenantRuntimeAddress(string Host, int? Port)
{
    public const string HostSetting = "Cell:RuntimeDatabaseHost";
    public const string PortSetting = "Cell:RuntimeDatabasePort";

    /// <summary>
    /// Reads the address, or <c>null</c> when the host is not in a cell or neither setting is set. Outside a cell the
    /// settings are ignored and not checked.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The port is set without a host, or is not a whole number from 1 to 65535. The message names the setting.
    /// </exception>
    public static TenantRuntimeAddress? Load(IConfiguration configuration, CellOptions cell)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(cell);

        if (!cell.IsCell)
        {
            return null;
        }

        var host = configuration[HostSetting];
        var port = configuration[PortSetting];
        var hasHost = !string.IsNullOrWhiteSpace(host);
        var hasPort = !string.IsNullOrWhiteSpace(port);
        if (!hasHost && !hasPort)
        {
            return null;
        }

        if (!hasHost)
        {
            throw new InvalidOperationException($"{PortSetting} is set, so {HostSetting} must be set too.");
        }

        int? number = null;
        if (hasPort)
        {
            if (!int.TryParse(port!.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 1 or > 65535)
            {
                throw new InvalidOperationException($"{PortSetting} '{port}' must be a port number from 1 to 65535.");
            }

            number = parsed;
        }

        return new TenantRuntimeAddress(host!.Trim(), number);
    }

    /// <summary>
    /// Sets the host, the port when one is set, and small pool values on <paramref name="builder"/>: at most 5 connections,
    /// none kept idle for more than 60 seconds, and no automatic prepared statements. An idle tenant then holds no
    /// connection, and no prepared statement outlives a transaction.
    /// </summary>
    public void ApplyTo(NpgsqlConnectionStringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Host = Host;
        if (Port is { } port)
        {
            builder.Port = port;
        }

        builder.MaxPoolSize = 5;
        builder.MinPoolSize = 0;
        builder.ConnectionIdleLifetime = 60;
        builder.MaxAutoPrepare = 0;
    }
}
