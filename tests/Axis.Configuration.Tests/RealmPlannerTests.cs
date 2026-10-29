using Axis.Configuration.Model;
using Axis.Configuration.Realms;

namespace Axis.Configuration.Tests;

public sealed class RealmPlannerTests
{
    private static readonly IdentityProviderClaimsModel _claims = new("name", "email", "locale");

    [Fact]
    public void Two_applications_whose_providers_name_the_same_bindings_in_different_letter_case_share_one_provider()
    {
        var first = Application("Sales", Oidc("CompanyIssuer", "CompanyClient", "CompanySecret"));
        var second = Application("Hr", Oidc("companyissuer", "COMPANYCLIENT", "companysecret", rolesClaim: "groups"));

        var result = RealmPlanner.Build([first, second]);

        var provider = Assert.Single(result.Plan.Providers);
        Assert.Equal("oidc-companyissuer", provider.Alias);
        Assert.Equal(["groups", "roles"], provider.RolesClaims);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void The_plan_is_the_same_whatever_the_order_of_the_applications()
    {
        var first = Application("Sales", Oidc("IssuerA", "ClientA", "SecretA"));
        var second = Application("Hr", Oidc("IssuerB", "ClientB", "SecretB"));

        Assert.Equal(RealmPlanner.Build([first, second]).Plan.Providers.Select(provider => provider.Alias), RealmPlanner.Build([second, first]).Plan.Providers.Select(provider => provider.Alias));
        Assert.Equal(["oidc-issuera", "oidc-issuerb"], RealmPlanner.Build([second, first]).Plan.Providers.Select(provider => provider.Alias));
    }

    [Fact]
    public void A_provider_that_shares_the_issuer_binding_with_another_client_id_is_a_conflict_for_both_applications()
    {
        var first = Application("Sales", Oidc("Issuer", "ClientA", "Secret"));
        var second = Application("Hr", Oidc("Issuer", "ClientB", "Secret"));

        var result = RealmPlanner.Build([first, second]);

        Assert.All(result.Conflicts, conflict => Assert.Equal(RealmConflictKind.ClientBindings, conflict.Kind));
        Assert.Equal(
            new[] { first.ApplicationId, second.ApplicationId }.Order(),
            result.Conflicts.Select(conflict => conflict.ApplicationId).Order());
        var conflict = Assert.Single(result.Conflicts, conflict => conflict.ApplicationId == second.ApplicationId);
        Assert.Equal("Sales", conflict.Other.Name);
    }

    [Fact]
    public void A_provider_with_the_same_bindings_and_other_claims_is_a_claims_conflict()
    {
        var first = Application("Sales", Oidc("Issuer", "Client", "Secret"));
        var second = Application("Hr", Oidc("Issuer", "Client", "Secret", claims: new IdentityProviderClaimsModel("fullName", "email", "locale")));

        var result = RealmPlanner.Build([first, second]);

        Assert.All(result.Conflicts, conflict => Assert.Equal(RealmConflictKind.Claims, conflict.Kind));
        Assert.Equal(2, result.Conflicts.Count);
        Assert.Single(result.Plan.Providers);
    }

    [Fact]
    public void Providers_with_other_issuer_bindings_do_not_conflict()
    {
        var result = RealmPlanner.Build([Application("Sales", Oidc("IssuerA", "Client", "Secret"), Oidc("IssuerB", "Client", "Secret"))]);

        Assert.Empty(result.Conflicts);
        Assert.Equal(2, result.Plan.Providers.Count);
    }

    [Fact]
    public void A_site_with_sign_in_lists_only_the_providers_it_allows_and_a_site_without_lists_every_provider()
    {
        var accounts = Provider("accounts", "accounts");
        var company = Oidc("Issuer", "Client", "Secret");
        var other = Oidc("OtherIssuer", "Client", "Secret", name: "other");
        var application = Application(
            "Sales",
            [new RealmApplicationSite("restricted", ["COMPANY"]), new RealmApplicationSite("open", null)],
            accounts,
            company,
            other);

        var plan = RealmPlanner.Build([application]).Plan;

        Assert.Equal(["open", "restricted"], plan.Sites.Select(site => site.Path));
        var restricted = plan.Sites[1];
        Assert.False(restricted.Accounts);
        Assert.Equal(["oidc-issuer"], restricted.Providers);
        var open = plan.Sites[0];
        Assert.True(open.Accounts);
        Assert.Equal(["oidc-issuer", "oidc-otherissuer"], open.Providers);
    }

    [Fact]
    public void Accounts_and_saml_providers_without_a_metadata_binding_are_not_planned()
    {
        var result = RealmPlanner.Build([Application("Sales", Provider("accounts", "accounts"), Provider("saml", "saml"))]);

        Assert.Empty(result.Plan.Providers);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void Saml_providers_that_name_the_same_metadata_binding_in_other_letter_case_share_one_provider()
    {
        var result = RealmPlanner.Build([
            Application("Sales", Saml("Metadata")),
            Application("Support", Saml("METADATA", name: "partner")),
        ]);

        var provider = Assert.Single(result.Plan.Providers);
        Assert.Equal("saml-metadata", provider.Alias);
        Assert.Equal("saml", provider.Type);
        Assert.Equal("metadata", provider.MetadataBinding, ignoreCase: true);
        Assert.Null(provider.IssuerBinding);
        Assert.Null(provider.ClientIdBinding);
        Assert.Empty(result.Conflicts);
        Assert.All(result.Plan.Sites, site => Assert.Equal(["saml-metadata"], site.Providers));
    }

    [Fact]
    public void Saml_providers_that_name_the_same_metadata_binding_with_other_claims_conflict()
    {
        var result = RealmPlanner.Build([
            Application("Sales", Saml("Metadata")),
            Application("Support", Saml("Metadata", claims: new IdentityProviderClaimsModel("cn", "mail", "lang"))),
        ]);

        Assert.NotEmpty(result.Conflicts);
        Assert.All(result.Conflicts, conflict => Assert.Equal(RealmConflictKind.Claims, conflict.Kind));
    }

    [Fact]
    public void A_platform_saml_provider_shares_one_provider_with_an_application_saml_provider()
    {
        var result = RealmPlanner.Build([Application("Sales", Saml("Metadata"))], [Saml("METADATA", name: "platform", rolesClaim: "groups")]);

        var provider = Assert.Single(result.Plan.Providers);
        Assert.Equal("saml-metadata", provider.Alias);
        Assert.Equal(["groups", "roles"], provider.RolesClaims);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void A_saml_provider_and_an_oidc_provider_with_the_same_binding_name_do_not_share()
    {
        var result = RealmPlanner.Build([Application("Sales", Saml("Same"), Oidc("Same", "Client", "Secret", name: "other"))]);

        Assert.Equal(["oidc-same", "saml-same"], result.Plan.Providers.Select(provider => provider.Alias));
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void A_platform_provider_shares_one_provider_with_an_application_provider_that_names_the_same_bindings_in_other_letter_case()
    {
        var application = Application("Sales", Oidc("CompanyIssuer", "CompanyClient", "CompanySecret"));
        var platform = Oidc("COMPANYISSUER", "companyclient", "companysecret", name: "platform", rolesClaim: "groups");

        var result = RealmPlanner.Build([application], [platform]);

        var provider = Assert.Single(result.Plan.Providers);
        Assert.Equal("oidc-companyissuer", provider.Alias);
        Assert.Equal(["groups", "roles"], provider.RolesClaims);
        Assert.Empty(result.Conflicts);
        var site = Assert.Single(result.Plan.Sites);
        Assert.Equal(["oidc-companyissuer"], site.Providers);
    }

    [Fact]
    public void A_provider_that_only_the_platform_names_is_planned_and_belongs_to_no_site()
    {
        var application = Application("Sales", Oidc("IssuerA", "ClientA", "SecretA"));

        var result = RealmPlanner.Build([application], [Oidc("IssuerB", "ClientB", "SecretB", name: "platform")]);

        Assert.Equal(["oidc-issuera", "oidc-issuerb"], result.Plan.Providers.Select(provider => provider.Alias));
        Assert.Equal(["oidc-issuera"], Assert.Single(result.Plan.Sites).Providers);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void A_platform_provider_with_other_client_bindings_than_an_application_provider_is_a_conflict_for_the_platform()
    {
        var application = Application("Sales", Oidc("Issuer", "Client", "Secret"));
        var platform = Oidc("Issuer", "OtherClient", "OtherSecret", name: "platform");

        var result = RealmPlanner.Build([application], [platform]);

        Assert.All(result.Conflicts, conflict => Assert.Equal(RealmConflictKind.ClientBindings, conflict.Kind));
        var conflict = Assert.Single(result.Conflicts, conflict => conflict.ApplicationId == RealmApplication.PlatformSignInId);
        Assert.Equal("platform", conflict.Provider.Name);
        Assert.Equal("Sales", conflict.Other.Name);
        Assert.Contains(result.Conflicts, conflict => conflict.ApplicationId == application.ApplicationId && conflict.Other.ApplicationId == RealmApplication.PlatformSignInId);
    }

    [Fact]
    public void A_platform_provider_with_the_same_bindings_and_other_claims_is_a_claims_conflict_for_the_platform()
    {
        var application = Application("Sales", Oidc("Issuer", "Client", "Secret"));
        var platform = Oidc("Issuer", "Client", "Secret", name: "platform", claims: new IdentityProviderClaimsModel("fullName", "email", "locale"));

        var result = RealmPlanner.Build([application], [platform]);

        Assert.All(result.Conflicts, conflict => Assert.Equal(RealmConflictKind.Claims, conflict.Kind));
        Assert.Contains(result.Conflicts, conflict => conflict.ApplicationId == RealmApplication.PlatformSignInId);
    }

    [Fact]
    public void Two_platform_providers_with_the_same_issuer_binding_and_other_client_bindings_are_a_conflict()
    {
        var result = RealmPlanner.Build([], [Oidc("Issuer", "ClientA", "Secret", name: "a"), Oidc("Issuer", "ClientB", "Secret", name: "b")]);

        Assert.Equal(2, result.Conflicts.Count);
        Assert.All(result.Conflicts, conflict => Assert.Equal(RealmApplication.PlatformSignInId, conflict.Other.ApplicationId));
    }

    [Fact]
    public void An_oidc_provider_gets_its_label_in_each_platform_locale_from_the_texts_of_the_application()
    {
        var application = Application("Sales", Oidc("Issuer", "Client", "Secret") with { LabelTextKey = "provider.company" }) with
        {
            Texts = Texts(("en", "Company"), ("vi", "Công ty")),
        };

        var provider = Assert.Single(RealmPlanner.Build([application]).Plan.Providers);

        Assert.Equal(new Dictionary<string, string> { ["en"] = "Company", ["vi"] = "Công ty" }, provider.Labels);
    }

    [Fact]
    public void A_locale_whose_texts_lack_the_label_key_takes_the_text_of_the_fallback_locale()
    {
        var application = Application("Sales", Oidc("Issuer", "Client", "Secret") with { LabelTextKey = "provider.company" }) with
        {
            Texts = Texts(("en", "Company")),
            FallbackLocale = "en",
        };

        var provider = Assert.Single(RealmPlanner.Build([application]).Plan.Providers);

        Assert.Equal("Company", provider.Labels["vi"]);
    }

    [Fact]
    public void A_label_without_text_in_the_locale_or_the_fallback_locale_takes_the_first_locale_that_has_it_and_then_the_provider_name()
    {
        var provider = Oidc("Issuer", "Client", "Secret") with { LabelTextKey = "provider.company" };
        var withOtherLocale = Application("Sales", provider) with { Texts = Texts(("fr", "Société")), FallbackLocale = "en" };
        var withoutTexts = Application("Hr", Oidc("OtherIssuer", "Client", "Secret") with { LabelTextKey = "provider.company" });

        var plan = RealmPlanner.Build([withOtherLocale, withoutTexts]).Plan;

        Assert.Equal("Société", plan.Providers.Single(entry => entry.Alias == "oidc-issuer").Labels["en"]);
        Assert.Equal("company", plan.Providers.Single(entry => entry.Alias == "oidc-otherissuer").Labels["vi"]);
    }

    [Fact]
    public void A_provider_shared_by_two_applications_takes_its_label_from_the_application_with_the_lower_id()
    {
        var lower = Application("Sales", Oidc("Issuer", "Client", "Secret") with { LabelTextKey = "provider.company" }) with
        {
            ApplicationId = new Guid("00000000-0000-0000-0000-000000000001"),
            Texts = Texts(("en", "Sales company"), ("vi", "Công ty bán hàng")),
        };
        var higher = Application("Hr", Oidc("Issuer", "Client", "Secret") with { LabelTextKey = "provider.company" }) with
        {
            ApplicationId = new Guid("00000000-0000-0000-0000-000000000002"),
            Texts = Texts(("en", "Hr company"), ("vi", "Công ty nhân sự")),
        };

        foreach (var applications in new[] { new[] { lower, higher }, [higher, lower] })
        {
            var provider = Assert.Single(RealmPlanner.Build(applications).Plan.Providers);

            Assert.Equal("Sales company", provider.Labels["en"]);
            Assert.Equal("Công ty bán hàng", provider.Labels["vi"]);
        }
    }

    [Fact]
    public void A_provider_shared_with_the_platform_sign_in_takes_its_label_from_the_application()
    {
        var application = Application("Sales", Oidc("Issuer", "Client", "Secret") with { LabelTextKey = "provider.company" }) with
        {
            Texts = Texts(("en", "Company"), ("vi", "Công ty")),
        };

        var provider = Assert.Single(RealmPlanner.Build([application], [Oidc("ISSUER", "client", "secret", name: "platform")]).Plan.Providers);

        Assert.Equal(new Dictionary<string, string> { ["en"] = "Company", ["vi"] = "Công ty" }, provider.Labels);
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Texts(params (string Locale, string Label)[] labels) =>
        labels.ToDictionary(
            label => label.Locale,
            label => (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["provider.company"] = label.Label });

    private static RealmApplication Application(string name, params IdentityProviderModel[] providers) =>
        Application(name, [new RealmApplicationSite($"{name.ToLowerInvariant()}-site", null)], providers);

    private static RealmApplication Application(string name, RealmApplicationSite[] sites, params IdentityProviderModel[] providers) =>
        new(Guid.NewGuid(), name, sites, providers);

    private static IdentityProviderModel Provider(string name, string type) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            File = $"identity-providers/{name}.json",
            Type = type,
            Roles = new IdentityProviderRolesModel("roles", new Dictionary<string, string>()),
            Acr = [],
        };

    private static IdentityProviderModel Saml(
        string metadata, string name = "agency", string rolesClaim = "roles", IdentityProviderClaimsModel? claims = null) =>
        Provider(name, "saml") with
        {
            MetadataBinding = metadata,
            Claims = claims ?? _claims,
            Roles = new IdentityProviderRolesModel(rolesClaim, new Dictionary<string, string>()),
        };

    private static IdentityProviderModel Oidc(
        string issuer, string clientId, string clientSecret, string name = "company", string rolesClaim = "roles", IdentityProviderClaimsModel? claims = null) =>
        Provider(name, "oidc") with
        {
            IssuerBinding = issuer,
            ClientIdBinding = clientId,
            ClientSecretBinding = clientSecret,
            Claims = claims ?? _claims,
            Roles = new IdentityProviderRolesModel(rolesClaim, new Dictionary<string, string>()),
        };
}
