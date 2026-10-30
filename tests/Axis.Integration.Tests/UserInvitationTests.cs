using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axis.Admin;
using Axis.Configuration.Tests;
using Axis.Core.Tenancy;
using Axis.Data.Storage;
using Axis.Server.Authentication;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// An administrator invites a person through <c>POST /api/users/invite</c> and sends a new invitation through
/// <c>POST /api/users/{id}/resend-invitation</c>. The realm is one that <see cref="TenantRealm"/> makes in a real Keycloak, with
/// the tenant's mail settings pointing at a real Mailpit. The person follows the emailed link, sets a password and adds an
/// authenticator app, signs in through the server, and the users list shows them as <c>active</c>. A resend deletes the realm
/// user and creates it again, so the old link no longer reaches the set-password page, and the given roles move to the new id.
/// A test-only path signs the administrator in with the login time and roles that a test asks for. The tenant is served at localhost.
/// </summary>
public sealed partial class UserInvitationTests(KeycloakFixture keycloak, MailpitFixture mailpit, PostgreSqlFixture database)
    : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string Administrator = "administrator";
    private const string Employee = "employee";
    private const string AuthenticatorSecret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
    private const string PlatformSecret = "platform-secret-1";
    private const string UsersSecret = "users-secret-1";
    private const string PatEmail = "pat@example.test";

    private static readonly Guid _rolesId = Guid.Parse("5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c01");

    private readonly string _realm = $"t{Guid.NewGuid():N}"[..16];
    private readonly string _password = Guid.NewGuid().ToString("N");
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private Uri _baseAddress = null!;
    private string _adminToken = null!;
    private string _patId = null!;
    private string? _connectionString;
    private long _lastCodePeriod;
    private WebApplicationFactory<Program>? _factory;
    private TemporaryFolder? _rolesApplication;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task An_invited_person_follows_the_link_sets_a_password_and_an_authenticator_signs_in_and_the_list_shows_them_active()
    {
        var admin = await SessionCookieAsync(Administrator, Administrator, authenticatedMinutesAgo: 0);
        var email = NewEmail();

        using var invited = await InviteAsync(admin, email, "Dana Nguyen Example");

        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var id = (await invited.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetString()!;
        Assert.Equal("invited", (await ListedAsync(admin, email, id)).GetProperty("status").GetString());
        var realmUser = await GetJsonAsync($"admin/realms/{_realm}/users/{id}");
        Assert.Equal(email, realmUser.GetProperty("username").GetString());
        Assert.Equal("Dana Nguyen Example", realmUser.GetProperty("firstName").GetString());

        var link = LinkIn((await WaitForMailsAsync(email, 1)).Single());
        using var browser = new RealmBrowser();
        var newPassword = Guid.NewGuid().ToString("N");
        var secret = await SetPasswordAndAuthenticatorAsync(browser, link, newPassword);

        using var app = CreateClient(cookies: true);
        await SignInAsync(app, email, newPassword, secret);

        var listed = await ListedAsync(admin, email, id);
        Assert.Equal("active", listed.GetProperty("status").GetString());
        Assert.Equal("Dana Nguyen Example", listed.GetProperty("displayName").GetString());
        Assert.Equal(1L, await UserAuditCountAsync("user.invited", id));
        Assert.Equal(0L, await LinkAuditCountAsync());
    }

    [Fact]
    public async Task A_resent_invitation_ends_the_old_link_and_the_new_link_works_and_the_given_role_moves_to_the_new_id()
    {
        var admin = await SessionCookieAsync(Administrator, Administrator, authenticatedMinutesAgo: 0);
        var email = NewEmail();
        using var invited = await InviteAsync(admin, email, "Lan Tran", roles: $$"""[{ "applicationId": "{{_rolesId}}", "role": "buyer" }]""");
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var id = (await invited.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetString()!;
        var firstLink = LinkIn((await WaitForMailsAsync(email, 1)).Single());

        using var resent = await PostAsync($"/api/users/{id}/resend-invitation", admin);

        Assert.Equal(HttpStatusCode.NoContent, resent.StatusCode);
        var messages = await WaitForMailsAsync(email, 2);
        var secondLink = LinkIn(messages[1]);
        Assert.NotEqual(firstLink, secondLink);
        Assert.False(await ReachesRequiredActionAsync(firstLink));
        Assert.True(await ReachesRequiredActionAsync(secondLink));

        using var oldUser = await SendAsync(HttpMethod.Get, $"admin/realms/{_realm}/users/{id}");
        Assert.Equal(HttpStatusCode.NotFound, oldUser.StatusCode);
        var newId = (await GetJsonAsync($"admin/realms/{_realm}/users?email={Uri.EscapeDataString(email)}&exact=true")).EnumerateArray().Single().GetProperty("id").GetString()!;
        Assert.NotEqual(id, newId);
        Assert.Equal([new GivenRole(_rolesId, "buyer")], await GivenRolesAsync(newId));
        Assert.Empty(await GivenRolesAsync(id));

        var audit = Assert.Single(await AuditDetailsAsync("user.invitationResent", email));
        Assert.Equal(newId, audit["userId"]!.GetValue<string>());
        Assert.Equal(id, audit["previousUserId"]!.GetValue<string>());
        Assert.Equal(email, audit["email"]!.GetValue<string>());
        Assert.Single(await AuditDetailsAsync("user.invited", email));
        Assert.Equal(1L, await UserAuditCountAsync("user.roleGiven", id));
        Assert.Equal(0L, await UserAuditCountAsync("user.roleGiven", newId));
        Assert.Equal(0L, await LinkAuditCountAsync());
    }

    [Fact]
    public async Task Inviting_with_a_role_stores_it_and_writes_one_record_for_the_invitation_and_one_for_the_role()
    {
        var admin = await SessionCookieAsync(Administrator, Administrator, authenticatedMinutesAgo: 0);
        var email = NewEmail();

        using var invited = await InviteAsync(admin, $"  {email.ToUpperInvariant()} ", "Minh Le", roles: $$"""[{ "applicationId": "{{_rolesId}}", "role": "buyer" }, { "role": "author" }]""");

        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var id = (await invited.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetString()!;
        Assert.Equal([new GivenRole(null, "author"), new GivenRole(_rolesId, "buyer")], await GivenRolesAsync(id));
        Assert.Equal(1L, await RoleAuditCountAsync(id, _rolesId));
        Assert.Equal(1L, await RoleAuditCountAsync(id, null));
        var audit = Assert.Single(await AuditDetailsAsync("user.invited", email));
        Assert.Equal(id, audit["userId"]!.GetValue<string>());
        Assert.Equal(email, audit["email"]!.GetValue<string>());
        Assert.Equal(3L, await UserAuditCountAsync());
        Assert.Equal(0L, await LinkAuditCountAsync());
        Assert.Single(await WaitForMailsAsync(email, 1));
    }

    [Fact]
    public async Task Inviting_is_refused_without_a_session_without_the_administrator_role_with_an_old_login_for_a_platform_role_and_for_a_used_email()
    {
        var email = NewEmail();
        var admin = await SessionCookieAsync(Administrator, Administrator, authenticatedMinutesAgo: 0);
        var employee = await SessionCookieAsync("employee", Employee, authenticatedMinutesAgo: 0);
        var stale = await SessionCookieAsync(Administrator, Administrator, authenticatedMinutesAgo: 6);

        using var nobody = await InviteAsync(null, email, "Nobody");
        using var forbidden = await InviteAsync(employee, email, "Employee");
        using var oldLogin = await InviteAsync(stale, email, "Old Login", roles: """[{ "role": "administrator" }]""");
        using var used = await InviteAsync(admin, PatEmail, "Pat Again");
        using var usedInOtherCase = await InviteAsync(admin, "PAT@Example.Test", "Pat Again");

        Assert.Equal(HttpStatusCode.Unauthorized, nobody.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, used.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, usedInOtherCase.StatusCode);
        Assert.Empty((await GetJsonAsync($"admin/realms/{_realm}/users?email={Uri.EscapeDataString(email)}&exact=true")).EnumerateArray());
        Assert.Empty(await mailpit.MessagesToAsync(email, CancellationToken));
        Assert.Equal(0L, await UserAuditCountAsync());
        Assert.Equal(0L, await GivenRolesCountAsync());
    }

    [Fact]
    public async Task Inviting_with_a_body_that_is_not_valid_answers_400_and_changes_nothing()
    {
        var admin = await SessionCookieAsync(Administrator, Administrator, authenticatedMinutesAgo: 0);
        var email = NewEmail();

        using var malformed = await PostJsonAsync("/api/users/invite", admin, "{ not json");
        using var noAddress = await InviteAsync(admin, "not an address", "Dana");
        using var noName = await InviteAsync(admin, email, "   ");
        using var blankRole = await InviteAsync(admin, email, "Dana", roles: """[{ "role": " " }]""");
        using var notAKind = await InviteAsync(admin, email, "Dana", roles: """[{ "role": "buyer" }]""");
        using var notDeclared = await InviteAsync(admin, email, "Dana", roles: $$"""[{ "applicationId": "{{_rolesId}}", "role": "seller" }]""");
        using var noRelease = await InviteAsync(admin, email, "Dana", roles: $$"""[{ "applicationId": "{{Guid.NewGuid()}}", "role": "buyer" }]""");

        foreach (var response in new[] { malformed, noAddress, noName, blankRole, notAKind, notDeclared, noRelease })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Empty((await GetJsonAsync($"admin/realms/{_realm}/users?email={Uri.EscapeDataString(email)}&exact=true")).EnumerateArray());
        Assert.Equal(0L, await UserAuditCountAsync());
    }

    [Fact]
    public async Task A_platform_role_at_invite_time_needs_a_recent_login_and_is_stored_when_there_is_one()
    {
        var admin = await SessionCookieAsync(Administrator, Administrator, authenticatedMinutesAgo: 1);
        var email = NewEmail();

        using var invited = await InviteAsync(admin, email, "Operator Person", roles: """[{ "role": "operator" }]""");

        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var id = (await invited.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetString()!;
        Assert.Equal([new GivenRole(null, "operator")], await GivenRolesAsync(id));
        var role = Assert.Single(await AuditDetailsAsync("user.roleGiven", id, key: "userId"));
        Assert.Equal("operator", role["role"]!.GetValue<string>());
        Assert.True(role["platform"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Resending_is_refused_without_a_session_without_the_role_for_an_unknown_id_a_person_with_a_password_and_a_person_who_has_signed_in()
    {
        var admin = await SessionCookieAsync(Administrator, Administrator, authenticatedMinutesAgo: 0);
        var employee = await SessionCookieAsync("employee", Employee, authenticatedMinutesAgo: 0);
        var email = NewEmail();
        using var invited = await InviteAsync(admin, email, "Signed In");
        var id = (await invited.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetString()!;
        await PostgreSqlServer.AdminScalarAsync(
            _connectionString!,
            $"""INSERT INTO axis.user_profiles (user_id, display_name, first_signed_in_at, last_signed_in_at) VALUES ('{id}', 'Signed In', now(), now()) RETURNING 1""",
            CancellationToken);

        using var nobody = await PostAsync($"/api/users/{id}/resend-invitation", null);
        using var forbidden = await PostAsync($"/api/users/{id}/resend-invitation", employee);
        using var unknown = await PostAsync($"/api/users/{Guid.NewGuid()}/resend-invitation", admin);
        using var signedIn = await PostAsync($"/api/users/{id}/resend-invitation", admin);
        using var withPassword = await PostAsync($"/api/users/{_patId}/resend-invitation", admin);

        Assert.Equal(HttpStatusCode.Unauthorized, nobody.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, signedIn.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, withPassword.StatusCode);
        using var stillThere = await SendAsync(HttpMethod.Get, $"admin/realms/{_realm}/users/{id}");
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
        using var patStillThere = await SendAsync(HttpMethod.Get, $"admin/realms/{_realm}/users/{_patId}");
        Assert.Equal(HttpStatusCode.OK, patStillThere.StatusCode);
        Assert.Empty(await AuditDetailsAsync("user.invitationResent", email));
        Assert.Single(await WaitForMailsAsync(email, 1));
    }

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _adminToken = await MasterTokenAsync();
        var smtpServer = new Dictionary<string, string>
        {
            ["host"] = MailpitFixture.NetworkAlias,
            ["port"] = $"{MailpitFixture.NetworkSmtpPort}",
            ["from"] = "noreply@example.test",
            ["starttls"] = "false",
        };
        await mailpit.SmtpHostAsync();
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(_realm, "Acme", new TenantRealmSecrets(PlatformSecret, "activation-secret-1", UsersSecret), smtpServer, CancellationToken);
        await ConfigureClientAsync();
        _patId = await CreatePatAsync();

        _connectionString = await database.CreateDatabaseAsync("user_invitations", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(_connectionString, CancellationToken);
        _rolesApplication = RolesApplication();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(_connectionString);
            builder.UseSetting("Tenants:a:Hosts:0", "localhost");
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_connectionString));
            builder.UseSetting("Tenants:a:SchemaConnectionString", _connectionString);
            builder.UseSetting("Tenants:a:Oidc:Authority", new Uri(_baseAddress, $"realms/{_realm}").ToString());
            builder.UseSetting("Tenants:a:Oidc:ClientId", "axis");
            builder.UseSetting("Tenants:a:Oidc:ClientSecret", PlatformSecret);
            builder.UseSetting("Tenants:a:Oidc:UsersClientSecret", UsersSecret);
            builder.UseSetting("Tenants:a:Oidc:RolesClaim", "roles");
            builder.UseSetting("Users:AdministratorRole", Administrator);
            builder.UseSetting("Console:OperatorRole", "operator");
            builder.UseSetting("ActivateOnStartup:0", _rolesApplication.Path);
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, SessionStartupFilter>());
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        _rolesApplication?.Dispose();
        using var response = await SendAsync(HttpMethod.Delete, $"admin/realms/{_realm}");
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        _http.Dispose();
    }

    // Mailpit is shared by the whole run, so every test invites addresses of its own.
    private static string NewEmail() => $"{Guid.NewGuid():N}@example.test";

    private static string LinkIn(MailpitMessage message)
    {
        var match = ActionTokenLinkPattern().Match(message.Text);
        Assert.True(match.Success, message.Text);
        return match.Value;
    }

    [GeneratedRegex("""https?://[^\s<>"]*login-actions/action-token[^\s<>"]*""")]
    private static partial Regex ActionTokenLinkPattern();

    // The info page's own link, not the links of its language menu.
    [GeneratedRegex("""id="kc-info-message".*?<a href="(?<address>[^"]*)"+""", RegexOptions.Singleline)]
    private static partial Regex ProceedLinkPattern();

    private async Task<IReadOnlyList<MailpitMessage>> WaitForMailsAsync(string address, int count)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var messages = await mailpit.MessagesToAsync(address, CancellationToken);
            if (messages.Count >= count)
            {
                Assert.Equal(count, messages.Count);
                return messages;
            }

            Assert.True(DateTimeOffset.UtcNow < deadline, $"Mailpit received {messages.Count} of {count} emails for {address} within 30 seconds.");
            await Task.Delay(250, CancellationToken);
        }
    }

    /// <summary>
    /// Opens the emailed link in the browser. Keycloak shows an info page with a link that proceeds, and then the page that sets a password.
    /// Returns the HTML of that page, or null when the link leads somewhere else, such as an error page.
    /// </summary>
    private static async Task<string?> OpenLinkAsync(RealmBrowser browser, string link)
    {
        var html = await FollowAsync(browser, await browser.GetAsync(new Uri(link)));

        for (var hops = 0; hops < 3 && html is not null && !html.Contains("axis-sign-in-context", StringComparison.Ordinal); hops++)
        {
            if (ProceedLinkPattern().Match(html) is not { Success: true } proceed)
            {
                return null;
            }

            html = await FollowAsync(browser, await browser.GetAsync(new Uri(new Uri(link), WebUtility.HtmlDecode(proceed.Groups["address"].Value))));
        }

        return html is not null && html.Contains("axis-sign-in-context", StringComparison.Ordinal) ? html : null;
    }

    // Follows redirects and returns the HTML of the page it ends on, or null when that page is not a 200.
    private static async Task<string?> FollowAsync(RealmBrowser browser, HttpResponseMessage response)
    {
        for (var hops = 0; hops < 5 && response.StatusCode == HttpStatusCode.Found; hops++)
        {
            var location = response.Headers.Location!;
            response.Dispose();
            response = await browser.GetAsync(location);
        }

        using (response)
        {
            return response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadAsStringAsync(CancellationToken) : null;
        }
    }

    // The invitation asks for a password and an authenticator, in the order that the identity provider chooses.
    private static async Task<bool> ReachesRequiredActionAsync(string link)
    {
        using var browser = new RealmBrowser();
        var html = await OpenLinkAsync(browser, link);
        return html is not null && KeycloakSignIn.Page(html) is "updatePassword" or "addAuthenticator";
    }

    /// <summary>Follows the link, sets the password and adds an authenticator app. Returns the authenticator's secret.</summary>
    private async Task<string> SetPasswordAndAuthenticatorAsync(RealmBrowser browser, string link, string password)
    {
        var html = await OpenLinkAsync(browser, link);
        string? secret = null;
        for (var step = 0; step < 2; step++)
        {
            Assert.NotNull(html);
            var page = KeycloakSignIn.Page(html);
            Dictionary<string, string> form;
            if (page == "updatePassword")
            {
                form = new Dictionary<string, string> { ["password-new"] = password, ["password-confirm"] = password };
            }
            else
            {
                Assert.Equal("addAuthenticator", page);
                secret = KeycloakSignIn.Property(html, "manualKey")!.Replace(" ", "", StringComparison.Ordinal);
                _lastCodePeriod = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
                form = new Dictionary<string, string>
                {
                    ["totp"] = DevelopmentTotp.CodeFromSecret(secret, DateTimeOffset.UtcNow),
                    ["totpSecret"] = KeycloakSignIn.Property(html, "secret")!,
                    ["userLabel"] = "Phone",
                };
            }

            var response = await browser.PostAsync(KeycloakSignIn.LoginAction(html).AbsoluteUri, form);
            if (step == 0)
            {
                html = await FollowAsync(browser, response);
                continue;
            }

            // The last action ends on a page that links back to the address the invitation named, the tenant's home.
            using (response)
            {
                var done = await response.Content.ReadAsStringAsync(CancellationToken);
                Assert.True(response.StatusCode == HttpStatusCode.OK, done);
                Assert.Contains("href=\"http://localhost/\"", done, StringComparison.Ordinal);
            }
        }

        return secret ?? throw new InvalidOperationException("The invitation did not ask for an authenticator.");
    }

    private async Task<HttpResponseMessage> InviteAsync(string? cookie, string email, string displayName, string? roles = null)
    {
        var body = new JsonObject { ["email"] = email, ["displayName"] = displayName };
        if (roles is not null)
        {
            body["roles"] = JsonNode.Parse(roles);
        }

        return await PostJsonAsync("/api/users/invite", cookie, body.ToJsonString());
    }

    /// <summary>The row of the users list for the person, found by searching their email.</summary>
    private async Task<JsonElement> ListedAsync(string cookie, string email, string id)
    {
        using var response = await GetAsync($"/api/users?search={Uri.EscapeDataString(email)}", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return list.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
    }

    // The client of the person signs in at the server's challenge, gives the password and a code of the authenticator, and follows the callback.
    private async Task SignInAsync(HttpClient app, string username, string password, string secret)
    {
        using var challenge = await app.GetAsync("/api/auth/sign-in", CancellationToken);
        Assert.Equal(HttpStatusCode.Found, challenge.StatusCode);
        using var browser = new RealmBrowser();
        using var page = await browser.GetAsync(challenge.Headers.Location!);
        var loginHtml = await page.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(page.StatusCode == HttpStatusCode.OK, loginHtml);
        using var afterPassword = await browser.PostAsync(
            KeycloakSignIn.LoginAction(loginHtml).AbsoluteUri,
            new Dictionary<string, string> { ["username"] = username, ["password"] = password, ["credentialId"] = "" });
        var html = await afterPassword.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(afterPassword.StatusCode == HttpStatusCode.OK && KeycloakSignIn.Page(html) == "twoStep", html);
        using var login = await browser.PostAsync(
            KeycloakSignIn.LoginAction(html).AbsoluteUri,
            new Dictionary<string, string> { ["otp"] = await NextAuthenticatorCodeAsync(secret), ["login"] = "Sign In" });
        Assert.True(login.StatusCode == HttpStatusCode.Found, await login.Content.ReadAsStringAsync(CancellationToken));
        var callback = login.Headers.Location!;
        Assert.Equal("http://localhost/api/auth/callback", callback.GetLeftPart(UriPartial.Path));
        using var signedIn = await app.GetAsync(callback.PathAndQuery, CancellationToken);
        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
    }

    // The realm accepts only the code of the current 30 seconds and refuses one it has seen, so a second sign-in waits for the next period.
    private async Task<string> NextAuthenticatorCodeAsync(string secret)
    {
        while (true)
        {
            var now = DateTimeOffset.UtcNow;
            var period = now.ToUnixTimeSeconds() / 30;
            if (period > _lastCodePeriod && now.ToUnixTimeSeconds() % 30 < 27)
            {
                _lastCodePeriod = period;
                return DevelopmentTotp.CodeFromSecret(secret, now);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken);
        }
    }

    // The axis client accepts the test host's callback and home, and puts the realm roles of a person in its roles claim.
    private async Task ConfigureClientAsync()
    {
        var axis = (await GetJsonAsync($"admin/realms/{_realm}/clients?clientId=axis")).EnumerateArray().Single();
        var uuid = axis.GetProperty("id").GetString()!;
        var representation = JsonNode.Parse(axis.GetRawText())!.AsObject();
        representation["redirectUris"] = new JsonArray("http://localhost/*");
        using var updated = await SendAsync(HttpMethod.Put, $"admin/realms/{_realm}/clients/{uuid}", representation);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

        using var mapper = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/clients/{uuid}/protocol-mappers/models", new
        {
            name = "roles",
            protocol = "openid-connect",
            protocolMapper = "oidc-usermodel-realm-role-mapper",
            config = new Dictionary<string, string>
            {
                ["claim.name"] = "roles",
                ["multivalued"] = "true",
                ["jsonType.label"] = "String",
                ["id.token.claim"] = "true",
                ["access.token.claim"] = "true",
                ["userinfo.token.claim"] = "true",
            },
        });
        Assert.Equal(HttpStatusCode.Created, mapper.StatusCode);
    }

    // An account that exists before any invitation, with a password and an authenticator, such as a development user.
    private async Task<string> CreatePatAsync()
    {
        using var created = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/users", new
        {
            username = "pat",
            enabled = true,
            emailVerified = true,
            email = PatEmail,
            firstName = "Pat",
            lastName = "Example",
            credentials = new object[]
            {
                new { type = "password", value = _password, temporary = false },
                new
                {
                    type = "otp",
                    secretData = $$"""{"value":"{{AuthenticatorSecret}}"}""",
                    credentialData = """{"subType":"totp","digits":6,"period":30,"algorithm":"HmacSHA1","counter":0,"secretEncoding":"BASE32"}""",
                },
            },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return created.Headers.Location!.Segments[^1];
    }

    private HttpClient CreateClient(bool cookies) =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = cookies,
        });

    /// <summary>Signs a user in through the test-only path, with a last login <paramref name="authenticatedMinutesAgo"/> minutes ago or none, and returns the session cookie's value.</summary>
    private async Task<string> SessionCookieAsync(string user, string role, int? authenticatedMinutesAgo)
    {
        using var client = CreateClient(cookies: false);
        var query = authenticatedMinutesAgo is { } minutes ? $"&authenticatedMinutesAgo={minutes}" : string.Empty;
        using var response = await client.GetAsync(new Uri($"/test/session?user={user}&role={role}{query}", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var prefix = $"{Session.Scheme}=";
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return setCookie[prefix.Length..].Split(';')[0];
    }

    private async Task<HttpResponseMessage> PostAsync(string path, string? cookie)
    {
        using var client = CreateClient(cookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        request.Headers.Add("X-Axis-Csrf", "1");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string path, string? cookie, string body)
    {
        using var client = CreateClient(cookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string? cookie)
    {
        using var client = CreateClient(cookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        }

        return await client.SendAsync(request, CancellationToken);
    }

    /// <summary>The application <c>Roles</c> with the role <c>buyer</c>.</summary>
    private static TemporaryFolder RolesApplication() => new TemporaryFolder()
        .With("application.json", $$"""{ "id": "{{_rolesId}}", "kind": "application", "name": "Roles", "formatVersion": 1 }""")
        .With("texts/en.json", """
            { "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c10", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
              "texts": { "role.buyer": "Buyer" } }
            """)
        .With("roles/buyer.json", """{ "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c13", "kind": "role", "name": "buyer", "formatVersion": 1, "label": { "textKey": "role.buyer" } }""")
        .With("entities/note.json", """
            { "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c21", "kind": "entity", "name": "Note", "formatVersion": 1, "displayField": "title",
              "fields": [ { "name": "title", "type": "text", "required": true } ] }
            """)
        .With("policies/buyer-access.json", """
            { "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c15", "kind": "policy", "name": "BuyerAccess", "formatVersion": 1,
              "roles": ["buyer"], "grants": [ { "entity": "Note", "actions": ["read"], "fields": ["title"] } ] }
            """);

    private async Task<GivenRole[]> GivenRolesAsync(string userId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(CancellationToken);
        return [.. await GivenRoles.ListAsync(connection, userId, CancellationToken)];
    }

    private async Task<long> GivenRolesCountAsync() =>
        (long)(await PostgreSqlServer.AdminScalarAsync(_connectionString!, "SELECT count(*) FROM axis.given_roles", CancellationToken))!;

    private async Task<long> UserAuditCountAsync() =>
        (long)(await PostgreSqlServer.AdminScalarAsync(_connectionString!, "SELECT count(*) FROM axis.audit_records WHERE action LIKE 'user.%'", CancellationToken))!;

    private async Task<long> UserAuditCountAsync(string action, string userId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM axis.audit_records WHERE action = @action AND details->>'userId' = @user", connection);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("user", userId);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private async Task<long> RoleAuditCountAsync(string userId, Guid? application)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM axis.audit_records WHERE action = 'user.roleGiven' AND details->>'userId' = @user AND application_id IS NOT DISTINCT FROM @application",
            connection);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.Add(new NpgsqlParameter("application", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)application ?? DBNull.Value });
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    // The details of the records of the action whose details name the value under the key.
    private async Task<List<JsonObject>> AuditDetailsAsync(string action, string value, string key = "email")
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT details::text FROM axis.audit_records WHERE action = @action AND details->>@key = @value", connection);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("value", value);
        var details = new List<JsonObject>();
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        while (await reader.ReadAsync(CancellationToken))
        {
            details.Add(JsonNode.Parse(reader.GetString(0))!.AsObject());
        }

        return details;
    }

    // No audit record holds the link, which is an action token with a key.
    private async Task<long> LinkAuditCountAsync() =>
        (long)(await PostgreSqlServer.AdminScalarAsync(
            _connectionString!,
            "SELECT count(*) FROM axis.audit_records WHERE details::text LIKE '%action-token%' OR details::text LIKE '%key=%'",
            CancellationToken))!;

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _http.SendAsync(request, CancellationToken);
    }

    private async Task<string> MasterTokenAsync()
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.AdminClientId,
            ["client_secret"] = KeycloakFixture.AdminClientSecret,
        });
        using var response = await _http.PostAsync(new Uri(_baseAddress, "realms/master/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// The browser at the realm. Keycloak marks its cookies Secure, which a browser sends to http://localhost but a
    /// CookieContainer does not, so the browser carries them itself.
    /// </summary>
    private sealed class RealmBrowser : IDisposable
    {
        private readonly HttpClient _client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        private readonly Dictionary<string, string> _jar = [];

        public async Task<HttpResponseMessage> GetAsync(Uri address)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            return await SendAsync(request);
        }

        public async Task<HttpResponseMessage> PostAsync(string address, Dictionary<string, string> form)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(address)) { Content = new FormUrlEncodedContent(form) };
            return await SendAsync(request);
        }

        public void Dispose() => _client.Dispose();

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            if (_jar.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", _jar.Select(cookie => $"{cookie.Key}={cookie.Value}")));
            }

            var response = await _client.SendAsync(request, CancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var pair in setCookies.Select(setCookie => setCookie.Split(';')[0].Split('=', 2)))
                {
                    _jar[pair[0]] = pair[1];
                }
            }

            return response;
        }
    }

    /// <summary>
    /// Answers <c>GET /test/session?user=&amp;role=&amp;authenticatedMinutesAgo=</c> by signing the user in with that role and a last login
    /// at the identity provider that many minutes ago, or with none when the minutes are missing.
    /// </summary>
    private sealed class SessionStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // The session store reads the tenant, which the real pipeline resolves only after this filter.
            app.UseWhen(
                context => HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/test/session",
                branch =>
                {
                    branch.UseTenantResolution();
                    branch.Run(async context =>
                    {
                        var tenantId = context.RequestServices.GetRequiredService<ITenantContextAccessor>().Current!.TenantId;
                        var clock = context.RequestServices.GetRequiredService<TimeProvider>();
                        var user = context.Request.Query["user"].ToString();
                        DateTimeOffset? authenticatedAt = int.TryParse(context.Request.Query["authenticatedMinutesAgo"], out var minutes)
                            ? clock.GetUtcNow() - TimeSpan.FromMinutes(minutes)
                            : null;
                        await context.SignInAsync(Session.Scheme, Session.CreatePrincipal(tenantId, user, user, [context.Request.Query["role"].ToString()], authenticatedAt));
                        context.Response.StatusCode = StatusCodes.Status204NoContent;
                    });
                });
            next(app);
        };
    }
}
