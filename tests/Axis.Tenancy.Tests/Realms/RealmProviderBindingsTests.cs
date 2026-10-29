using Axis.Configuration.Model;
using Axis.Tenancy.Realms;

namespace Axis.Tenancy.Tests.Realms;

public sealed class RealmProviderBindingsTests
{
    [Fact]
    public void A_provider_whose_bindings_are_all_set_misses_none_ignoring_letter_case()
    {
        Assert.Null(RealmProviderBindings.Missing(Provider("issuer", "CLIENT", "secret"), Bindings()));
    }

    [Fact]
    public void A_binding_the_tenant_lacks_is_named()
    {
        Assert.Equal("Other", RealmProviderBindings.Missing(Provider("Issuer", "Client", "Other"), Bindings()));
        Assert.Equal("Absent", RealmProviderBindings.Missing(Provider("Absent", "Client", "Secret"), Bindings()));
    }

    [Fact]
    public void A_binding_of_the_wrong_kind_or_without_a_value_is_named()
    {
        Assert.Equal("Client", RealmProviderBindings.Missing(Provider("Issuer", "Client", "Secret"), new Dictionary<string, TenantBinding>(StringComparer.OrdinalIgnoreCase)
        {
            ["Issuer"] = new("url", "https://idp.example/", null),
            ["Client"] = new("url", "https://x.example/", null),
            ["Secret"] = new("secret", null, new SecretValue("s")),
        }));
        Assert.Equal("Issuer", RealmProviderBindings.Missing(Provider("Issuer", "Client", "Secret"), new Dictionary<string, TenantBinding>(StringComparer.OrdinalIgnoreCase)
        {
            ["Issuer"] = new("url", " ", null),
            ["Client"] = new("secret", null, new SecretValue("c")),
            ["Secret"] = new("secret", null, new SecretValue("s")),
        }));
    }

    [Fact]
    public void A_provider_that_is_not_oidc_misses_none()
    {
        Assert.Null(RealmProviderBindings.Missing(Provider("Absent", "Absent", "Absent") with { Type = "saml" }, Bindings()));
    }

    private static Dictionary<string, TenantBinding> Bindings() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Issuer"] = new("url", "https://idp.example/", null),
            ["Client"] = new("secret", null, new SecretValue("c")),
            ["Secret"] = new("secret", null, new SecretValue("s")),
        };

    private static IdentityProviderModel Provider(string issuer, string clientId, string clientSecret) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "company",
            File = "",
            Type = "oidc",
            IssuerBinding = issuer,
            ClientIdBinding = clientId,
            ClientSecretBinding = clientSecret,
            Roles = new IdentityProviderRolesModel("roles", new Dictionary<string, string>()),
            Acr = [],
        };
}
