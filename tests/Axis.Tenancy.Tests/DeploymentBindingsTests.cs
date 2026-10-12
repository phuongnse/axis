using Axis.Core.Tenancy;

namespace Axis.Tenancy.Tests;

public sealed class DeploymentBindingsTests
{
    private readonly TenantOptions _options = Tenants.Options(
            ("tenant-a", ["a.example.test"], Tenants.ConnectionString),
            ("tenant-b", ["b.example.test"], Tenants.ConnectionString))
        .WithBinding("tenant-a", "PurchasingSystemToken", "secret", "a-s3cr3t-value")
        .WithBinding("tenant-b", "PurchasingSystemToken", "secret", "b-s3cr3t-value")
        .WithBinding("tenant-b", "ReportingToken", "secret", "b-reporting-s3cr3t")
        .WithBinding("tenant-a", "PurchasingSystemUrl", "url", "https://a.example.test/api")
        .WithBinding("tenant-b", "PurchasingSystemUrl", "url", "https://b.example.test/api");

    [Fact]
    public void Resolves_the_binding_of_the_current_tenant()
    {
        var bindings = Bindings("tenant-b");

        var binding = bindings.Resolve("PurchasingSystemUrl");

        Assert.Equal(new DeploymentBinding("PurchasingSystemUrl", "url", "https://b.example.test/api"), binding);
    }

    [Fact]
    public void Resolves_the_secret_of_the_current_tenant()
    {
        var bindings = Bindings("tenant-b");

        Assert.Equal("b-s3cr3t-value", bindings.ResolveSecret("PurchasingSystemToken").Reveal());
    }

    [Fact]
    public void Binding_name_is_matched_ignoring_letter_case()
    {
        var bindings = Bindings("tenant-a");

        Assert.Equal("https://a.example.test/api", bindings.Resolve("purchasingsystemurl").Value);
        Assert.Equal("a-s3cr3t-value", bindings.ResolveSecret("purchasingsystemtoken").Reveal());
    }

    [Fact]
    public void Resolving_a_secret_as_a_plain_binding_names_the_binding_and_no_value()
    {
        var bindings = Bindings("tenant-a");

        var exception = Assert.Throws<InvalidOperationException>(() => bindings.Resolve("PurchasingSystemToken"));

        Assert.Equal("Tenant 'tenant-a' binding 'PurchasingSystemToken' is a secret: resolve it with ResolveSecret.", exception.Message);
    }

    [Fact]
    public void Resolving_a_url_as_a_secret_names_the_binding_and_no_value()
    {
        var bindings = Bindings("tenant-a");

        var exception = Assert.Throws<InvalidOperationException>(() => bindings.ResolveSecret("PurchasingSystemUrl"));

        Assert.Equal("Tenant 'tenant-a' binding 'PurchasingSystemUrl' is not a secret.", exception.Message);
    }

    [Fact]
    public void Resolving_without_a_tenant_context_names_the_binding_and_no_value()
    {
        var bindings = new DeploymentBindings(_options, new TenantContextAccessor());

        var exception = Assert.Throws<InvalidOperationException>(() => bindings.ResolveSecret("PurchasingSystemToken"));

        Assert.Contains("No tenant context", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'PurchasingSystemToken'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolving_a_name_the_tenant_lacks_names_the_binding_and_no_value()
    {
        var bindings = Bindings("tenant-a");

        var exception = Assert.Throws<InvalidOperationException>(() => bindings.ResolveSecret("ReportingToken"));

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
        var binding = Bindings("tenant-a").Resolve("PurchasingSystemUrl");

        Assert.DoesNotContain("a.example.test", binding.ToString(), StringComparison.Ordinal);
        Assert.Contains("PurchasingSystemUrl", binding.ToString(), StringComparison.Ordinal);
    }

    private DeploymentBindings Bindings(string tenantId) =>
        new(_options, new TenantContextAccessor { Current = new TenantContext(tenantId) });
}
