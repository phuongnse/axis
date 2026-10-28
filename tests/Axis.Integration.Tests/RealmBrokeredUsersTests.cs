using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Admin;
using Axis.Tenancy.Realms;

namespace Axis.Integration.Tests;

/// <summary>
/// A tenant realm that <see cref="TenantRealm"/> made and <see cref="RealmSync"/> synced never links two brokered users by
/// email. A person who signs in through two providers that give the same email becomes two realm users, and each
/// username is the provider's alias, a dot and the person's subject at that provider.
/// </summary>
public sealed partial class RealmBrokeredUsersTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    private const string Email = "pat@example.test";
    private const string UpstreamPassword = "upstream-password-1";
    private const string BrokerSecret = "broker-secret-1";
    private const string RedirectUri = "http://localhost/callback";
    private const string TenantClientId = "browser";

    private static readonly TenantRealmSecrets _secrets = new("platform-secret-1", "activation-secret-1", "users-secret-1");

    private static readonly string[] _anyRedirect = ["*"];

    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private readonly List<string> _realms = [];
    private Uri _baseAddress = null!;
    private string _token = null!;

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _token = await MasterTokenAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var realm in _realms)
        {
            using var response = await SendAsync(HttpMethod.Delete, $"admin/realms/{realm}");
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _http.Dispose();
    }

    [Fact]
    public async Task One_person_who_signs_in_through_two_providers_with_the_same_email_becomes_two_users_named_after_their_provider()
    {
        var tenant = NewRealm();
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(tenant, "Acme", _secrets, TestContext.Current.CancellationToken);
        var upstreamA = await NewUpstreamRealmAsync();
        var upstreamB = await NewUpstreamRealmAsync();
        var subjectA = await AddUpstreamUserAsync(upstreamA);
        var subjectB = await AddUpstreamUserAsync(upstreamB);

        var state = new RealmState([], [Provider("oidc-a", upstreamA), Provider("oidc-b", upstreamB)]);
        await new RealmSync(_http, _baseAddress).SyncAsync(tenant, _secrets.Activation, state, TestContext.Current.CancellationToken);
        await PointBackChannelAtTheContainerAsync(tenant, "oidc-a", upstreamA);
        await PointBackChannelAtTheContainerAsync(tenant, "oidc-b", upstreamB);
        await AddTenantClientAsync(tenant);

        await SignInThroughAsync(tenant, "oidc-a", upstreamA);
        await SignInThroughAsync(tenant, "oidc-b", upstreamB);

        var users = await GetJsonAsync($"admin/realms/{tenant}/users?email={Uri.EscapeDataString(Email)}&exact=true");
        Assert.Equal(2, users.GetArrayLength());
        var byUsername = users.EnumerateArray().ToDictionary(user => user.GetProperty("username").GetString()!, user => user.GetProperty("id").GetString()!);
        Assert.Equal(
            [$"oidc-a.{subjectA}".ToLowerInvariant(), $"oidc-b.{subjectB}".ToLowerInvariant()],
            byUsername.Keys.Order(StringComparer.Ordinal).ToArray());
        foreach (var (username, id) in byUsername)
        {
            var identities = await GetJsonAsync($"admin/realms/{tenant}/users/{id}/federated-identity");
            var identity = Assert.Single(identities.EnumerateArray());
            Assert.Equal(username[..username.IndexOf('.', StringComparison.Ordinal)], identity.GetProperty("identityProvider").GetString());
        }
    }

    private RealmBrokeredProvider Provider(string alias, string upstream) =>
        new(alias, new Uri(_baseAddress, $"realms/{upstream}"), "tenant-broker", BrokerSecret, "name", "email", "locale", []);

    private string NewRealm()
    {
        var realm = $"t{Guid.NewGuid():N}"[..16];
        _realms.Add(realm);
        return realm;
    }

    /// <summary>
    /// Makes a realm that plays the company's issuer. Its frontend address is the container's mapped address, so the
    /// <c>iss</c> of a token that the tenant realm fetches from inside the container matches the issuer that the sync stored.
    /// </summary>
    private async Task<string> NewUpstreamRealmAsync()
    {
        var realm = NewRealm();
        using var created = await SendAsync(HttpMethod.Post, "admin/realms", new
        {
            realm,
            enabled = true,
            attributes = new Dictionary<string, string> { ["frontendUrl"] = _baseAddress.AbsoluteUri.TrimEnd('/') },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var client = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/clients", new
        {
            clientId = "tenant-broker",
            enabled = true,
            publicClient = false,
            secret = BrokerSecret,
            standardFlowEnabled = true,
            redirectUris = _anyRedirect,
        });
        Assert.Equal(HttpStatusCode.Created, client.StatusCode);
        return realm;
    }

    /// <summary>Adds the person with a complete profile, so Keycloak's profile check asks for nothing. Returns the user's id, which is the subject.</summary>
    private async Task<string> AddUpstreamUserAsync(string realm)
    {
        using var created = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/users", new
        {
            username = "pat",
            email = Email,
            emailVerified = true,
            enabled = true,
            firstName = "Pat",
            lastName = "Example",
            credentials = new[] { new { type = "password", temporary = false, value = UpstreamPassword } },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return created.Headers.Location!.Segments[^1];
    }

    /// <summary>
    /// The tenant realm runs inside the container, which cannot reach its own mapped port. It calls the upstream realm's
    /// back-channel endpoints on the container's own port instead, and the browser keeps using the mapped one.
    /// </summary>
    private async Task PointBackChannelAtTheContainerAsync(string tenant, string alias, string upstream)
    {
        var path = $"admin/realms/{tenant}/identity-provider/instances/{alias}";
        var provider = await GetJsonAsync(path);
        var representation = provider.Deserialize<Dictionary<string, JsonElement>>()!;
        var config = provider.GetProperty("config").Deserialize<Dictionary<string, string>>()!;
        foreach (var key in new[] { "tokenUrl", "jwksUrl", "userInfoUrl" })
        {
            config[key] = config[key].Replace(_baseAddress.AbsoluteUri, "http://localhost:8080/", StringComparison.Ordinal);
            Assert.Contains($"http://localhost:8080/realms/{upstream}/", config[key], StringComparison.Ordinal);
        }

        representation["config"] = JsonSerializer.SerializeToElement(config);
        using var updated = await SendAsync(HttpMethod.Put, path, representation);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
    }

    private async Task AddTenantClientAsync(string tenant)
    {
        using var response = await SendAsync(HttpMethod.Post, $"admin/realms/{tenant}/clients", new
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
    /// and follows every redirect until Keycloak sends it to the client's callback. A page on the way, such as one that
    /// offers to link an account, fails the test with its HTML.
    /// </summary>
    private async Task SignInThroughAsync(string tenant, string alias, string upstream)
    {
        var jar = new List<(string Name, string Path, string Value)>();
        var authorize = new Uri(_baseAddress,
            $"realms/{tenant}/protocol/openid-connect/auth?client_id={TenantClientId}&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
            + $"&response_type=code&scope=openid&kc_idp_hint={alias}");

        var (page, html) = await FollowAsync(jar, HttpMethod.Get, authorize, null);
        Assert.True(page is not null, $"The sign-in ended at the callback before the login at '{upstream}'.");
        Assert.True(page.AbsolutePath.StartsWith($"/realms/{upstream}/", StringComparison.Ordinal), html);
        var action = new Uri(WebUtility.HtmlDecode(FormActionPattern().Match(html).Groups["action"].Value));

        var (stopped, stoppedHtml) = await FollowAsync(jar, HttpMethod.Post, action, new Dictionary<string, string>
        {
            ["username"] = "pat",
            ["password"] = UpstreamPassword,
            ["credentialId"] = "",
        });
        Assert.True(stopped is null, stoppedHtml);
    }

    /// <summary>
    /// Sends the request and follows each redirect by hand. Returns the address and HTML of the first page that is not a redirect, or
    /// <see langword="null"/> when the redirects end at the callback.
    /// </summary>
    private async Task<(Uri? Page, string Html)> FollowAsync(
        List<(string Name, string Path, string Value)> jar, HttpMethod method, Uri address, Dictionary<string, string>? form)
    {
        for (var hop = 0; hop < 20; hop++)
        {
            using var request = new HttpRequestMessage(method, address);
            if (form is not null)
            {
                request.Content = new FormUrlEncodedContent(form);
            }

            var cookie = string.Join("; ", jar.Where(c => address.AbsolutePath.StartsWith(c.Path, StringComparison.Ordinal)).Select(c => $"{c.Name}={c.Value}"));
            if (cookie.Length > 0)
            {
                request.Headers.Add("Cookie", cookie);
            }

            using var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);
            Remember(jar, response);
            if (response.StatusCode is not (HttpStatusCode.Found or HttpStatusCode.SeeOther))
            {
                var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                return (address, $"{(int)response.StatusCode} {address}\n{html}");
            }

            var location = new Uri(address, response.Headers.Location!);
            if (location.AbsoluteUri.StartsWith(RedirectUri, StringComparison.Ordinal))
            {
                return (null, location.AbsoluteUri);
            }

            method = HttpMethod.Get;
            form = null;
            address = location;
        }

        throw new InvalidOperationException("The sign-in redirected more than 20 times.");
    }

    /// <summary>
    /// Keeps each cookie by name and path. Keycloak sets <c>AUTH_SESSION_ID</c> and <c>KC_RESTART</c> for each realm it
    /// talks to, so one jar that keeps names alone would mix the tenant realm with the upstream one.
    /// </summary>
    private static void Remember(List<(string Name, string Path, string Value)> jar, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return;
        }

        foreach (var cookie in cookies)
        {
            var parts = cookie.Split(';', StringSplitOptions.TrimEntries);
            var separator = parts[0].IndexOf('=', StringComparison.Ordinal);
            var name = parts[0][..separator];
            var value = parts[0][(separator + 1)..];
            var path = parts.Skip(1).Where(part => part.StartsWith("Path=", StringComparison.OrdinalIgnoreCase)).Select(part => part[5..]).FirstOrDefault() ?? "/";
            var expired = parts.Any(part => part.Equals("Max-Age=0", StringComparison.OrdinalIgnoreCase));
            jar.RemoveAll(c => c.Name == name && c.Path == path);
            if (!expired && value.Length > 0)
            {
                jar.Add((name, path, value));
            }
        }
    }

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<string> MasterTokenAsync()
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.AdminClientId,
            ["client_secret"] = KeycloakFixture.AdminClientSecret,
        });
        using var response = await _http.PostAsync(new Uri(_baseAddress, "realms/master/protocol/openid-connect/token"), content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    [GeneratedRegex("""<form\b[^>]*\baction="(?<action>[^"]+)""")]
    private static partial Regex FormActionPattern();
}
