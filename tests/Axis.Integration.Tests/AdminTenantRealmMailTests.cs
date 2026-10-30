using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axis.Integration.Tests;

/// <summary>
/// A tenant's realm in a real Keycloak sends its emails through the tenant's mail settings, or through the host's <c>Email</c>
/// settings when the tenant has none. "Forgot password?" sends a reset link to a real Mailpit server, also to the one that requires
/// a sign-in. <c>tenant email set</c> and <c>clear</c> and <c>activate</c> write the realm's mail server, and nothing prints the
/// SMTP password, which the admin API shows masked.
/// </summary>
public sealed class AdminTenantRealmMailTests(KeycloakFixture keycloak, MailpitFixture mailpit, PostgreSqlFixture database)
    : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string ClientId = "reset-mail";
    private const string RedirectUri = "http://localhost/callback";
    private const string Masked = "**********";

    private readonly HttpClientHandler _handler = new();
    private readonly List<string> _realms = [];
    private Uri _baseAddress = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _baseAddress = await keycloak.BaseAddressAsync();

    public async ValueTask DisposeAsync()
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        foreach (var realm in _realms)
        {
            using var response = await SendAsync(http, token, HttpMethod.Delete, $"admin/realms/{realm}");
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _handler.Dispose();
    }

    [Fact]
    public async Task Forgot_password_sends_the_reset_link_through_the_mail_settings_of_the_tenant()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantAsync(scenario);
        await mailpit.SmtpHostAsync();

        var set = await scenario.RunAsync(
            ["tenant", "email", "set", id, "--host", MailpitFixture.NetworkAlias, "--port", $"{MailpitFixture.NetworkSmtpPort}", "--tls", "false", "--from", $"noreply@{id}.test"],
            identityProvider: _handler,
            extra: Settings());
        Assert.Equal(0, set.Exit);
        Assert.Equal([$"{id} email ok"], set.Output);

        var address = $"{id}-reset@example.test";
        await RequestResetAsync(id, address);

        var message = await WaitForMailAsync(address, authenticated: false);
        Assert.Contains("login-actions/action-token", message.Text, StringComparison.Ordinal);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Clear_falls_back_to_the_email_settings_of_the_host_and_to_no_mail_server_when_the_host_has_none()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantAsync(scenario);
        var host = Settings(new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = MailpitFixture.NetworkAlias,
            ["Email:Smtp:Port"] = $"{MailpitFixture.NetworkSmtpPort}",
            ["Email:Smtp:UseTls"] = "false",
            ["Email:From"] = "host@example.test",
        });

        var set = await scenario.RunAsync(
            ["tenant", "email", "set", id, "--host", "smtp.tenant.test", "--from", "tenant@example.test"], identityProvider: _handler, extra: host);
        Assert.Equal(0, set.Exit);
        var own = await SmtpServerAsync(id);
        Assert.Equal("smtp.tenant.test", own.GetProperty("host").GetString());
        Assert.Equal("tenant@example.test", own.GetProperty("from").GetString());
        Assert.Equal("true", own.GetProperty("starttls").GetString());
        Assert.Equal("587", own.GetProperty("port").GetString());

        var cleared = await scenario.RunAsync(["tenant", "email", "clear", id], identityProvider: _handler, extra: host);
        Assert.Equal(0, cleared.Exit);
        Assert.Equal([$"{id} email ok"], cleared.Output);
        var fallback = await SmtpServerAsync(id);
        Assert.Equal(MailpitFixture.NetworkAlias, fallback.GetProperty("host").GetString());
        Assert.Equal("host@example.test", fallback.GetProperty("from").GetString());
        Assert.Equal("false", fallback.GetProperty("starttls").GetString());

        var again = await scenario.RunAsync(
            ["tenant", "email", "set", id, "--host", "smtp.tenant.test", "--from", "tenant@example.test"], identityProvider: _handler, extra: host);
        Assert.Equal(0, again.Exit);
        var none = await scenario.RunAsync(["tenant", "email", "clear", id], identityProvider: _handler, extra: Settings());
        Assert.Equal(0, none.Exit);
        Assert.False((await SmtpServerAsync(id)).TryGetProperty("host", out _));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task The_realm_signs_in_to_the_mail_server_with_the_stored_password_and_nothing_prints_it_and_the_admin_api_masks_it()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantAsync(scenario);
        await mailpit.AuthSmtpHostAsync();

        var set = await scenario.RunAsync(
            [
                "tenant", "email", "set", id, "--host", MailpitFixture.AuthNetworkAlias, "--port", $"{MailpitFixture.NetworkSmtpPort}", "--tls", "false",
                "--from", $"noreply@{id}.test", "--user-name", MailpitFixture.AuthUserName, "--password",
            ],
            identityProvider: _handler,
            input: MailpitFixture.AuthPassword,
            extra: Settings());

        Assert.Equal(0, set.Exit);
        var server = await SmtpServerAsync(id);
        Assert.Equal("true", server.GetProperty("auth").GetString());
        Assert.Equal(MailpitFixture.AuthUserName, server.GetProperty("user").GetString());
        Assert.Equal(Masked, server.GetProperty("password").GetString());

        // Keycloak never returns the password, so only a sign-in to the server that requires it shows that the copy is the stored one.
        var address = $"{id}-auth@example.test";
        await RequestResetAsync(id, address);
        var message = await WaitForMailAsync(address, authenticated: true);
        Assert.Contains("login-actions/action-token", message.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(MailpitFixture.AuthPassword, string.Join('\n', [.. set.Output, .. set.Error]), StringComparison.Ordinal);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Activate_writes_the_mail_server_of_the_tenant_again_after_it_was_changed_in_the_identity_provider()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantAsync(scenario);
        var set = await scenario.RunAsync(
            ["tenant", "email", "set", id, "--host", "smtp.tenant.test", "--from", "tenant@example.test"], identityProvider: _handler, extra: Settings());
        Assert.Equal(0, set.Exit);
        await ChangeSmtpServerAsync(id, new JsonObject { ["host"] = "other.test", ["port"] = "25", ["from"] = "other@example.test" });
        Assert.Equal("other.test", (await SmtpServerAsync(id)).GetProperty("host").GetString());
        using var folder = AdminActivateRealmTests.Folder(new AdminActivateRealmTests.Application(), ["sales"], provider: false);

        var result = await scenario.RunAsync(["activate", folder.Path, "--tenant", id], identityProvider: _handler);

        Assert.Equal(0, result.Exit);
        var server = await SmtpServerAsync(id);
        Assert.Equal("smtp.tenant.test", server.GetProperty("host").GetString());
        Assert.Equal("tenant@example.test", server.GetProperty("from").GetString());
        await scenario.AssertNoSecretsAsync();
    }

    // The settings that the commands need: the directory source, Development, which allows a mail server without TLS, and the real identity provider.
    private Dictionary<string, string?> Settings(IReadOnlyDictionary<string, string?>? more = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["IdentityProvider:Url"] = _baseAddress.ToString(),
            ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
            ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
        };
        foreach (var (name, value) in more ?? new Dictionary<string, string?>())
        {
            settings[name] = value;
        }

        return settings;
    }

    // Creates the tenant with its realm in the real Keycloak.
    private async Task<string> CreateTenantAsync(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        _realms.Add(id);
        var created = await scenario.RunAsync(AdminScenario.CreateArgs(id, $"{id}.example.test"), identityProvider: _handler, extra: Settings());
        Assert.Equal(0, created.Exit);
        return id;
    }

    // Asks for "Forgot password?" in the realm as a person does: opens the sign-in page, follows its link and posts the username.
    private async Task RequestResetAsync(string realm, string address)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        using (var client = await SendAsync(
            http, token, HttpMethod.Post, $"admin/realms/{realm}/clients", new { clientId = ClientId, publicClient = true, standardFlowEnabled = true, redirectUris = new[] { RedirectUri } }))
        {
            Assert.Equal(HttpStatusCode.Created, client.StatusCode);
        }

        using (var user = await SendAsync(
            http, token, HttpMethod.Post, $"admin/realms/{realm}/users", new { username = address, email = address, enabled = true, emailVerified = true, firstName = "Pat", lastName = "Example" }))
        {
            Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        }

        using var browserHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var browser = new HttpClient(browserHandler);
        var authorize = new Uri(
            _baseAddress,
            $"realms/{realm}/protocol/openid-connect/auth?client_id={ClientId}&response_type=code&scope=openid&redirect_uri={Uri.EscapeDataString(RedirectUri)}");
        var jar = new Dictionary<string, string>();

        var (loginHtml, _) = await GetPageAsync(browser, authorize, jar);
        var forgot = new Uri(_baseAddress, KeycloakSignIn.Property(loginHtml, "forgotPasswordUrl")!);
        var (resetHtml, _) = await GetPageAsync(browser, forgot, jar);

        using var post = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseAddress, KeycloakSignIn.LoginAction(resetHtml).OriginalString))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = address }),
        };
        post.Headers.Add("Cookie", CookieHeader(jar));
        using var response = await browser.SendAsync(post, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Found, $"{response.StatusCode}\n{body}");
    }

    private static async Task<(string Html, HttpStatusCode Status)> GetPageAsync(HttpClient browser, Uri address, Dictionary<string, string> jar)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        if (jar.Count > 0)
        {
            request.Headers.Add("Cookie", CookieHeader(jar));
        }

        using var response = await browser.SendAsync(request, CancellationToken);
        var html = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}\n{html}");

        // Keycloak marks its cookies Secure, which a browser sends to http://localhost but a CookieContainer does not.
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var pair in cookies.Select(cookie => cookie.Split(';')[0]))
            {
                var separator = pair.IndexOf('=', StringComparison.Ordinal);
                jar[pair[..separator]] = pair[(separator + 1)..];
            }
        }

        return (html, response.StatusCode);
    }

    private static string CookieHeader(Dictionary<string, string> jar) =>
        string.Join("; ", jar.Select(cookie => $"{cookie.Key}={cookie.Value}"));

    // Keycloak sends the email while it answers the request, but a slow start can delay it.
    private async Task<MailpitMessage> WaitForMailAsync(string address, bool authenticated)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var messages = await mailpit.MessagesToAsync(address, CancellationToken, authenticated);
            if (messages.Count > 0)
            {
                return Assert.Single(messages);
            }

            Assert.True(DateTimeOffset.UtcNow < deadline, $"Mailpit received no email for {address} within 30 seconds.");
            await Task.Delay(250, CancellationToken);
        }
    }

    // The realm's mail server as the admin API shows it. A cleared one has no properties.
    private async Task<JsonElement> SmtpServerAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        using var response = await SendAsync(http, token, HttpMethod.Get, $"admin/realms/{realm}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.TryGetProperty("smtpServer", out var server) ? server : JsonDocument.Parse("{}").RootElement;
    }

    private async Task ChangeSmtpServerAsync(string realm, JsonObject smtpServer)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        using var response = await SendAsync(http, token, HttpMethod.Put, $"admin/realms/{realm}", new JsonObject { ["smtpServer"] = smtpServer });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<string> MasterTokenAsync(HttpClient http)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.AdminClientId,
            ["client_secret"] = KeycloakFixture.AdminClientSecret,
        });
        using var response = await http.PostAsync(new Uri(_baseAddress, "realms/master/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, string token, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request, CancellationToken);
    }
}
