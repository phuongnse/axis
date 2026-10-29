using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;

namespace Axis.Tenancy.Tests.Realms;

public sealed class PlatformSignInProvidersTests
{
    [Fact]
    public void A_row_without_claims_and_roles_takes_the_defaults()
    {
        var provider = Assert.Single(PlatformSignInProviders.From([Row("Company")]));

        Assert.Equal("Company", provider.Name);
        Assert.Equal("", provider.File);
        Assert.Equal("oidc", provider.Type);
        Assert.Equal("Issuer", provider.IssuerBinding);
        Assert.Equal("Client", provider.ClientIdBinding);
        Assert.Equal("Secret", provider.ClientSecretBinding);
        Assert.Equal(new("name", "email", "locale"), provider.Claims);
        Assert.Equal("roles", provider.Roles.Claim);
        Assert.Empty(provider.Roles.Values);
        Assert.Empty(provider.Acr);
    }

    [Fact]
    public void A_row_with_claims_and_role_values_keeps_them()
    {
        var row = Row("Company");
        row.ClaimDisplayName = "fullName";
        row.ClaimEmail = "mail";
        row.ClaimLocale = "lang";
        row.RolesClaim = "groups";
        row.RoleValues = """{"admins":"administrator"}""";

        var provider = Assert.Single(PlatformSignInProviders.From([row]));

        Assert.Equal(new("fullName", "mail", "lang"), provider.Claims);
        Assert.Equal("groups", provider.Roles.Claim);
        Assert.Equal("administrator", provider.Roles.Values["admins"]);
    }

    [Fact]
    public void The_providers_are_ordered_by_name_and_have_unique_stable_ids()
    {
        var first = PlatformSignInProviders.From([Row("b"), Row("a")]);
        var second = PlatformSignInProviders.From([Row("a"), Row("b")]);

        Assert.Equal(["a", "b"], first.Select(provider => provider.Name));
        Assert.Equal(first.Select(provider => provider.Id), second.Select(provider => provider.Id));
        Assert.Equal(2, first.Select(provider => provider.Id).Distinct().Count());
        Assert.Equal(PlatformSignInProviders.From([Row("A")])[0].Id, PlatformSignInProviders.From([Row("a")])[0].Id);
    }

    private static TenantPlatformProviderRow Row(string name) =>
        new()
        {
            TenantId = "acme",
            Name = name,
            Type = "oidc",
            IssuerBinding = "Issuer",
            ClientIdBinding = "Client",
            ClientSecretBinding = "Secret",
        };
}
