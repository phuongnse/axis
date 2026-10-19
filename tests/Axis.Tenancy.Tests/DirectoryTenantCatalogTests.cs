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

    [Fact]
    public void On_other_schema_version_lists_the_tenants_that_differ_in_ordinal_order_and_survives_withhold()
    {
        var catalog = Catalog(("c", "key-c"), ("a", "key-a"), ("b", "key-b"));
        catalog = new DirectoryTenantCatalog(
            catalog.Tenants,
            [],
            new Dictionary<string, string> { ["a"] = "k", ["b"] = "k", ["c"] = "k" },
            new Dictionary<string, string> { ["a"] = "2", ["b"] = "1", ["c"] = "3" });

        Assert.Equal(["a", "b", "c"], catalog.OnOtherSchemaVersion("0"));
        Assert.Equal(["b", "c"], catalog.OnOtherSchemaVersion("2"));
        Assert.Equal(["b"], catalog.Withhold(["a"]).OnOtherSchemaVersion("3"));
        Assert.Empty(catalog.Withhold(["a", "b", "c"]).OnOtherSchemaVersion("9"));
    }

    [Fact]
    public void Oidc_changes_list_new_tenants_and_tenants_on_other_sign_in_settings_in_ordinal_order()
    {
        var previous = Catalog(OidcKeys("a", "oidc-a"), OidcKeys("b", "oidc-b"), OidcKeys("d", "oidc-d"));
        var next = Catalog(OidcKeys("a", "oidc-a"), OidcKeys("b", "other"), OidcKeys("d", "oidc-d"), OidcKeys("c", "oidc-c"));

        Assert.Equal(["b", "c"], next.OidcChanges(previous));
        Assert.Empty(previous.OidcChanges(previous));
    }

    [Fact]
    public void A_withheld_tenant_is_listed_in_oidc_changes_when_the_next_read_holds_it()
    {
        var read = Catalog(OidcKeys("a", "oidc-a"), OidcKeys("b", "oidc-b"));
        var served = read.Withhold(["a"]);

        Assert.Empty(served.OidcChanges(read.Withhold(["a"])));
        Assert.Equal(["a"], read.OidcChanges(served));
    }

    private static (string Id, string Key, string OidcKey) OidcKeys(string id, string oidcKey) => (id, $"key-{id}", oidcKey);

    private static DirectoryTenantCatalog Catalog(params (string Id, string Key)[] tenants) =>
        Catalog(tenants.Select(tenant => (tenant.Id, tenant.Key, OidcKey: tenant.Key)).ToArray());

    private static DirectoryTenantCatalog Catalog(params (string Id, string Key, string OidcKey)[] tenants) =>
        new(
            tenants.Select(tenant => new TenantEntry(
                tenant.Id,
                [$"{tenant.Id}.example.test"],
                new SecretValue(Tenants.ConnectionString),
                SchemaConnectionString: null,
                Oidc: null,
                new Dictionary<string, TenantBinding>())),
            [],
            tenants.ToDictionary(tenant => tenant.Id, tenant => tenant.Key),
            new Dictionary<string, string>(),
            tenants.ToDictionary(tenant => tenant.Id, tenant => tenant.OidcKey));
}
