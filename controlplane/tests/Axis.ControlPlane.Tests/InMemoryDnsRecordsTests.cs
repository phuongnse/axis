using Axis.ControlPlane.Dns;

namespace Axis.ControlPlane.Tests;

public sealed class InMemoryDnsRecordsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Setting_a_record_twice_leaves_one_record()
    {
        var records = new InMemoryDnsRecords();

        await records.SetCnameAsync("acme.vn.example.com", "ingress.vn-1.vn.example.com", Token);
        await records.SetCnameAsync("acme.vn.example.com", "ingress.vn-1.vn.example.com", Token);

        var record = Assert.Single(records.Records);
        Assert.Equal("acme.vn.example.com", record.Key);
        Assert.Equal("ingress.vn-1.vn.example.com", record.Value);
    }

    [Fact]
    public async Task Setting_a_name_again_replaces_its_target_and_names_match_without_regard_to_case()
    {
        var records = new InMemoryDnsRecords();

        await records.SetCnameAsync("acme.vn.example.com", "ingress.vn-1.vn.example.com", Token);
        await records.SetCnameAsync("ACME.vn.example.com.", "ingress.vn-2.vn.example.com.", Token);

        var record = Assert.Single(records.Records);
        Assert.Equal("ingress.vn-2.vn.example.com", record.Value);
    }

    [Fact]
    public async Task Removing_a_record_twice_leaves_no_record_and_no_other_name_is_touched()
    {
        var records = new InMemoryDnsRecords();
        await records.SetCnameAsync("acme.vn.example.com", "ingress.vn-1.vn.example.com", Token);
        await records.SetCnameAsync("other.vn.example.com", "ingress.vn-1.vn.example.com", Token);

        await records.RemoveCnameAsync("acme.vn.example.com", Token);
        await records.RemoveCnameAsync("acme.vn.example.com", Token);

        Assert.Equal(["other.vn.example.com"], records.Records.Keys);
    }
}
