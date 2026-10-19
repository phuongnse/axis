using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Axis.Server.Http;

/// <summary>
/// The proxies whose forwarded headers the server trusts, from <c>ForwardedHeaders:KnownProxies</c> and
/// <c>ForwardedHeaders:KnownNetworks</c>. Both are empty by default, and then no forwarded header is applied.
/// </summary>
internal sealed class ForwardedHeadersSettings
{
    public const string ProxiesSetting = "ForwardedHeaders:KnownProxies";

    public const string NetworksSetting = "ForwardedHeaders:KnownNetworks";

    private ForwardedHeadersSettings(IReadOnlyList<IPAddress> proxies, IReadOnlyList<System.Net.IPNetwork> networks)
    {
        Proxies = proxies;
        Networks = networks;
    }

    public IReadOnlyList<IPAddress> Proxies { get; }

    public IReadOnlyList<System.Net.IPNetwork> Networks { get; }

    /// <summary>No proxy is named, so no forwarded header is trusted.</summary>
    public bool IsEmpty => Proxies.Count == 0 && Networks.Count == 0;

    /// <exception cref="InvalidOperationException">An entry is not an IP address or a CIDR range.</exception>
    public static ForwardedHeadersSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var proxies = (configuration.GetSection(ProxiesSetting).Get<string[]>() ?? [])
            .Select(entry => IPAddress.TryParse(entry, out var address)
                ? address
                : throw new InvalidOperationException($"{ProxiesSetting} has an entry that is not an IP address: '{entry}'."))
            .ToList();
        var networks = (configuration.GetSection(NetworksSetting).Get<string[]>() ?? [])
            .Select(entry => System.Net.IPNetwork.TryParse(entry, out var network)
                ? network
                : throw new InvalidOperationException($"{NetworksSetting} has an entry that is not a CIDR range: '{entry}'."))
            .ToList();
        return new ForwardedHeadersSettings(proxies, networks);
    }

    /// <summary>
    /// Replaces the framework's defaults, which trust loopback, with the named proxies. Reads only the nearest
    /// proxy's headers, and never <c>X-Forwarded-Prefix</c>.
    /// </summary>
    public void Apply(ForwardedHeadersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in Proxies)
        {
            options.KnownProxies.Add(proxy);
        }

        foreach (var network in Networks)
        {
            options.KnownIPNetworks.Add(network);
        }

        options.ForwardLimit = 1;
        options.ForwardedHeaders = IsEmpty
            ? ForwardedHeaders.None
            : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    }
}
