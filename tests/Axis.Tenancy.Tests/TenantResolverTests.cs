namespace Axis.Tenancy.Tests;

public sealed class TenantResolverTests
{
    private readonly TenantResolver _resolver = new(ConfigurationTenantCatalog.FromOptions(Tenants.Options(
        ("tenant-a", ["a.example.test"], Tenants.ConnectionString),
        ("tenant-b", ["b.example.test", "localhost"], Tenants.ConnectionString))));

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

    [Fact]
    public void A_host_of_a_known_tenant_that_is_not_served_resolves_no_tenant_and_is_unavailable()
    {
        var catalog = new DirectoryTenantCatalog([], ["off.example.test"], new Dictionary<string, string>());
        var resolver = new TenantResolver(catalog);

        Assert.False(resolver.TryResolve("off.example.test", out _));
        Assert.True(resolver.IsUnavailable("OFF.example.test"));
        Assert.False(resolver.IsUnavailable("unknown.example.test"));
        Assert.False(resolver.IsUnavailable(null));
        Assert.False(_resolver.IsUnavailable("a.example.test"));
    }
}
