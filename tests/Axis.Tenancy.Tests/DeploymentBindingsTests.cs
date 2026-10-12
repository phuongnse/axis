using Axis.Core.Tenancy;

namespace Axis.Tenancy.Tests;

public sealed class DeploymentBindingsTests
{
    private readonly TenantOptions _options = Tenants.Options(
            ("tenant-a", ["a.example.test"], Tenants.ConnectionString),
            ("tenant-b", ["b.example.test"], Tenants.ConnectionString))
        .WithBinding("tenant-a", "PurchasingSystemToken", "secret", "a-s3cr3t-value")
        .WithBinding("tenant-b", "PurchasingSystemToken", "secret", "b-s3cr3t-value")
        .WithBinding("tenant-b", "ReportingToken", "secret", "b-reporting-s3cr3t");

    [Fact]
    public void Resolves_the_binding_of_the_current_tenant()
    {
        var bindings = Bindings("tenant-b");

        var binding = bindings.Resolve("PurchasingSystemToken");

        Assert.Equal(new DeploymentBinding("PurchasingSystemToken", "secret", "b-s3cr3t-value"), binding);
    }

    [Fact]
    public void Binding_name_is_matched_ignoring_letter_case()
    {
        var bindings = Bindings("tenant-a");

        Assert.Equal("a-s3cr3t-value", bindings.Resolve("purchasingsystemtoken").Value);
    }

    [Fact]
    public void Resolving_without_a_tenant_context_names_the_binding_and_no_value()
    {
        var bindings = new DeploymentBindings(_options, new TenantContextAccessor());

        var exception = Assert.Throws<InvalidOperationException>(() => bindings.Resolve("PurchasingSystemToken"));

        Assert.Contains("No tenant context", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'PurchasingSystemToken'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolving_a_name_the_tenant_lacks_names_the_binding_and_no_value()
    {
        var bindings = Bindings("tenant-a");

        var exception = Assert.Throws<InvalidOperationException>(() => bindings.Resolve("ReportingToken"));

        Assert.Equal("Tenant 'tenant-a' has no deployment binding 'ReportingToken'.", exception.Message);
    }

    [Fact]
    public void Resolving_for_an_unconfigured_tenant_names_the_binding()
    {
        var bindings = Bindings("other");

        var exception = Assert.Throws<InvalidOperationException>(() => bindings.Resolve("PurchasingSystemToken"));

        Assert.Equal("Tenant 'other' has no deployment binding 'PurchasingSystemToken'.", exception.Message);
    }

    [Fact]
    public void Binding_text_leaves_out_the_value()
    {
        var binding = Bindings("tenant-a").Resolve("PurchasingSystemToken");

        Assert.DoesNotContain("s3cr3t", binding.ToString(), StringComparison.Ordinal);
        Assert.Contains("PurchasingSystemToken", binding.ToString(), StringComparison.Ordinal);
    }

    private DeploymentBindings Bindings(string tenantId) =>
        new(_options, new TenantContextAccessor { Current = new TenantContext(tenantId) });
}
