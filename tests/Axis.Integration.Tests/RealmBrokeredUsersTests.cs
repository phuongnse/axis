using System.Net;
using Axis.Admin;
using Axis.Tenancy.Realms;

namespace Axis.Integration.Tests;

/// <summary>
/// A tenant realm that <see cref="TenantRealm"/> made and <see cref="RealmSync"/> synced never links two brokered users by
/// email. A person who signs in through two providers that give the same email becomes two realm users, and each
/// username is the provider's alias, a dot and the person's subject at that provider.
/// </summary>
public sealed class RealmBrokeredUsersTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    private const string RedirectUri = "http://localhost/callback";
    private const string TenantClientId = "browser";

    private static readonly TenantRealmSecrets _secrets = new("platform-secret-1", "activation-secret-1", "users-secret-1");

    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private Uri _baseAddress = null!;
    private UpstreamRealms _realms = null!;

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _realms = await UpstreamRealms.CreateAsync(_baseAddress);
    }

    public async ValueTask DisposeAsync()
    {
        await _realms.DeleteRealmsAsync();
        _realms.Dispose();
        _http.Dispose();
    }

    [Fact]
    public async Task One_person_who_signs_in_through_two_providers_with_the_same_email_becomes_two_users_named_after_their_provider()
    {
        var tenant = _realms.NewRealmName();
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(tenant, "Acme", _secrets, new Dictionary<string, string>(), TestContext.Current.CancellationToken);
        var upstreamA = await _realms.NewUpstreamRealmAsync();
        var upstreamB = await _realms.NewUpstreamRealmAsync();
        var subjectA = await _realms.AddUpstreamUserAsync(upstreamA);
        var subjectB = await _realms.AddUpstreamUserAsync(upstreamB);

        var state = new RealmState([], [Provider("oidc-a", upstreamA), Provider("oidc-b", upstreamB)]);
        await new RealmSync(_http, _baseAddress).SyncAsync(tenant, _secrets.Activation, state, TestContext.Current.CancellationToken);
        await _realms.PointBackChannelAtTheContainerAsync(tenant, "oidc-a", upstreamA);
        await _realms.PointBackChannelAtTheContainerAsync(tenant, "oidc-b", upstreamB);
        await AddTenantClientAsync(tenant);

        await SignInThroughAsync(tenant, "oidc-a", upstreamA);
        await SignInThroughAsync(tenant, "oidc-b", upstreamB);

        var users = await _realms.GetJsonAsync($"admin/realms/{tenant}/users?email={Uri.EscapeDataString(UpstreamRealms.Email)}&exact=true");
        Assert.Equal(2, users.GetArrayLength());
        var byUsername = users.EnumerateArray().ToDictionary(user => user.GetProperty("username").GetString()!, user => user.GetProperty("id").GetString()!);
        Assert.Equal(
            [$"oidc-a.{subjectA}".ToLowerInvariant(), $"oidc-b.{subjectB}".ToLowerInvariant()],
            byUsername.Keys.Order(StringComparer.Ordinal).ToArray());
        foreach (var (username, id) in byUsername)
        {
            var identities = await _realms.GetJsonAsync($"admin/realms/{tenant}/users/{id}/federated-identity");
            var identity = Assert.Single(identities.EnumerateArray());
            Assert.Equal(username[..username.IndexOf('.', StringComparison.Ordinal)], identity.GetProperty("identityProvider").GetString());
        }
    }

    private RealmBrokeredProvider Provider(string alias, string upstream) =>
        new(alias, new Uri(_baseAddress, $"realms/{upstream}"), UpstreamRealms.ClientId, UpstreamRealms.BrokerSecret, "name", "email", "locale", []);

    private async Task AddTenantClientAsync(string tenant)
    {
        using var response = await _realms.SendAsync(HttpMethod.Post, $"admin/realms/{tenant}/clients", new
        {
            clientId = TenantClientId,
            publicClient = true,
            standardFlowEnabled = true,
            redirectUris = new[] { RedirectUri },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// Plays a browser that starts at the tenant realm's login with a hint for the provider, logs in at the upstream realm
    /// and follows every redirect until Keycloak sends it to the client's callback.
    /// </summary>
    private async Task SignInThroughAsync(string tenant, string alias, string upstream)
    {
        var authorize = new Uri(_baseAddress,
            $"realms/{tenant}/protocol/openid-connect/auth?client_id={TenantClientId}&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
            + $"&response_type=code&scope=openid&kc_idp_hint={alias}");

        await _realms.LogInAsync(authorize, upstream, RedirectUri);
    }
}
