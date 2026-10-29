using Axis.Configuration.Model;
using Axis.Configuration.Realms;
using Axis.Configuration.Resources;
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

    [Fact]
    public void A_site_accepts_the_levels_of_the_allowed_providers_with_the_alias()
    {
        var model = Model(Provider("first", "issuer", ["2", "1"]), Provider("second", "ISSUER", ["3", "1"]), Provider("other", "elsewhere", ["9"]));

        var accepted = SiteSignIn.AcceptedAcr(model, Site("first", "second", "other"), "oidc-issuer");

        Assert.Equal(["2", "1", "3"], accepted);
    }

    [Fact]
    public void A_site_ignores_the_levels_of_a_provider_it_does_not_allow()
    {
        var model = Model(Provider("first", "issuer", ["2"]), Provider("second", "issuer", ["3"]));

        Assert.Equal(["2"], SiteSignIn.AcceptedAcr(model, Site("First"), "oidc-issuer"));
        Assert.Empty(SiteSignIn.AcceptedAcr(model, Site(), "oidc-issuer"));
    }

    [Fact]
    public void A_provider_without_a_list_gives_no_check()
    {
        var model = Model(Provider("first", "issuer", []), Provider("accounts", null, [], "accounts"));

        Assert.Empty(SiteSignIn.AcceptedAcr(model, Site("first", "accounts"), "oidc-issuer"));
        Assert.Empty(SiteSignIn.AcceptedAcr(model, Site("first", "accounts"), "oidc-unknown"));
        Assert.Null(SiteSignIn.AcrValues(model, Site("first", "accounts")));
    }

    [Fact]
    public void A_site_sends_the_distinct_levels_of_its_allowed_providers_in_ordinal_order()
    {
        var model = Model(Provider("first", "issuer", ["urn:high", "1"]), Provider("second", "other", ["1", "2"]), Provider("third", "third", ["9"]));

        Assert.Equal("1 2 urn:high", SiteSignIn.AcrValues(model, Site("first", "second")));
        Assert.Equal("9", SiteSignIn.AcrValues(model, Site("third")));
    }

    private static IdentityProviderModel Provider(string name, string? issuer, string[] acr, string type = "oidc") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        File = $"identity-providers/{name}.json",
        Type = type,
        IssuerBinding = issuer,
        Roles = new IdentityProviderRolesModel("roles", new Dictionary<string, string>()),
        Acr = acr,
    };

    private static ApplicationModel Model(params IdentityProviderModel[] providers) => new()
    {
        Manifest = new ApplicationManifest
        {
            Id = Guid.NewGuid(),
            Kind = ResourceKinds.Application,
            Name = "sample",
            FormatVersion = 1,
            File = "application.json",
        },
        Entities = [],
        IdentityProviders = providers,
    };

    private static SiteModel Site(params string[] signIn) => new()
    {
        Id = Guid.NewGuid(),
        Name = "main",
        Path = "main",
        Title = new TextReference("title"),
        Locales = new SiteLocales("en", "en", ["en"]),
        File = "sites/main.json",
        Navigation = [],
        SignIn = signIn,
    };
}
