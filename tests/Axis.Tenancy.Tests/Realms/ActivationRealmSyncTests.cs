using Axis.Configuration.Model;
using Axis.Configuration.Realms;
using Axis.Tenancy.Realms;

namespace Axis.Tenancy.Tests.Realms;

public sealed class ActivationRealmSyncTests
{
    private const string ClientId = "client-1d-v4lue";
    private const string ClientSecret = "client-s3cret-v4lue";

    [Fact]
    public void A_plan_with_two_sites_and_one_oidc_provider_maps_to_the_resolved_state()
    {
        var plan = new RealmPlan(
            [new RealmSiteClient("purchasing", Accounts: false, ["oidc-partner"]), new RealmSiteClient("requests", Accounts: true, [])],
            [Provider(clientSecretBinding: "PartnerSecret")]);

        var state = ActivationRealmSync.ToState(plan, Bindings());

        Assert.Equal(plan.Sites.Count, state.Sites.Count);
        foreach (var (expected, actual) in plan.Sites.Zip(state.Sites))
        {
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(expected.Accounts, actual.Accounts);
            Assert.True(expected.Providers.SequenceEqual(actual.Providers));
        }

        var provider = Assert.Single(state.Providers);
        Assert.Equal("oidc-partner", provider.Alias);
        Assert.Equal(new Uri("https://partner.example.test/realms/p"), provider.Issuer);
        Assert.Equal(ClientId, provider.ClientId);
        Assert.Equal(ClientSecret, provider.ClientSecret);
        Assert.Equal(("display", "mail", "lang"), (provider.DisplayNameClaim, provider.EmailClaim, provider.LocaleClaim));
        Assert.Equal(["groups", "roles"], provider.RolesClaims);
    }

    [Fact]
    public void A_provider_without_a_clientSecret_binding_fails_and_names_its_alias()
    {
        var plan = new RealmPlan([], [Provider(clientSecretBinding: null)]);

        var exception = Assert.Throws<InvalidOperationException>(() => ActivationRealmSync.ToState(plan, Bindings()));

        Assert.Contains("'oidc-partner'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("clientSecret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_binding_fails_and_names_the_alias_and_the_binding()
    {
        var plan = new RealmPlan([], [Provider(clientSecretBinding: "Unknown")]);

        var exception = Assert.Throws<InvalidOperationException>(() => ActivationRealmSync.ToState(plan, Bindings()));

        Assert.Contains("'oidc-partner'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'Unknown'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_binding_of_the_wrong_kind_fails_and_never_shows_a_value()
    {
        var plan = new RealmPlan([], [Provider(clientSecretBinding: "PartnerIssuer")]);

        var exception = Assert.Throws<InvalidOperationException>(() => ActivationRealmSync.ToState(plan, Bindings()));

        Assert.Contains("'oidc-partner'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'PartnerIssuer'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("partner.example.test", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tenant_without_realm_details_sends_no_request()
    {
        using var http = new HttpClient(new ThrowingHandler());
        var plan = new RealmPlan([], [Provider(clientSecretBinding: "PartnerSecret")]);

        await ActivationRealmSync.SyncAsync(http, realm: null, Bindings(), plan, TestContext.Current.CancellationToken);
    }

    private static RealmProvider Provider(string? clientSecretBinding) =>
        new(
            "oidc-partner",
            "PartnerIssuer",
            "PartnerClientId",
            clientSecretBinding,
            ClientKeyBinding: null,
            new IdentityProviderClaimsModel("display", "mail", "lang"),
            ["groups", "roles"],
            new Dictionary<string, string> { ["en"] = "Partner", ["vi"] = "Đối tác" });

    private static Dictionary<string, TenantBinding> Bindings() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["PartnerIssuer"] = new(DeploymentBindingKinds.Url, "https://partner.example.test/realms/p", null),
            ["partnerclientid"] = new(DeploymentBindingKinds.Secret, null, new SecretValue(ClientId)),
            ["PartnerSecret"] = new(DeploymentBindingKinds.Secret, null, new SecretValue(ClientSecret)),
        };

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No request is expected.");
    }
}
