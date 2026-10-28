using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Configuration.Tests;
using Axis.Server.Authentication;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant create --preview-of</c> under the tenant directory. A preview tenant has its own hosts, database and
/// bindings, and no sign-in settings of its own: the directory gives it those of its main tenant.
/// </summary>
public sealed partial class AdminPreviewTenantTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>
{
    private const string BindingValue = "not-a-url-SECRET123";

    private static readonly string _sample = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

    private static readonly string[] _createSteps = ["entry", "roles", "database", "grants", "migrations", "grants", "role-check", "version"];

    private static readonly string[] _activateSteps = ["version", "compile", "activate", "seed", "indexes"];

    private static readonly Uri _serverAddress = new("http://localhost:5206");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_preview_tenant_is_served_on_its_own_host_and_database_and_a_user_signs_in_there_with_the_main_tenants_settings()
    {
        await using var scenario = await NewScenarioAsync();
        var authority = await keycloak.StartAsync();
        var main = scenario.NewTenantId();
        var preview = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(main, "main.example.test")).Exit);

        // The tenant's own realm has no users, so it is pointed at the development realm.
        var created = await scenario.RunAsync(
            ["tenant", "oidc", "set", main, "--authority", authority.ToString(), "--client-id", KeycloakFixture.ClientId, "--client-secret"],
            extra: new Dictionary<string, string?>
            {
                ["Tenancy:Source"] = "directory",
                ["AXIS_OIDC_CLIENT_SECRET"] = KeycloakFixture.ClientSecret,
            });
        Assert.Equal(0, created.Exit);

        var result = await scenario.RunAsync(
            AdminScenario.PreviewArgs(preview, main, "localhost", "PurchasingSystemUrl=https://purchasing.example.test"),
            withClientSecret: false);

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Equal(_createSteps.Select(step => $"{preview} {step} ok"), result.Output);
        var row = await scenario.RowAsync(preview);
        Assert.Equal(main, row.PreviewOf);
        Assert.Equal($"axis_{preview}", row.DatabaseName);
        Assert.Equal("active", row.Status);
        Assert.Equal("", row.OidcAuthority);
        Assert.Equal("", row.OidcClientId);
        Assert.Empty(row.OidcClientSecret);
        Assert.Equal(["localhost"], await scenario.HostsAsync(preview));
        Assert.Equal(1, await scenario.CountBindingsAsync(preview));

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseTestPlatform(scenario.Platform);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Modules:Path", SampleModules.Path);
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = _serverAddress,
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        await SignInAsync(client, authority, "requester");

        using var me = await client.GetAsync("/api/me", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("requester", body.RootElement.GetProperty("id").GetString());
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task The_catalog_gives_a_preview_tenant_the_main_tenants_sign_in_settings_without_its_endpoint_and_service_audiences()
    {
        await using var scenario = await NewScenarioAsync();
        var main = scenario.NewTenantId();
        var preview = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(main, "main.example.test")).Exit);
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.PreviewArgs(preview, main, "preview.example.test"), withClientSecret: false)).Exit);
        await scenario.SetEndpointAudienceAsync(main, "axis-endpoints");
        await scenario.SetServiceAudienceAsync(main, "axis-services");

        var catalog = await DirectoryTenantCatalog.LoadAsync(scenario.Platform, AdminScenario.Cipher, CancellationToken);

        var mainEntry = catalog.Find(main)!;
        var previewEntry = catalog.Find(preview)!;
        Assert.Null(mainEntry.PreviewOf);
        Assert.Equal("axis-endpoints", mainEntry.Oidc!.EndpointAudience);
        Assert.Equal("axis-services", mainEntry.Oidc.ServiceAudience);
        Assert.Equal(main, previewEntry.PreviewOf);
        Assert.Equal(AdminScenario.RealmAuthority(main), previewEntry.Oidc!.Authority);
        Assert.Equal("axis", previewEntry.Oidc.ClientId);
        Assert.Null(previewEntry.Oidc.EndpointAudience);
        Assert.Null(previewEntry.Oidc.ServiceAudience);
        var mainRow = await scenario.RowAsync(main);
        Assert.Equal(AdminScenario.Cipher.Decrypt(main, TenantSecretFields.OidcClientSecret, mainRow.OidcClientSecret).Reveal(), previewEntry.Oidc.ClientSecret!.Reveal());
    }

    [Fact]
    public async Task A_preview_of_a_preview_a_second_preview_of_one_tenant_a_preview_of_itself_and_a_preview_of_an_unknown_tenant_exit_2_and_change_nothing()
    {
        await using var scenario = await NewScenarioAsync();
        var main = scenario.NewTenantId();
        var preview = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(main, "main.example.test")).Exit);
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.PreviewArgs(preview, main, "preview.example.test"), withClientSecret: false)).Exit);
        var before = await scenario.SnapshotAsync();
        var entries = await scenario.CountEntriesAsync();

        var ofPreview = await scenario.RunAsync(AdminScenario.PreviewArgs(scenario.NewTenantId(), preview, "a.example.test"), withClientSecret: false);
        var second = await scenario.RunAsync(AdminScenario.PreviewArgs(scenario.NewTenantId(), main, "b.example.test"), withClientSecret: false);
        var unknown = await scenario.RunAsync(AdminScenario.PreviewArgs(scenario.NewTenantId(), "nobody", "c.example.test"), withClientSecret: false);
        var itself = await scenario.RunAsync(AdminScenario.PreviewArgs(main, main, "d.example.test"), withClientSecret: false);

        Assert.All([ofPreview, second, unknown, itself], result =>
        {
            Assert.Equal(2, result.Exit);
            Assert.Empty(result.Output);
            Assert.NotEmpty(result.Error);
        });
        Assert.Contains(preview, Assert.Single(ofPreview.Error), StringComparison.Ordinal);
        Assert.Contains(main, Assert.Single(second.Error), StringComparison.Ordinal);
        Assert.Contains("nobody", Assert.Single(unknown.Error), StringComparison.Ordinal);
        Assert.Equal(before, await scenario.SnapshotAsync());
        Assert.Equal(entries, await scenario.CountEntriesAsync());
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_binding_without_preview_of_an_oidc_option_with_preview_of_and_a_bad_binding_exit_2_and_print_no_binding_value()
    {
        await using var scenario = await NewScenarioAsync();
        var main = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(main, "main.example.test")).Exit);
        var before = await scenario.SnapshotAsync();
        var id = scenario.NewTenantId();

        var withoutPreview = await scenario.RunAsync([.. AdminScenario.CreateArgs(id, "a.example.test"), "--binding", $"Name={BindingValue}"]);
        var withOidcClient = await scenario.RunAsync([.. AdminScenario.PreviewArgs(id, main, "b.example.test"), "--oidc-client-id", "axis"]);
        var withOidcAuthority = await scenario.RunAsync([.. AdminScenario.PreviewArgs(id, main, "b.example.test"), "--oidc-authority", AdminScenario.Authority]);
        var notAUrl = await scenario.RunAsync(AdminScenario.PreviewArgs(id, main, "c.example.test", $"Name={BindingValue}"), withClientSecret: false);
        var notHttp = await scenario.RunAsync(AdminScenario.PreviewArgs(id, main, "c.example.test", "Name=ftp://files.example.test/SECRET123"), withClientSecret: false);
        var noSeparator = await scenario.RunAsync(AdminScenario.PreviewArgs(id, main, "c.example.test", BindingValue), withClientSecret: false);
        var repeated = await scenario.RunAsync(
            AdminScenario.PreviewArgs(id, main, "c.example.test", "Name=https://one.example.test/SECRET123", "NAME=https://two.example.test/SECRET123"),
            withClientSecret: false);

        var results = new[] { withoutPreview, withOidcClient, withOidcAuthority, notAUrl, notHttp, noSeparator, repeated };
        Assert.All(results, result =>
        {
            Assert.Equal(2, result.Exit);
            Assert.Empty(result.Output);
            Assert.NotEmpty(result.Error);
            Assert.All([.. result.Output, .. result.Error], line => Assert.DoesNotContain("SECRET123", line, StringComparison.Ordinal));
        });
        Assert.Contains("'Name'", Assert.Single(notAUrl.Error), StringComparison.Ordinal);
        Assert.Equal(before, await scenario.SnapshotAsync());
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task List_shows_the_main_tenants_id_in_the_preview_of_column_of_the_preview_tenant_and_a_dash_for_the_main_tenant()
    {
        await using var scenario = await NewScenarioAsync();
        var ids = new[] { scenario.NewTenantId(), scenario.NewTenantId() };
        var main = ids[0];
        var preview = ids[1];
        Assert.Equal(0, (await scenario.CreateAsync(main, "main.example.test")).Exit);
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.PreviewArgs(preview, main, "preview.example.test"), withClientSecret: false)).Exit);

        var result = await scenario.RunAsync(["tenant", "list"]);

        Assert.Equal(0, result.Exit);
        Assert.Equal(["id", "status", "hosts", "schema version", "preview of"], Cells(result.Output[0]));
        Assert.Equal("-", Cells(result.Output.Single(line => line.StartsWith(main, StringComparison.Ordinal)))[^1]);
        Assert.Equal(main, Cells(result.Output.Single(line => line.StartsWith(preview, StringComparison.Ordinal)))[^1]);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Activate_on_a_preview_tenant_without_bindings_exits_0_and_writes_an_AXC0100_warning()
    {
        await using var scenario = await NewScenarioAsync();
        var main = scenario.NewTenantId();
        var preview = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(main, "main.example.test")).Exit);
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.PreviewArgs(preview, main, "preview.example.test"), withClientSecret: false)).Exit);

        var result = await scenario.RunAsync(["activate", _sample, "--tenant", preview]);

        Assert.Equal(0, result.Exit);
        Assert.Equal(_activateSteps.Select(step => $"{preview} {step} ok"), result.Output);
        Assert.Contains(result.Error, line => line.StartsWith($"{preview} warning AXC0100 ", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Error, line => !line.StartsWith($"{preview} warning ", StringComparison.Ordinal));

        // The main tenant has no such mercy: a missing binding fails its activation.
        var onMain = await scenario.RunAsync(["activate", _sample, "--tenant", main]);
        Assert.Equal(1, onMain.Exit);
        await scenario.AssertNoSecretsAsync();
    }

    private static string[] Cells(string line) => line.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private Task<AdminScenario> NewScenarioAsync() => AdminScenario.NewAsync(database, migrate: true, CancellationToken);

    /// <summary>Signs in through the server and Keycloak's login form, as a browser would.</summary>
    private static async Task SignInAsync(HttpClient client, Uri authority, string username)
    {
        var challenge = await client.GetAsync("/api/auth/sign-in", CancellationToken);
        Assert.Equal(HttpStatusCode.Found, challenge.StatusCode);
        var authorize = challenge.Headers.Location!;
        Assert.StartsWith($"{authority}/protocol/openid-connect/auth?", authorize.AbsoluteUri, StringComparison.Ordinal);

        // A separate client plays the browser at Keycloak, which marks its cookies Secure. See SignInTests.
        using var browserHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var browser = new HttpClient(browserHandler);
        var loginPage = await browser.GetAsync(authorize, CancellationToken);
        var html = await loginPage.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(loginPage.StatusCode == HttpStatusCode.OK, html);
        var action = WebUtility.HtmlDecode(FormActionPattern().Match(html).Groups["action"].Value);
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(action))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = KeycloakFixture.Password,
                ["credentialId"] = "",
            }),
        };
        var cookieHeader = string.Join("; ", loginPage.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0]));
        loginRequest.Headers.Add("Cookie", cookieHeader);
        var password = await browser.SendAsync(loginRequest, CancellationToken);
        var login = await KeycloakSignIn.SubmitCodeAsync(browser, password, cookieHeader, username);
        Assert.True(login.StatusCode == HttpStatusCode.Found, await login.Content.ReadAsStringAsync(CancellationToken));
        var callback = login.Headers.Location!;
        Assert.Equal("http://localhost:5206/api/auth/callback", callback.GetLeftPart(UriPartial.Path));

        var signedIn = await client.GetAsync(callback.PathAndQuery, CancellationToken);
        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        Assert.Contains(signedIn.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith($"{Session.Scheme}=", StringComparison.Ordinal));
    }

    [GeneratedRegex("""<form\b[^>]*\baction="(?<action>[^"]+)""")]
    private static partial Regex FormActionPattern();
}
