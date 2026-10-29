using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Server.Authentication;
using Axis.Server.Users;

namespace Axis.Server.Tests;

/// <summary>
/// The roles that each application's identity providers give one sign-in through their <c>roles</c> mapping, and the roles
/// that the policies of one application see.
/// </summary>
public sealed class ApplicationRolesTests
{
    private const string Alias = "oidc-companyissuer";

    [Fact]
    public void A_claim_value_gives_the_role_that_values_map_it_to()
    {
        var application = Application("buyer", Provider("company", "CompanyIssuer", "groups", ("purchasing-managers", "buyer")));

        var roles = ApplicationRoles.Resolve([application], Alias, Claims(("axis_roles_groups", "purchasing-managers")));

        Assert.Equal(["buyer"], roles[application.Manifest.Id]);
    }

    [Fact]
    public void Without_values_a_claim_value_gives_the_declared_role_of_the_same_name()
    {
        var application = Application("buyer", Provider("company", "CompanyIssuer", "groups"));

        var roles = ApplicationRoles.Resolve([application], Alias, Claims(("axis_roles_groups", "buyer")));

        Assert.Equal(["buyer"], roles[application.Manifest.Id]);
    }

    [Fact]
    public void Without_values_an_undeclared_claim_value_gives_no_role()
    {
        var application = Application("buyer", Provider("company", "CompanyIssuer", "groups"));

        var roles = ApplicationRoles.Resolve([application], Alias, Claims(("axis_roles_groups", "approver")));

        Assert.Empty(roles);
    }

    [Fact]
    public void A_value_that_values_do_not_list_is_ignored_even_when_a_role_has_its_name()
    {
        var application = Application("buyer", Provider("company", "CompanyIssuer", "groups", ("purchasing-managers", "buyer")));

        var roles = ApplicationRoles.Resolve([application], Alias, Claims(("axis_roles_groups", "buyer")));

        Assert.Empty(roles);
    }

    [Fact]
    public void The_provider_reads_the_claim_that_roles_name()
    {
        var application = Application("buyer", Provider("company", "CompanyIssuer", "teams"));

        var roles = ApplicationRoles.Resolve([application], Alias, Claims(("axis_roles_groups", "buyer")));

        Assert.Empty(roles);
    }

    [Fact]
    public void Two_applications_map_one_claim_value_differently()
    {
        var first = Application("buyer", Provider("company", "CompanyIssuer", "groups", ("staff", "buyer")));
        var second = Application("clerk", Provider("company", "CompanyIssuer", "groups", ("staff", "clerk")));
        var third = Application("buyer", Provider("company", "CompanyIssuer", "groups"));

        var roles = ApplicationRoles.Resolve([first, second, third], Alias, Claims(("axis_roles_groups", "staff")));

        Assert.Equal(["buyer"], roles[first.Manifest.Id]);
        Assert.Equal(["clerk"], roles[second.Manifest.Id]);
        Assert.False(roles.ContainsKey(third.Manifest.Id));
    }

    [Fact]
    public void A_value_that_maps_to_an_undeclared_role_gives_no_role()
    {
        var application = Application("buyer", Provider("company", "CompanyIssuer", "groups", ("staff", "owner")));

        Assert.Empty(ApplicationRoles.Resolve([application], Alias, Claims(("axis_roles_groups", "staff"))));
    }

    [Fact]
    public void Two_values_that_map_to_one_role_give_it_once()
    {
        var application = Application("buyer", Provider("company", "CompanyIssuer", "groups", ("a", "buyer"), ("b", "buyer")));

        var roles = ApplicationRoles.Resolve([application], Alias, Claims(("axis_roles_groups", "a"), ("axis_roles_groups", "b")));

        Assert.Equal(["buyer"], roles[application.Manifest.Id]);
    }

