using System.Net;
using Axis.Server.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace Axis.Server.Tests;

public sealed class ForwardedHeadersSettingsTests
{
    [Fact]
    public void Proxies_and_networks_of_both_address_families_are_read()
    {
        var settings = Load(
            ("ForwardedHeaders:KnownProxies:0", "10.0.0.5"),
            ("ForwardedHeaders:KnownProxies:1", "2001:db8::1"),
            ("ForwardedHeaders:KnownNetworks:0", "192.168.0.0/16"),
            ("ForwardedHeaders:KnownNetworks:1", "2001:db8::/32"));

        Assert.Equal([IPAddress.Parse("10.0.0.5"), IPAddress.Parse("2001:db8::1")], settings.Proxies);
        Assert.Equal(
            [System.Net.IPNetwork.Parse("192.168.0.0/16"), System.Net.IPNetwork.Parse("2001:db8::/32")],
            settings.Networks);
        Assert.False(settings.IsEmpty);
    }

    [Fact]
    public void Empty_lists_trust_no_forwarded_header()
    {
        var settings = Load();
        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.All };

        settings.Apply(options);

        Assert.True(settings.IsEmpty);
        Assert.Equal(ForwardedHeaders.None, options.ForwardedHeaders);
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public void Apply_replaces_the_loopback_defaults_and_reads_the_nearest_proxy_only()
    {
        var settings = Load(("ForwardedHeaders:KnownProxies:0", "10.0.0.5"), ("ForwardedHeaders:KnownNetworks:0", "172.16.0.0/12"));
        var options = new ForwardedHeadersOptions { ForwardLimit = 5 };
        Assert.Contains(IPAddress.IPv6Loopback, options.KnownProxies);

        settings.Apply(options);

        Assert.Equal([IPAddress.Parse("10.0.0.5")], options.KnownProxies);
        Assert.Equal([System.Net.IPNetwork.Parse("172.16.0.0/12")], options.KnownIPNetworks);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Equal(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
            options.ForwardedHeaders);
    }

    private static ForwardedHeadersSettings Load(params (string Key, string Value)[] values) =>
        ForwardedHeadersSettings.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build());
}
