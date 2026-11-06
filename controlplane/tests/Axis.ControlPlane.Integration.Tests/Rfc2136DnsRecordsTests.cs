using Axis.ControlPlane.Dns;
using DnsClient;
using DnsClient.Protocol;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>The RFC 2136 provider against a real BIND server. Each test has names of its own on the shared server.</summary>
public sealed class Rfc2136DnsRecordsTests(BindFixture bind)
{
    private const string Target = "ingress.vn-1.vn.example.com";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_record_that_is_set_resolves_with_a_time_to_live_of_300_and_after_removal_it_is_gone()
    {
        var records = await NewProviderAsync(BindFixture.Key);
        var client = await bind.ClientAsync();
        var name = NewName();

        await records.SetCnameAsync(name, Target, Token);

        var record = Assert.Single(await CnamesAsync(client, name));
        Assert.Equal(Target, record.CanonicalName.Value.TrimEnd('.'));
        Assert.Equal(300, record.TimeToLive);

        await records.RemoveCnameAsync(name, Token);

        Assert.Empty(await CnamesAsync(client, name));
    }

    [Fact]
    public async Task Setting_a_record_twice_leaves_one_record_and_a_new_target_replaces_the_old_one()
    {
        var records = await NewProviderAsync(BindFixture.Key);
        var client = await bind.ClientAsync();
        var name = NewName();

        await records.SetCnameAsync(name, Target, Token);
        await records.SetCnameAsync(name, Target, Token);

        Assert.Equal(Target, Assert.Single(await CnamesAsync(client, name)).CanonicalName.Value.TrimEnd('.'));

        await records.SetCnameAsync(name, "ingress.vn-2.vn.example.com", Token);

        Assert.Equal("ingress.vn-2.vn.example.com", Assert.Single(await CnamesAsync(client, name)).CanonicalName.Value.TrimEnd('.'));
    }

    [Fact]
    public async Task Removing_a_record_twice_leaves_no_record_and_the_other_names_are_untouched()
    {
        var records = await NewProviderAsync(BindFixture.Key);
        var client = await bind.ClientAsync();
        var name = NewName();
        var other = NewName();
        await records.SetCnameAsync(name, Target, Token);
        await records.SetCnameAsync(other, Target, Token);

        await records.RemoveCnameAsync(name, Token);
        await records.RemoveCnameAsync(name, Token);

        Assert.Empty(await CnamesAsync(client, name));
        Assert.Single(await CnamesAsync(client, other));
    }

    [Fact]
    public async Task An_update_signed_with_a_wrong_key_is_refused_and_the_error_holds_neither_key()
    {
        var records = await NewProviderAsync(BindFixture.WrongKey);
        var client = await bind.ClientAsync();
        var name = NewName();

        var exception = await Assert.ThrowsAsync<DnsUpdateException>(() => records.SetCnameAsync(name, Target, Token));

        Assert.DoesNotContain(BindFixture.Key, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(BindFixture.WrongKey, exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("NOTAUTH", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await CnamesAsync(client, name));
    }

    [Fact]
    public async Task A_name_outside_the_zone_is_refused_before_anything_is_sent()
    {
        var records = await NewProviderAsync(BindFixture.Key);

        await Assert.ThrowsAsync<ArgumentException>(() => records.SetCnameAsync("acme.other.example.org", Target, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => records.RemoveCnameAsync("acme.notvn.example.com", Token));
    }

    private async Task<Rfc2136DnsRecords> NewProviderAsync(string key) =>
        new(
            new ControlPlaneDnsOptions
            {
                Provider = DnsProviders.Rfc2136,
                Server = await bind.HostAsync(),
                Port = await bind.PortAsync(),
                Zone = BindFixture.Zone,
                KeyName = BindFixture.KeyName,
                Key = key,
            },
            TimeProvider.System);

    private static string NewName() => $"t{Guid.NewGuid():N}.{BindFixture.Zone}";

    private static async Task<IReadOnlyList<CNameRecord>> CnamesAsync(LookupClient client, string name)
    {
        var answer = await client.QueryAsync(name, QueryType.CNAME, cancellationToken: Token);
        return [.. answer.Answers.CnameRecords()];
    }
}
