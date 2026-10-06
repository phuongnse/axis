namespace Axis.Tenancy.Tests;

public sealed class TenantOptionsValidatorTests
{
    [Fact]
    public void Valid_tenants_have_no_problems()
    {
        var options = Tenants.Options(
            ("default", ["localhost"], Tenants.ConnectionString),
            ("tenant-2", ["a.example.test", "b.example.test"], Tenants.ConnectionString));

        Assert.Empty(TenantOptionsValidator.Validate(options));
    }

    [Fact]
    public void No_tenants_is_a_problem()
    {
        var problem = Assert.Single(TenantOptionsValidator.Validate(new TenantOptions()));

        Assert.Contains("No tenants are configured", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("tenant_a")]
    [InlineData("tenant.a")]
    [InlineData("tenant a")]
    public void Tenant_id_with_disallowed_characters_is_named(string tenantId)
    {
        var options = Tenants.Options((tenantId, ["localhost"], Tenants.ConnectionString));

        var problem = Assert.Single(TenantOptionsValidator.Validate(options));

        Assert.Contains($"Tenant id '{tenantId}' is invalid", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Tenant_without_hosts_is_named()
    {
        var options = Tenants.Options(("default", [], Tenants.ConnectionString));

        var problem = Assert.Single(TenantOptionsValidator.Validate(options));

        Assert.Equal("Tenant 'default' has no hosts.", problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Tenant_with_a_blank_host_is_named(string host)
    {
        var options = Tenants.Options(("default", ["localhost", host], Tenants.ConnectionString));

        var problem = Assert.Single(TenantOptionsValidator.Validate(options));

        Assert.Equal("Tenant 'default' has a blank host.", problem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Tenant_without_a_connection_string_is_named(string? connectionString)
    {
        var options = Tenants.Options(("default", ["localhost"], connectionString));

        var problem = Assert.Single(TenantOptionsValidator.Validate(options));

        Assert.Equal("Tenant 'default' has no connection string.", problem);
    }

    [Fact]
    public void Host_listed_under_two_tenants_names_the_host_and_both_tenants()
    {
        var options = Tenants.Options(
            ("tenant-a", ["shared.example.test"], Tenants.ConnectionString),
            ("tenant-b", ["Shared.Example.TEST"], Tenants.ConnectionString));

        var problem = Assert.Single(TenantOptionsValidator.Validate(options));

        Assert.Contains("Shared.Example.TEST", problem, StringComparison.Ordinal);
        Assert.Contains("'tenant-a'", problem, StringComparison.Ordinal);
        Assert.Contains("'tenant-b'", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_problem_is_reported()
    {
        var options = Tenants.Options(
            ("Bad", [], null),
            ("good", ["localhost"], Tenants.ConnectionString));

        var problems = TenantOptionsValidator.Validate(options);

        Assert.Equal(
            [
                "Tenant id 'Bad' is invalid: use only lowercase letters, digits and hyphens.",
                "Tenant 'Bad' has no hosts.",
                "Tenant 'Bad' has no connection string.",
            ],
            problems);
    }
}
