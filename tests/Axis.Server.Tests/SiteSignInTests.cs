using Axis.Configuration.Realms;
using Axis.Server.Authentication;
using Axis.Tenancy;

namespace Axis.Server.Tests;

/// <summary>A sign-in that starts on a site page reads the site from the first segment of the return address, and goes straight to a site's only provider.</summary>
public sealed class SiteSignInTests
{
    [Theory]
    [InlineData("/sales/orders?x=1", "sales")]
    [InlineData("/sales", "sales")]
    [InlineData("/sales?x=1", "sales")]
    [InlineData("/sales#top", "sales")]
    [InlineData("/Sales", "Sales")]
    [InlineData("/", null)]
    [InlineData("/?a", null)]
    [InlineData("/#a", null)]
    [InlineData("//sales", null)]
    public void The_first_segment_is_the_text_after_the_leading_slash_up_to_the_next_separator(string returnUrl, string? expected)
    {
        Assert.Equal(expected, SiteSignIn.FirstSegment(returnUrl));
    }

    [Fact]
    public void The_site_client_id_names_the_path()
    {
        Assert.Equal("site-sales", SiteSignIn.ClientId("sales"));
    }

    [Fact]
    public void One_provider_and_no_accounts_gives_the_hint()
    {
        Assert.Equal("oidc-issuer", SiteSignIn.ProviderHint(new RealmSiteClient("sales", Accounts: false, ["oidc-issuer"])));
    }

    [Fact]
    public void One_provider_with_accounts_gives_no_hint()
    {
        Assert.Null(SiteSignIn.ProviderHint(new RealmSiteClient("sales", Accounts: true, ["oidc-issuer"])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void No_provider_gives_no_hint(bool accounts)
    {
        Assert.Null(SiteSignIn.ProviderHint(new RealmSiteClient("sales", accounts, [])));
    }

    [Fact]
    public void Two_providers_give_no_hint()
    {
        Assert.Null(SiteSignIn.ProviderHint(new RealmSiteClient("sales", Accounts: false, ["oidc-a", "oidc-b"])));
    }

    [Fact]
    public void A_site_allows_a_provider_in_its_list()
    {
        Assert.True(SiteSignIn.Allows(new RealmSiteClient("sales", Accounts: false, ["oidc-a", "oidc-b"]), "oidc-b"));
    }

    [Theory]
    [InlineData("oidc-c")]
    [InlineData("OIDC-A")]
    [InlineData("accounts")]
    public void A_site_does_not_allow_a_provider_outside_its_list(string alias)
    {
        Assert.False(SiteSignIn.Allows(new RealmSiteClient("sales", Accounts: true, ["oidc-a", "oidc-b"]), alias));
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, " ")]
    [InlineData(false, null)]
    [InlineData(false, "")]
    public void A_token_without_a_provider_claim_comes_from_accounts(bool accounts, string? claim)
    {
        Assert.Equal(accounts, SiteSignIn.Allows(new RealmSiteClient("sales", accounts, ["oidc-a"]), claim));
    }

    [Theory]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    [InlineData("main", true, false)]
    [InlineData(null, true, false, TenantSignInProfiles.Fapi2)]
    public void Only_a_standard_tenant_with_realm_details_that_is_not_a_preview_has_a_site_client_key(
        string? previewOf,
        bool hasRealm,
        bool expected,
        string profile = TenantSignInProfiles.Standard)
    {
        var oidc = new TenantOidc(
            "https://idp.example.test/realms/a", "axis", new SecretValue("platform-secret"), null, null, null, null, null, null, null,
            profile, profile == TenantSignInProfiles.Fapi2 ? [new TenantClientKey("key-1", new SecretValue("pem"))] : null);
        var tenant = new TenantEntry(
            "a", ["a.example.test"], new SecretValue("connection"), null, oidc, new Dictionary<string, TenantBinding>(), previewOf,
            Realm: hasRealm
                ? new TenantRealmAccess(new Uri("https://idp.example.test/"), "a", new SecretValue("activation"), new SecretValue("platform-secret"))
                : null);
        var problems = new List<string>();

        var settings = TenantOidcSettings.TryCreate(tenant, problems);

        Assert.Empty(problems);
        Assert.Equal(expected, settings!.SiteClientKey is not null);
        Assert.Equal(expected ? "platform-secret" : null, settings.SiteClientKey?.Reveal());
    }
}
