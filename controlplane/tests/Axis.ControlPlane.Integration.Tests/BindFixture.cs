using System.Net;
using System.Text;
using DnsClient;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

[assembly: AssemblyFixture(typeof(Axis.ControlPlane.Integration.Tests.BindFixture))]

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// One real BIND container, a DNS server that takes dynamic updates signed with a TSIG key, shared by the whole run. It starts
/// the first time a test needs it, so a run that needs no DNS server never starts one. The fixture removes the container when
/// the run ends. Every test uses names of its own in the zone.
/// </summary>
public sealed class BindFixture : IAsyncDisposable
{
    public const string Image = "internetsystemsconsortium/bind9:9.20";

    /// <summary>The zone that the server holds and lets the key update.</summary>
    public const string Zone = "vn.example.com";

    public const string KeyName = "axis-controlplane";

    /// <summary>The base64 secret of <see cref="KeyName"/>, in the form that <c>tsig-keygen</c> writes.</summary>
    public const string Key = "c2VjcmV0LWtleS1ieXRlcy0xMjM0NTY3ODkwMTIzNDU2";

    /// <summary>A valid base64 secret that the server does not know.</summary>
    public const string WrongKey = "d3Jvbmcta2V5LWJ5dGVzLTk4NzY1NDMyMTA5ODc2NTQ=";

    private const int DnsContainerPort = 53;

    private const string NamedConf = $$"""
        key "{{KeyName}}" { algorithm hmac-sha256; secret "{{Key}}"; };

        options {
            directory "/var/cache/bind";
            recursion no;
            listen-on { any; };
            allow-query { any; };
        };

        zone "{{Zone}}" {
            type primary;
            file "/var/lib/bind/{{Zone}}.zone";
            allow-update { key "{{KeyName}}"; };
        };
        """;

    private const string ZoneFile = $$"""
        $TTL 300
        @ IN SOA ns.{{Zone}}. hostmaster.{{Zone}}. 1 3600 600 86400 300
        @ IN NS ns.{{Zone}}.
        ns IN A 127.0.0.1
        """;

    // The start takes no test's cancellation token, so cancelling one test does not fail the other.
    private readonly Lazy<Task<IContainer>> _container = new(StartContainerAsync);

    /// <summary>Starts the container if no test has yet, and returns the host that the DNS port is published on.</summary>
    public async Task<string> HostAsync() => (await _container.Value).Hostname;

    /// <summary>Starts the container if no test has yet, and returns the published DNS port, for TCP.</summary>
    public async Task<int> PortAsync() => (await _container.Value).GetMappedPublicPort(DnsContainerPort);

    /// <summary>A client that asks the server over TCP and keeps no cache.</summary>
    public async Task<LookupClient> ClientAsync()
    {
        var container = await _container.Value;
        var address = (await System.Net.Dns.GetHostAddressesAsync(container.Hostname))[0];
        return NewClient(address, container.GetMappedPublicPort(DnsContainerPort));
    }

    public async ValueTask DisposeAsync()
    {
        // A failed start already removed its container.
        if (_container.IsValueCreated && _container.Value.IsCompletedSuccessfully)
        {
            await _container.Value.Result.DisposeAsync();
        }
    }

    private static LookupClient NewClient(IPAddress address, int port) =>
        new(new LookupClientOptions(new IPEndPoint(address, port)) { UseTcpOnly = true, UseCache = false, Retries = 0, Timeout = TimeSpan.FromSeconds(5) });

    private static async Task<IContainer> StartContainerAsync()
    {
        var container = new ContainerBuilder(Image)
            .WithPortBinding(DnsContainerPort, assignRandomHostPort: true)
            .WithResourceMapping(Encoding.UTF8.GetBytes(NamedConf), "/etc/bind/named.conf")
            .WithResourceMapping(Encoding.UTF8.GetBytes(ZoneFile), $"/var/lib/bind/{Zone}.zone")
            .Build();
        try
        {
            await container.StartAsync();
            await WaitUntilAnswersAsync(container);
            return container;
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    // The published port accepts connections before BIND does, so ask for the zone's SOA record until it answers.
    private static async Task WaitUntilAnswersAsync(IContainer container)
    {
        var address = (await System.Net.Dns.GetHostAddressesAsync(container.Hostname))[0];
        var client = NewClient(address, container.GetMappedPublicPort(DnsContainerPort));
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            try
            {
                var answer = await client.QueryAsync(Zone, QueryType.SOA);
                if (!answer.HasError && answer.Answers.Count > 0)
                {
                    return;
                }
            }
            catch (DnsResponseException)
            {
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The BIND container did not answer for its zone within 60 seconds.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }
}
