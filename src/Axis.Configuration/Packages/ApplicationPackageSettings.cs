using Microsoft.Extensions.Configuration;

namespace Axis.Configuration.Packages;

/// <summary>
/// The one reader of <c>Extensions:ApplicationPackages</c>, see
/// docs/reference/extension-packages.md#an-applications-packages. <see cref="On"/> is the setting, <see langword="false"/> when it is
/// unset. <see cref="RunnerHost"/> and <see cref="RunnerPort"/> are the <c>host:port</c> of <c>application-package-host</c> from
/// <c>Extensions:Runners:Application</c>, <see langword="null"/> and 0 when it is not set.
/// </summary>
public sealed record ApplicationPackageSettings(bool On, string? RunnerHost, int RunnerPort)
{
    public const string Setting = "Extensions:ApplicationPackages";
    public const string RunnerSetting = "Extensions:Runners:Application";

    /// <summary>Reads <c>Extensions:ApplicationPackages</c>, <c>true</c> or <c>false</c> in any case, and <c>false</c> when unset.</summary>
    /// <exception cref="InvalidOperationException">The setting has another value. The message names it.</exception>
    public static bool IsOn(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration[Setting] switch
        {
            null or "" => false,
            var value when bool.TryParse(value, out var on) => on,
            _ => throw new InvalidOperationException($"'{Setting}' must be 'true' or 'false'."),
        };
    }

    /// <summary>
    /// Reads the setting and <c>Extensions:Runners:Application</c>, a <c>host:port</c> or <c>[ipv6]:port</c> with a port from 1 to 65535,
    /// for the worker, which asks <c>application-package-host</c> whether it runs under gVisor, and for the publisher, which
    /// sends it each package to build or test.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>Extensions:ApplicationPackages</c> is not <c>true</c> or <c>false</c>, <c>Extensions:Runners:Application</c> is not a
    /// <c>host:port</c>, or it is not set while the setting is on. The message names the setting.
    /// </exception>
    public static ApplicationPackageSettings Load(IConfiguration configuration)
    {
        var on = IsOn(configuration);
        if (configuration[RunnerSetting] is not { Length: > 0 } runner)
        {
            return on
                ? throw new InvalidOperationException($"'{RunnerSetting}' must be set to the host and port of application-package-host when '{Setting}' is 'true'.")
                : new ApplicationPackageSettings(false, null, 0);
        }

        return PackageIsolation.TryParseHostPort(runner, out var host, out var port)
            ? new ApplicationPackageSettings(on, host, port)
            : throw new InvalidOperationException($"'{RunnerSetting}' must be a host and a port from 1 to 65535, as in 'host:port'.");
    }
}
