using System.Net;
using Axis.Admin;
using Axis.Configuration.Resources;
using Axis.Tenancy;
using Axis.Tenancy.Realms;

namespace Axis.Integration.Tests;

/// <summary>
/// A tenant realm that <see cref="TenantRealm"/> made and <see cref="RealmSync"/> synced refuses a first brokered sign-in while
/// <see cref="RealmUsersQuota"/> has switched the deny step of the first-login flow on, and creates the user while it is off.
/// A person who already has a user still signs in.
/// </summary>
public sealed class RealmUsersQuotaTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    private const string RedirectUri = "http://localhost/callback";
    private const string TenantClientId = "browser";
    private const string RefusalText = "Your organization has reached its number of users.";

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
    public async Task Below_the_quota_the_step_stays_off_and_a_first_brokered_sign_in_creates_a_user()
    {
        var (tenant, access, users, upstreamA, _) = await NewTenantAsync();

        Assert.False(await new RealmUsersQuota(_http).RefreshAsync(access, users, 5, TestContext.Current.CancellationToken));
        var before = await UserCountAsync(tenant);
        var (callback, html) = await SignInAsync(tenant, "oidc-a", upstreamA);

        Assert.True(callback is not null, html);
        Assert.Equal(before + 1, await UserCountAsync(tenant));
    }

    [Fact]
    public async Task At_the_quota_a_first_brokered_sign_in_is_refused_and_a_user_who_exists_still_signs_in()
    {
        var (tenant, access, users, upstreamA, upstreamB) = await NewTenantAsync();
        var quota = new RealmUsersQuota(_http);
        var (first, firstHtml) = await SignInAsync(tenant, "oidc-a", upstreamA);
        Assert.True(first is not null, firstHtml);

        Assert.True(await quota.RefreshAsync(access, users, 1, TestContext.Current.CancellationToken));
        var before = await UserCountAsync(tenant);
        var (refused, refusedHtml) = await SignInAsync(tenant, "oidc-b", upstreamB);

        Assert.Null(refused);
        Assert.Contains(RefusalText, WebUtility.HtmlDecode(refusedHtml), StringComparison.Ordinal);
        Assert.Equal(before, await UserCountAsync(tenant));
        var (again, againHtml) = await SignInAsync(tenant, "oidc-a", upstreamA);
        Assert.True(again is not null, againHtml);
        Assert.Equal(before, await UserCountAsync(tenant));

        Assert.False(await quota.RefreshAsync(access, users, null, TestContext.Current.CancellationToken));
        var deny = (await _realms.GetJsonAsync(
            $"admin/realms/{tenant}/authentication/flows/{Uri.EscapeDataString(RealmSync.FirstLoginFlowAlias)}/executions")).EnumerateArray().First();
        Assert.Equal(RealmUsersQuota.DenyAccessProvider, deny.GetProperty("providerId").GetString());
        Assert.Equal("DISABLED", deny.GetProperty("requirement").GetString());
    }

    private async Task<(string Tenant, TenantRealmAccess Access, TenantRealmUsers Users, string UpstreamA, string UpstreamB)> NewTenantAsync()
    {
        var tenant = _realms.NewRealmName();
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(tenant, "Acme", _secrets, new Dictionary<string, string>(), TestContext.Current.CancellationToken);
        var upstreamA = await _realms.NewUpstreamRealmAsync();
        var upstreamB = await _realms.NewUpstreamRealmAsync();
        await _realms.AddUpstreamUserAsync(upstreamA);
        await _realms.AddUpstreamUserAsync(upstreamB);

        var state = new RealmState([], [Provider("oidc-a", upstreamA), Provider("oidc-b", upstreamB)]);
        await new RealmSync(_http, _baseAddress).SyncAsync(tenant, _secrets.Activation, state, TestContext.Current.CancellationToken);
        await _realms.PointBackChannelAtTheContainerAsync(tenant, "oidc-a", upstreamA);
        await _realms.PointBackChannelAtTheContainerAsync(tenant, "oidc-b", upstreamB);
        using var response = await _realms.SendAsync(HttpMethod.Post, $"admin/realms/{tenant}/clients", new
        {
            clientId = TenantClientId,
            publicClient = true,
            standardFlowEnabled = true,
            redirectUris = new[] { RedirectUri },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (
            tenant,
            new TenantRealmAccess(_baseAddress, tenant, new SecretValue(_secrets.Activation)),
            new TenantRealmUsers(_baseAddress, tenant, new SecretValue(_secrets.Users)),
            upstreamA,
            upstreamB);
    }

    private RealmBrokeredProvider Provider(string alias, string upstream) =>
        new(alias, new Uri(_baseAddress, $"realms/{upstream}"), UpstreamRealms.ClientId, UpstreamRealms.BrokerSecret, "name", "email", "locale", []);

    private async Task<int> UserCountAsync(string tenant)
    {
        using var response = await _realms.SendAsync(HttpMethod.Get, $"admin/realms/{tenant}/users/count?search=*");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return int.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private Task<(Uri? Callback, string Html)> SignInAsync(string tenant, string alias, string upstream) =>
        _realms.SignInAsync(
            new Uri(_baseAddress,
                $"realms/{tenant}/protocol/openid-connect/auth?client_id={TenantClientId}&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
                + $"&response_type=code&scope=openid&kc_idp_hint={alias}{Pkce.New().Query}"),
            upstream,
            RedirectUri);
}
