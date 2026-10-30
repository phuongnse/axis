using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;

namespace Axis.Configuration.Tests;

public sealed class IdentityProviderCompilerTests
{
    private const string AccountsId = "5d0b8c1e-2f4a-4b6c-9d8e-7a1b2c3d4e51";
    private const string OidcId = "5d0b8c1e-2f4a-4b6c-9d8e-7a1b2c3d4e52";
    private const string SamlId = "5d0b8c1e-2f4a-4b6c-9d8e-7a1b2c3d4e53";
    private const string ConnectorId = "409bc638-0aec-4084-ada2-e3baed50057d";

    private const string Texts = """
        { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "role.finance": "Finance", "signIn.company": "Continue with Company", "signIn.agency": "Continue with the agency" } }
        """;

    private const string FinanceRole = """
        { "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "finance", "formatVersion": 1, "label": { "textKey": "role.finance" } }
        """;

    private const string OidcBody = """
        "type": "oidc", "label": { "textKey": "signIn.company" },
        "issuer": { "binding": "CompanyIssuer" },
        "clientId": { "binding": "CompanyClientId" },
        "clientSecret": { "binding": "CompanyClientSecret" }
        """;

    [Fact]
    public void A_provider_of_each_type_compiles_into_the_model_with_the_claim_defaults()
    {
        using var folder = Folder()
            .With("identity-providers/accounts.json", Provider(AccountsId, "accounts", """ "type": "accounts" """))
            .With("identity-providers/company.json", Provider(OidcId, "company", OidcBody))
            .With("identity-providers/agency.json", Provider(SamlId, "agency", """
                "type": "saml", "label": { "textKey": "signIn.agency" }, "metadata": { "binding": "AgencyMetadata" },
                "claims": { "email": "mail" },
                "roles": { "claim": "groups", "values": { "finance-team": "finance" } },
                "acr": ["urn:level:2"]
                """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Equal(["accounts", "agency", "company"], result.Model.IdentityProviders.Select(provider => provider.Name).Order().ToArray());

        var accounts = result.Model.IdentityProviders.Single(provider => provider.Name == "accounts");
        Assert.Equal(
            (Guid.Parse(AccountsId), "identity-providers/accounts.json", "accounts", null, null),
            (accounts.Id, accounts.File, accounts.Type, accounts.LabelTextKey, accounts.Claims));
        Assert.Equal("roles", accounts.Roles.Claim);
        Assert.Empty(accounts.Roles.Values);
        Assert.Empty(accounts.Acr);

        var oidc = result.Model.IdentityProviders.Single(provider => provider.Name == "company");
        Assert.Equal(
            ("oidc", "signIn.company", "CompanyIssuer", "CompanyClientId", "CompanyClientSecret", null, null),
            (oidc.Type, oidc.LabelTextKey, oidc.IssuerBinding, oidc.ClientIdBinding, oidc.ClientSecretBinding, oidc.ClientKeyBinding, oidc.MetadataBinding));
        Assert.Equal(new IdentityProviderClaimsModel("name", "email", "locale"), oidc.Claims);
        Assert.Equal("roles", oidc.Roles.Claim);
        Assert.Empty(oidc.Roles.Values);

        var saml = result.Model.IdentityProviders.Single(provider => provider.Name == "agency");
        Assert.Equal(
            ("saml", "signIn.agency", "AgencyMetadata", null),
            (saml.Type, saml.LabelTextKey, saml.MetadataBinding, saml.IssuerBinding));
        Assert.Equal(new IdentityProviderClaimsModel("name", "mail", "locale"), saml.Claims);
        Assert.Equal("groups", saml.Roles.Claim);
        Assert.Equal(new Dictionary<string, string> { ["finance-team"] = "finance" }, saml.Roles.Values);
        Assert.Equal(["urn:level:2"], saml.Acr);

        Assert.Contains(result.Resources, resource => resource.Path == "identity-providers/accounts.json");
        Assert.Contains(result.Resources, resource => resource.Path == "identity-providers/company.json");
        Assert.Contains(result.Resources, resource => resource.Path == "identity-providers/agency.json");
    }

    [Fact]
    public void An_oidc_provider_may_hold_a_client_key_instead_of_a_secret()
    {
        using var folder = Folder().With("identity-providers/company.json", Provider(OidcId, "company", """
            "type": "oidc", "label": { "textKey": "signIn.company" },
            "issuer": { "binding": "CompanyIssuer" }, "clientId": { "binding": "CompanyClientId" },
            "clientKey": { "binding": "CompanyClientKey" }
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var provider = Assert.Single(result.Model!.IdentityProviders);
        Assert.Equal((null, "CompanyClientKey"), (provider.ClientSecretBinding, provider.ClientKeyBinding));
    }

    [Fact]
    public void An_unknown_type_is_reported_at_its_type()
    {
        using var folder = Folder().With("identity-providers/company.json", Provider(OidcId, "company", """ "type": "ldap" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownIdentityProviderType, "identity-providers/company.json", "/type"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_binding_used_as_another_kind_is_reported_at_its_binding()
    {
        using var folder = Folder()
            .With("connectors/purchasing-system.json", $$"""
                { "id": "{{ConnectorId}}", "kind": "connector", "name": "PurchasingSystem", "formatVersion": 1,
                  "baseUrl": { "binding": "companyclientid" },
                  "operations": [{ "name": "ping", "method": "GET", "path": "/ping" }] }
                """)
            .With("identity-providers/company.json", Provider(OidcId, "company", OidcBody));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.IdentityProviderBindingKind, "identity-providers/company.json", "/clientId/binding"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("connectors/purchasing-system.json", diagnostic.Message);
    }

    [Fact]
    public void A_binding_used_as_the_token_url_of_an_oauth2_connector_is_a_url_binding_for_the_provider_check()
    {
        using var folder = Folder()
            .With("connectors/purchasing-system.json", $$"""
                { "id": "{{ConnectorId}}", "kind": "connector", "name": "PurchasingSystem", "formatVersion": 1,
                  "baseUrl": { "binding": "PurchasingSystemUrl" },
                  "auth": { "type": "oauth2", "tokenUrl": { "binding": "companyclientid" },
                            "clientId": { "binding": "PurchasingClientId" }, "clientSecret": { "binding": "PurchasingClientSecret" } },
                  "operations": [{ "name": "ping", "method": "GET", "path": "/ping" }] }
                """)
            .With("identity-providers/company.json", Provider(OidcId, "company", OidcBody));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.IdentityProviderBindingKind, "identity-providers/company.json", "/clientId/binding"),
                (DiagnosticCodes.ConnectorBindingKind, "connectors/purchasing-system.json", "/auth/tokenUrl/binding"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)).Order().ToArray());
    }

    [Fact]
    public void A_binding_shared_with_another_provider_as_another_kind_is_reported_at_each_use()
    {
        using var folder = Folder()
            .With("identity-providers/company.json", Provider(OidcId, "company", OidcBody))
            .With("identity-providers/agency.json", Provider(SamlId, "agency", """
                "type": "saml", "label": { "textKey": "signIn.agency" }, "metadata": { "binding": "CompanyClientId" }
                """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.IdentityProviderBindingKind, "identity-providers/agency.json", "/metadata/binding"),
                (DiagnosticCodes.IdentityProviderBindingKind, "identity-providers/company.json", "/clientId/binding"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)).Order().ToArray());
    }

    [Fact]
    public void A_binding_used_twice_as_the_same_kind_is_allowed()
    {
        using var folder = Folder()
            .With("identity-providers/company.json", Provider(OidcId, "company", OidcBody))
            .With("identity-providers/other.json", Provider(SamlId, "other", OidcBody.Replace("signIn.company", "signIn.agency")));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(""" "issuer": { "binding": "I" }, "clientSecret": { "binding": "S" } """, 1)]
    [InlineData(""" "clientId": { "binding": "C" }, "clientKey": { "binding": "K" } """, 1)]
    [InlineData(""" "issuer": { "binding": "I" }, "clientId": { "binding": "C" } """, 1)]
    [InlineData(""" "clientId": { "binding": "C" } """, 2)]
    public void An_oidc_provider_without_its_bindings_is_reported_at_its_type_once_for_each(string bindings, int count)
    {
        using var folder = Folder().With("identity-providers/company.json", Provider(
            OidcId, "company", $$"""
            "type": "oidc", "label": { "textKey": "signIn.company" }, {{bindings}}
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(count, result.Diagnostics.Count);
        Assert.All(
            result.Diagnostics,
            diagnostic => Assert.Equal((DiagnosticCodes.MissingIdentityProviderBinding, "identity-providers/company.json", "/type"), (diagnostic.Code, diagnostic.File, diagnostic.Path)));
    }

    [Fact]
    public void A_saml_provider_without_metadata_is_reported_at_its_type()
    {
        using var folder = Folder().With("identity-providers/agency.json", Provider(SamlId, "agency", """
            "type": "saml", "label": { "textKey": "signIn.agency" }
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingIdentityProviderBinding, "identity-providers/agency.json", "/type"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("metadata", diagnostic.Message);
    }

    [Fact]
    public void A_role_that_the_application_does_not_declare_is_reported_at_its_claim_value()
    {
        using var folder = Folder().With("identity-providers/company.json", Provider(OidcId, "company", $$"""
            {{OidcBody}},
            "roles": { "claim": "groups", "values": { "finance": "finance", "ops/team": "operator" } }
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownIdentityProviderRole, "identity-providers/company.json", "/roles/values/ops~1team"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("operator", diagnostic.Message);
    }

    [Fact]
    public void A_second_accounts_provider_is_reported_at_its_type()
    {
        using var folder = Folder()
            .With("identity-providers/accounts.json", Provider(AccountsId, "accounts", """ "type": "accounts" """))
            .With("identity-providers/more.json", Provider(SamlId, "more", """ "type": "accounts" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.SecondAccountsProvider, "identity-providers/more.json", "/type"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("identity-providers/accounts.json", diagnostic.Message);
    }

    [Fact]
    public void A_label_text_key_that_no_locale_has_is_reported()
    {
        using var folder = Folder().With("identity-providers/company.json", Provider(OidcId, "company", OidcBody.Replace("signIn.company", "signIn.nope")));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingTextKey, "identity-providers/company.json", "/label/textKey"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Theory]
    [InlineData(""" "type": "accounts", "label": { "textKey": "signIn.company" } """)]
    [InlineData(""" "type": "accounts", "issuer": { "binding": "I" } """)]
    [InlineData(""" "type": "accounts", "claims": { "email": "mail" } """)]
    [InlineData(""" "type": "oidc", "issuer": { "binding": "I" }, "clientId": { "binding": "C" }, "clientSecret": { "binding": "S" } """)]
    [InlineData(""" "type": "oidc", "label": { "textKey": "signIn.company" }, "metadata": { "binding": "M" } """)]
    [InlineData(""" "type": "oidc", "label": { "textKey": "signIn.company" }, "issuer": { "binding": "I" }, "clientId": { "binding": "C" }, "clientSecret": { "binding": "S" }, "clientKey": { "binding": "K" } """)]
    [InlineData(""" "type": "saml", "label": { "textKey": "signIn.agency" }, "issuer": { "binding": "I" } """)]
    [InlineData(""" "type": "saml", "metadata": { "binding": "M" } """)]
    [InlineData(""" "type": "saml", "label": { "textKey": "signIn.agency" }, "metadata": "https://example.test" """)]
    [InlineData(""" "type": "accounts", "acr": [] """)]
    [InlineData(""" "type": "accounts", "roles": { "values": { "x": "Finance" } } """)]
    [InlineData(""" "type": "accounts", "claims": { "email": "" } """)]
    public void A_provider_in_another_shape_is_a_schema_violation(string body)
    {
        using var folder = Folder().With("identity-providers/company.json", Provider(OidcId, "company", body));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, "identity-providers/company.json"), (diagnostic.Code, diagnostic.File)));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_roles_and_acr_setting_is_allowed_on_accounts()
    {
        using var folder = Folder().With("identity-providers/accounts.json", Provider(AccountsId, "accounts", """
            "type": "accounts", "roles": { "values": { "managers": "finance" } }, "acr": ["urn:level:2"]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var provider = Assert.Single(result.Model!.IdentityProviders);
        Assert.Equal(("roles", "finance"), (provider.Roles.Claim, provider.Roles.Values["managers"]));
    }

    private static string Provider(string id, string name, string body) => $$"""
        { "id": "{{id}}", "kind": "identityProvider", "name": "{{name}}", "formatVersion": 1, {{body}} }
        """;

    private static TemporaryFolder Folder() =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", Texts)
            .With("roles/finance.json", FinanceRole);
}
