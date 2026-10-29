using Axis.Tenancy.Platform;
using Microsoft.Extensions.Configuration;

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

    [Fact]
    public void A_row_with_an_activation_secret_gets_a_realm_whose_secret_decrypts_to_the_stored_value()
    {
        var cipher = CipherFor();
        var row = Row(cipher);

        var realm = DirectoryTenantCatalog.Realm(row, cipher);

        Assert.NotNull(realm);
        Assert.Equal(new Uri("https://idp.example.test/"), realm.BaseAddress);
        Assert.Equal("acme", realm.Name);
        Assert.Equal(ActivationSecret, realm.ActivationClientSecret.Reveal());
        Assert.DoesNotContain(ActivationSecret, realm.ToString(), StringComparison.Ordinal);
        Assert.Equal(PlatformSecret, realm.PlatformClientSecret!.Reveal());
        Assert.DoesNotContain(PlatformSecret, realm.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_without_an_oidc_client_secret_gets_a_realm_without_a_platform_secret()
    {
        var cipher = CipherFor();
        var row = Row(cipher);
        row.OidcClientSecret = [];

        var realm = DirectoryTenantCatalog.Realm(row, cipher);

        Assert.NotNull(realm);
        Assert.Null(realm.PlatformClientSecret);
    }

    [Fact]
    public void A_preview_row_gets_no_realm()
    {
        var cipher = CipherFor();
        var row = Row(cipher);
        row.PreviewOf = "main";

        Assert.Null(DirectoryTenantCatalog.Realm(row, cipher));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_row_without_an_activation_secret_gets_no_realm(bool empty)
    {
        var cipher = CipherFor();
        var row = Row(cipher);
        row.RealmActivationClientSecret = empty ? [] : null;

        Assert.Null(DirectoryTenantCatalog.Realm(row, cipher));
    }

    [Fact]
    public void A_row_that_signs_in_with_an_outside_identity_provider_gets_no_realm()
    {
        var cipher = CipherFor();
        var row = Row(cipher);
        row.OidcAuthority = "https://login.example.test/realms/other";

        Assert.Null(DirectoryTenantCatalog.Realm(row, cipher));
    }

    [Fact]
    public void A_row_with_a_users_secret_gets_realm_users_whose_secret_decrypts_to_the_stored_value()
    {
        var cipher = CipherFor();
        var row = Row(cipher);

        var users = DirectoryTenantCatalog.RealmUsers(row, cipher);

        Assert.NotNull(users);
        Assert.Equal(new Uri("https://idp.example.test/"), users.BaseAddress);
        Assert.Equal("acme", users.Realm);
        Assert.Equal(UsersSecret, users.ClientSecret.Reveal());
        Assert.DoesNotContain(UsersSecret, users.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_preview_row_gets_no_realm_users()
    {
        var cipher = CipherFor();
        var row = Row(cipher);
        row.PreviewOf = "main";

        Assert.Null(DirectoryTenantCatalog.RealmUsers(row, cipher));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_row_without_a_users_secret_gets_no_realm_users(bool empty)
    {
        var cipher = CipherFor();
        var row = Row(cipher);
        row.RealmUsersClientSecret = empty ? [] : null;

        Assert.Null(DirectoryTenantCatalog.RealmUsers(row, cipher));
    }

    [Fact]
    public void A_row_that_signs_in_with_an_outside_identity_provider_gets_no_realm_users()
    {
        var cipher = CipherFor();
        var row = Row(cipher);
        row.OidcAuthority = "https://login.example.test/realms/other";

        Assert.Null(DirectoryTenantCatalog.RealmUsers(row, cipher));
    }

    [Fact]
    public void Bindings_decrypt_a_secret_only_when_it_is_revealed_and_match_names_ignoring_letter_case()
    {
        var cipher = CipherFor();
        var rows = new[]
        {
            new TenantBindingRow { TenantId = "acme", Name = "Issuer", Kind = DeploymentBindingKinds.Url, Url = "https://login.example.test" },
            new TenantBindingRow
            {
                TenantId = "acme",
                Name = "Secret",
                Kind = DeploymentBindingKinds.Secret,
                SecretValue = cipher.Encrypt("acme", TenantSecretFields.Binding("Secret"), "binding-v4lue"),
            },
        };

        var bindings = DirectoryTenantCatalog.Bindings("acme", rows, cipher);

        Assert.Equal("https://login.example.test", bindings["issuer"].Url);
        Assert.Equal("binding-v4lue", bindings["SECRET"].Secret!.Reveal());
    }

    private const string ActivationSecret = "activ4tion-cl13nt-s3cret";
    private const string PlatformSecret = "pl4tform-cl13nt-s3cret";

    private const string UsersSecret = "us3rs-cl13nt-s3cret";

    private static TenantRow Row(TenantSecretCipher cipher) =>
        new()
        {
            Id = "acme",
            Status = "active",
            OidcAuthority = "https://idp.example.test/realms/acme",
            RealmActivationClientSecret = cipher.Encrypt("acme", TenantSecretFields.RealmActivationClientSecret, ActivationSecret),
            RealmUsersClientSecret = cipher.Encrypt("acme", TenantSecretFields.RealmUsersClientSecret, UsersSecret),
            OidcClientSecret = cipher.Encrypt("acme", TenantSecretFields.OidcClientSecret, PlatformSecret),
        };

    private static TenantSecretCipher CipherFor() =>
        TenantSecretCipher.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Platform:SecretKey"] = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()),
            })
            .Build());

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
