using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// A person who signs in through a brokered provider gets, in each active application, the roles that the application's
/// <c>roles</c> mapping gives, and only that application's policies see them. Two applications read the same <c>groups</c> claim.
/// <c>RolesA</c> maps the value <c>purchasing-managers</c> to its role <c>buyer</c>, and <c>RolesB</c> maps no value, so the same
/// value gives no role there. Both grant <c>read</c> on <c>Note</c> to <c>buyer</c>. The test rewrites the ID token that
/// Keycloak returns, so it carries the <c>identity_provider</c> claim and the roles attribute as the realm sync gives them.
/// </summary>
public sealed class ApplicationRolesSignInTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string CompanyIssuer = "CompanyIssuer";
    private const string CompanyClientId = "CompanyClientId";
    private const string CompanySecret = "CompanySecret";

    private static readonly Guid _rolesAId = Guid.Parse("5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4b01");
    private static readonly Guid _rolesBId = Guid.Parse("5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4b02");

    // The realm's client accepts redirects to http://localhost:5206/*, the development server.
    private static readonly Uri _address = new("http://localhost:5206");

    private string? _schemaConnection;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        _schemaConnection = await database.CreateDatabaseAsync("applicationroles", CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_brokered_person_holds_the_mapped_role_in_the_application_that_maps_it_only()
    {
        using var handler = new IdTokenRewriteHandler(editIdToken: claims =>
        {
            claims["identity_provider"] = "oidc-companyissuer";
            claims["axis_roles_groups"] = new JsonArray(JsonValue.Create("purchasing-managers"));
        });
        using var rolesA = Application("RolesA", _rolesAId, "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4b21", """{ "claim": "groups", "values": { "purchasing-managers": "buyer" } }""");
        using var rolesB = Application("RolesB", _rolesBId, "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4b22", """{ "claim": "groups" }""");
        var authority = await keycloak.StartAsync();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(_schemaConnection!);
            builder.UseSetting("Tenants:a:Hosts:0", "localhost");
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_schemaConnection!));
            builder.UseSetting("Tenants:a:SchemaConnectionString", _schemaConnection!);
            builder.UseSetting("Tenants:a:Oidc:Authority", authority.ToString());
            builder.UseSetting("Tenants:a:Oidc:ClientId", KeycloakFixture.ClientId);
            builder.UseSetting("Tenants:a:Oidc:ClientSecret", KeycloakFixture.ClientSecret);
            builder.UseSetting($"Tenants:a:Bindings:{CompanyIssuer}:Kind", "url");
            builder.UseSetting($"Tenants:a:Bindings:{CompanyIssuer}:Value", authority.AbsoluteUri);
            builder.UseSetting($"Tenants:a:Bindings:{CompanyClientId}:Kind", "secret");
            builder.UseSetting($"Tenants:a:Bindings:{CompanyClientId}:Value", "company-client-value");
            builder.UseSetting($"Tenants:a:Bindings:{CompanySecret}:Kind", "secret");
            builder.UseSetting($"Tenants:a:Bindings:{CompanySecret}:Value", "company-secret-value");
            builder.UseSetting("ActivateOnStartup:0", rolesA.Path);
            builder.UseSetting("ActivateOnStartup:1", rolesB.Path);
            builder.ConfigureTestServices(services =>
                services.Configure<OpenIdConnectOptions>(TenantSignIn.SchemeName("a"), options => options.BackchannelHttpHandler = handler));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = _address,
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        var callback = await KeycloakSignIn.LogInAsync(client, authority, "requester", _address);
        using var signedIn = await client.GetAsync(callback.PathAndQuery, CancellationToken);

        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        var stored = JsonNode.Parse((string)(await PostgreSqlServer.AdminScalarAsync(
            _schemaConnection!, "SELECT application_roles::text FROM axis.sessions", CancellationToken))!)!.AsObject();
        Assert.Equal([_rolesAId.ToString("D")], stored.Select(pair => pair.Key));
        Assert.Equal(["buyer"], stored[_rolesAId.ToString("D")]!.AsArray().Select(role => role!.GetValue<string>()));
        using var inA = await client.GetAsync("/api/apps/RolesA/entities/Note/records", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, inA.StatusCode);
        using var inB = await client.GetAsync("/api/apps/RolesB/entities/Note/records", CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, inB.StatusCode);
        using var me = await client.GetAsync("/api/me", CancellationToken);
        using var document = JsonDocument.Parse(await me.Content.ReadAsStringAsync(CancellationToken));
        Assert.DoesNotContain(document.RootElement.GetProperty("roles").EnumerateArray(), role => role.GetString() == "buyer");
    }

    /// <summary>An application with an <c>accounts</c> provider, the <c>company</c> provider with <paramref name="roles"/>, the role <c>buyer</c> and a policy that lets it read notes.</summary>
    private static TemporaryFolder Application(string name, Guid id, string entityId, string roles) => new TemporaryFolder()
        .With("application.json", $$"""{ "id": "{{id}}", "kind": "application", "name": "{{name}}", "formatVersion": 1 }""")
        .With("texts/en.json", """
            { "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4b10", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
              "texts": { "signIn.company": "Continue with Company", "role.buyer": "Buyer" } }
            """)
        .With("identity-providers/accounts.json", """{ "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4b11", "kind": "identityProvider", "name": "accounts", "formatVersion": 1, "type": "accounts" }""")
        .With("identity-providers/company.json", $$"""
            { "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4b12", "kind": "identityProvider", "name": "company", "formatVersion": 1, "type": "oidc",
              "label": { "textKey": "signIn.company" },
              "issuer": { "binding": "{{CompanyIssuer}}" }, "clientId": { "binding": "{{CompanyClientId}}" },
              "clientSecret": { "binding": "{{CompanySecret}}" }, "roles": {{roles}} }
            """)
        .With("roles/buyer.json", """{ "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4b13", "kind": "role", "name": "buyer", "formatVersion": 1, "label": { "textKey": "role.buyer" } }""")
        .With("entities/note.json", $$"""
            { "id": "{{entityId}}", "kind": "entity", "name": "Note", "formatVersion": 1, "displayField": "title",
              "fields": [ { "name": "title", "type": "text", "required": true } ] }
            """)
        .With("policies/buyer-access.json", """
            { "id": "5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4b15", "kind": "policy", "name": "BuyerAccess", "formatVersion": 1,
              "roles": ["buyer"], "grants": [ { "entity": "Note", "actions": ["read"], "fields": ["title"] } ] }
            """);
}