    [Fact]
    public void Providers_with_the_same_alias_give_the_union_of_their_roles()
    {
        var application = Application(
            ["buyer", "clerk"],
            Provider("first", "CompanyIssuer", "groups", ("staff", "buyer")),
            Provider("second", "companyissuer", "groups", ("staff", "clerk")));

        var roles = ApplicationRoles.Resolve([application], Alias, Claims(("axis_roles_groups", "staff")));

        Assert.Equal(["buyer", "clerk"], roles[application.Manifest.Id]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void An_account_sign_in_gives_no_mapped_role(string? identityProvider)
    {
        var application = Application("buyer", Provider("company", "CompanyIssuer", "groups"));

        Assert.Empty(ApplicationRoles.Resolve([application], identityProvider, Claims(("axis_roles_groups", "buyer"))));
    }

    [Theory]
    [InlineData("oidc-other")]
    [InlineData("OIDC-COMPANYISSUER")]
    [InlineData("accounts")]
    public void An_alias_that_no_provider_has_gives_no_mapped_role(string identityProvider)
    {
        var application = Application("buyer", Provider("company", "CompanyIssuer", "groups"));

        Assert.Empty(ApplicationRoles.Resolve([application], identityProvider, Claims(("axis_roles_groups", "buyer"))));
    }

    [Fact]
    public void A_provider_that_is_not_oidc_never_matches()
    {
        var application = Application("buyer", Provider("company", "CompanyIssuer", "groups", "saml"), Provider("accounts", null, "groups", "accounts"));

        Assert.Empty(ApplicationRoles.Resolve([application], Alias, Claims(("axis_roles_groups", "buyer"))));
    }

    [Fact]
    public void The_policies_of_an_application_see_the_tenant_wide_roles_and_the_roles_mapped_for_it()
    {
        var first = Application("buyer", Provider("company", "CompanyIssuer", "groups"));
        var second = Application("buyer", Provider("company", "CompanyIssuer", "groups"));
        var user = new CurrentUserResponse("u1", "User", ["tenant-role", "buyer"])
        {
            ApplicationRoles = new Dictionary<Guid, IReadOnlyList<string>>
            {
                [first.Manifest.Id] = ["buyer", "clerk"],
            },
        };

        Assert.Equal(["tenant-role", "buyer", "clerk"], user.RolesIn(first));
        Assert.Equal(["tenant-role", "buyer"], user.RolesIn(second));
        Assert.Equal(["tenant-role", "buyer"], user.Roles);
    }

    [Fact]
    public void A_role_mapped_for_one_application_is_no_role_in_another()
    {
        var first = Application("buyer", Provider("company", "CompanyIssuer", "groups"));
        var second = Application("buyer", Provider("company", "CompanyIssuer", "groups"));
        var user = new CurrentUserResponse("u1", "User", [])
        {
            ApplicationRoles = new Dictionary<Guid, IReadOnlyList<string>> { [first.Manifest.Id] = ["buyer"] },
        };

        Assert.Equal(["buyer"], user.RolesIn(first));
        Assert.Empty(user.RolesIn(second));
    }

    [Fact]
    public void The_session_principal_keeps_the_roles_of_each_application()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var principal = Session.CreatePrincipal(
            "a",
            "u1",
            "User",
            ["tenant-role"],
            applicationRoles: new Dictionary<Guid, IReadOnlyList<string>>
            {
                [first] = ["buyer", "clerk"],
                [second] = ["buyer"],
            });

        var user = Session.Read(principal)!;

        Assert.Equal(["tenant-role"], user.Roles);
        Assert.Equal(["buyer", "clerk"], user.ApplicationRoles[first]);
        Assert.Equal(["buyer"], user.ApplicationRoles[second]);
    }

    private static Func<string, IEnumerable<string>> Claims(params (string Name, string Value)[] claims) =>
        name => claims.Where(claim => claim.Name == name).Select(claim => claim.Value);

    private static IdentityProviderModel Provider(
        string name,
        string? issuer,
        string claim,
        params (string Value, string Role)[] values) => Provider(name, issuer, claim, "oidc", values);

    private static IdentityProviderModel Provider(
        string name,
        string? issuer,
        string claim,
        string type,
        params (string Value, string Role)[] values) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            File = $"identity-providers/{name}.json",
            Type = type,
            IssuerBinding = issuer,
            Roles = new IdentityProviderRolesModel(claim, values.ToDictionary(pair => pair.Value, pair => pair.Role)),
            Acr = [],
        };

    private static ApplicationModel Application(string role, params IdentityProviderModel[] providers) => Application([role], providers);

    private static ApplicationModel Application(string[] roles, params IdentityProviderModel[] providers) => new()
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
        Roles = [.. roles.Select(role => new RoleResource
        {
            Id = Guid.NewGuid(),
            Kind = ResourceKinds.Role,
            Name = role,
            FormatVersion = 1,
            File = $"roles/{role}.json",
        })],
        IdentityProviders = providers,
    };
}
