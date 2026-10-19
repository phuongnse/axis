namespace Axis.Tenancy.Tests;

public sealed class DirectoryTenantCatalogTests
{
    [Fact]
    public void Withhold_removes_the_tenants_and_makes_their_hosts_unavailable()
    {
        var catalog = Catalog(("a", "key-a"), ("b", "key-b"));

        var withheld = catalog.Withhold(["a"]);

        Assert.Equal(["b"], withheld.Tenants.Select(tenant => tenant.Id));
        Assert.Null(withheld.FindByHost("a.example.test"));
        Assert.True(withheld.IsUnavailableHost("A.example.test"));
        Assert.NotNull(withheld.FindByHost("b.example.test"));
        Assert.False(withheld.IsUnavailableHost("b.example.test"));
    }

    [Fact]
    public void A_withheld_tenant_is_checked_again_when_the_next_read_holds_it()
    {
        var read = Catalog(("a", "key-a"), ("b", "key-b"));
        var served = read.Withhold(["a"]);

        Assert.Empty(served.DatabaseChanges(read.Withhold(["a"])));
        Assert.Equal(["a"], read.DatabaseChanges(served));
    }

    [Fact]
    public void Database_changes_list_new_tenants_and_tenants_on_other_database_settings_in_ordinal_order()
    {
        var previous = Catalog(("a", "key-a"), ("b", "key-b"), ("d", "key-d"));
        var next = Catalog(("a", "key-a"), ("b", "other"), ("d", "key-d"), ("c", "key-c"));

        Assert.Equal(["b", "c"], next.DatabaseChanges(previous));
        Assert.Empty(previous.DatabaseChanges(previous));
    }

    private static DirectoryTenantCatalog Catalog(params (string Id, string Key)[] tenants) =>
        new(
            tenants.Select(tenant => new TenantEntry(
                tenant.Id,
                [$"{tenant.Id}.example.test"],
                new SecretValue(Tenants.ConnectionString),
                SchemaConnectionString: null,
                Oidc: null,
                new Dictionary<string, TenantBinding>())),
            [],
            tenants.ToDictionary(tenant => tenant.Id, tenant => tenant.Key));
}
