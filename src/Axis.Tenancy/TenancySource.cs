using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy;

/// <summary>Where the server and the worker learn their tenants, set by <c>Tenancy:Source</c>.</summary>
public enum TenancySource
{
    /// <summary>The <c>Tenants</c> section of the host configuration. This is the default.</summary>
    Configuration,

    /// <summary>The tenant directory in the <c>platform</c> schema of the platform database.</summary>
    Directory,
}

public static class TenancySettings
{
    public const string SourceSetting = "Tenancy:Source";

    public const string RefreshIntervalSetting = "Tenancy:RefreshInterval";

    /// <summary>How often the server and the worker re-read the tenant directory when <c>Tenancy:RefreshInterval</c> is not set.</summary>
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Reads <c>Tenancy:Source</c>. A missing or blank value is <see cref="TenancySource.Configuration"/>.
    /// The value matches exactly, in lower case.
    /// </summary>
    /// <exception cref="InvalidOperationException">The value is neither <c>configuration</c> nor <c>directory</c>.</exception>
    public static TenancySource Source(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var value = configuration[SourceSetting];
        return value switch
        {
            _ when string.IsNullOrWhiteSpace(value) => TenancySource.Configuration,
            "configuration" => TenancySource.Configuration,
            "directory" => TenancySource.Directory,
            _ => throw new InvalidOperationException(
                $"The setting {SourceSetting} is '{value}'. It must be 'configuration' or 'directory'."),
        };
    }

    /// <summary>
    /// Reads <c>Tenancy:RefreshInterval</c>, how often the server and the worker re-read the tenant directory. A missing or blank
    /// value is 30 seconds. Only the <c>directory</c> source uses it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The value is not a positive time span.</exception>
    public static TimeSpan RefreshInterval(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var value = configuration[RefreshIntervalSetting];
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultRefreshInterval;
        }

        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var interval) || interval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"The setting {RefreshIntervalSetting} is '{value}'. It must be a positive time span, such as 00:00:30.");
        }

        return interval;
    }
}
