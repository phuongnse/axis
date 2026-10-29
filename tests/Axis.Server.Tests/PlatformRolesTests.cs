using Axis.Server.Authentication;
using Axis.Server.Studio;
using Axis.Server.Users;
using Axis.Tenancy;

namespace Axis.Server.Tests;

/// <summary>
/// What a tenant's platform sign-in accepts, and the platform roles that a provider's roles claim values give. A
/// result of <c>null</c> is a refused sign-in.
/// </summary>
public sealed class PlatformRolesTests
{
    private static readonly StudioOptions _studio = new([], "studio-author", "studio-publisher");
    private static readonly ConsoleSettings _console = new("console-operator");
    private static readonly UsersSettings _users = new("users-administrator");

    private static readonly PlatformSignInProvider _company = new(
        "company",
        "oidc-companyissuer",
        "groups",
        new Dictionary<string, string>
        {
            ["axis-authors"] = "author",
            ["axis-publishers"] = "publisher",
            ["axis-operators"] = "operator",
            ["axis-admins"] = "administrator",
            ["axis-unknown"] = "owner",
        });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_account_sign_in_is_accepted_with_accounts_on_and_maps_no_role(string? identityProvider)
    {
        var roles = Resolve(new PlatformSignIn(true, [_company]), identityProvider, "axis-authors");

        Assert.NotNull(roles);
        Assert.Empty(roles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_account_sign_in_is_refused_with_accounts_off(string? identityProvider)
    {
        Assert.Null(Resolve(new PlatformSignIn(false, [_company]), identityProvider, "axis-authors"));
    }

    [Theory]
    [InlineData("oidc-other")]
    [InlineData("oidc-companyissuer-2")]
    [InlineData("oidc-CompanyIssuer")]
    [InlineData("company")]
    public void A_provider_that_the_platform_sign_in_does_not_accept_is_refused(string identityProvider)
    {
        Assert.Null(Resolve(new PlatformSignIn(true, [_company]), identityProvider, "axis-authors"));
    }

    [Fact]
    public void A_provider_without_an_alias_never_matches()
    {
        var saml = new PlatformSignInProvider("federation", null, null, new Dictionary<string, string>());

        Assert.Null(Resolve(new PlatformSignIn(true, [saml]), "oidc-federation", []));
    }

    [Fact]
    public void A_mapped_value_gives_the_host_role_name()
    {
        var roles = Resolve(new PlatformSignIn(false, [_company]), "oidc-companyissuer", "axis-authors");

        Assert.Equal(["studio-author"], roles);
    }

    [Fact]
    public void A_value_that_is_not_mapped_gives_no_role()
    {
        var roles = Resolve(new PlatformSignIn(false, [_company]), "oidc-companyissuer", "staff", "axis-unknown");

        Assert.NotNull(roles);
        Assert.Empty(roles);
    }

    [Fact]
    public void Each_of_the_four_platform_roles_becomes_the_role_name_of_its_host_setting()
    {
        var roles = Resolve(
            new PlatformSignIn(false, [_company]),
            "oidc-companyissuer",
            "axis-admins",
            "axis-operators",
            "axis-publishers",
            "axis-authors",
            "axis-authors");

        Assert.Equal(["users-administrator", "console-operator", "studio-publisher", "studio-author"], roles);
    }

    [Fact]
    public void A_host_role_name_that_is_unset_is_skipped()
    {
        var roles = Resolve(
            new PlatformSignIn(false, [_company]),
            "oidc-companyissuer",
            _studio with { AuthorRole = null, PublisherRole = null },
            "axis-authors",
            "axis-publishers",
            "axis-operators");

        Assert.Equal(["console-operator"], roles);
    }

    [Fact]
    public void Only_the_values_of_the_providers_own_roles_claim_count()
    {
        var read = new List<string>();
        var roles = PlatformRoles.Resolve(
            new PlatformSignIn(false, [_company]),
            "oidc-companyissuer",
            name =>
            {
                read.Add(name);
                return name == "axis_roles_groups" ? ["axis-authors"] : ["axis-admins"];
            },
            _studio,
            _console,
            _users);

        Assert.Equal(["axis_roles_groups"], read);
        Assert.Equal(["studio-author"], roles);
    }

    [Fact]
    public void A_provider_without_a_roles_claim_maps_no_role()
    {
        var provider = _company with { RolesClaim = null };

        var roles = Resolve(new PlatformSignIn(false, [provider]), "oidc-companyissuer", "axis-authors");

        Assert.NotNull(roles);
        Assert.Empty(roles);
    }

    private static IReadOnlyList<string>? Resolve(PlatformSignIn signIn, string? identityProvider, params string[] values) =>
        Resolve(signIn, identityProvider, _studio, values);

    private static IReadOnlyList<string>? Resolve(PlatformSignIn signIn, string? identityProvider, StudioOptions studio, params string[] values) =>
        PlatformRoles.Resolve(signIn, identityProvider, _ => values, studio, _console, _users);
}
