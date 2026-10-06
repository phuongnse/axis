using Axis.Core.Tenancy;

namespace Axis.Tenancy.Tests;

public sealed class TenantConnectionFactoryTests
{
    private readonly TenantOptions _options = Tenants.Options(("default", ["localhost"], Tenants.ConnectionString));

    [Fact]
    public async Task Opening_a_connection_without_a_tenant_context_throws()
    {
        await using var factory = new TenantConnectionFactory(_options, new TenantContextAccessor());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await factory.OpenConnectionAsync(TestContext.Current.CancellationToken));

        Assert.Contains("No tenant context", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opening_a_connection_for_an_unconfigured_tenant_throws()
    {
        var accessor = new TenantContextAccessor { Current = new TenantContext("other") };
        await using var factory = new TenantConnectionFactory(_options, accessor);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await factory.OpenConnectionAsync(TestContext.Current.CancellationToken));

        Assert.Contains("'other'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tenant_context_does_not_leak_to_other_asynchronous_flows()
    {
        var accessor = new TenantContextAccessor();

        await Task.Run(() => accessor.Current = new TenantContext("default"), TestContext.Current.CancellationToken);

        Assert.Null(accessor.Current);
    }
}
