using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy;

/// <summary>Where the server learns its tenants, set by <c>Tenancy:Source</c>.</summary>
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
}
