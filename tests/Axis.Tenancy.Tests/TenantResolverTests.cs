namespace Axis.Tenancy.Tests;

public sealed class TenantResolverTests
{
    private readonly TenantResolver _resolver = new(Tenants.Options(
        ("tenant-a", ["a.example.test"], Tenants.ConnectionString),
        ("tenant-b", ["b.example.test", "localhost"], Tenants.ConnectionString)));

    [Theory]
    [InlineData("a.example.test", "tenant-a")]
    [InlineData("A.Example.TEST", "tenant-a")]
    [InlineData("b.example.test", "tenant-b")]
    [InlineData("LOCALHOST", "tenant-b")]
    public void Host_resolves_its_tenant_ignoring_letter_case(string host, string tenantId)
    {
        Assert.True(_resolver.TryResolve(host, out var tenant));
        Assert.Equal(tenantId, tenant.TenantId);
    }

    [Theory]
    [InlineData("unknown.example")]
    [InlineData("example.test")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_or_empty_host_resolves_no_tenant(string? host)
    {
        Assert.False(_resolver.TryResolve(host, out var tenant));
        Assert.Null(tenant);
    }
}
