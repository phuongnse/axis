using System.Text.Json;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant sign-in set</c> stores the platform sign-in of a directory tenant: whether realm accounts are allowed and the
/// brokered providers that are accepted. A run replaces the whole platform sign-in.
/// </summary>
public sealed class AdminTenantSignInSetTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static readonly Dictionary<string, string?> _directory = new() { ["Tenancy:Source"] = "directory" };

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_providers_and_accounts_are_stored_a_second_run_replaces_them_and_the_record_names_the_settings_and_no_value()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        await SetBindingsAsync(scenario, id, "CompanyIssuer", "CompanyClientId", "CompanySecret");
        await SetBindingsAsync(scenario, id, "I", "C", "S");
        var before = await scenario.RowAsync(id);

        var first = await scenario.RunAsync(
            [
                "tenant", "sign-in", "set", id, "--accounts", "true",
                "--provider", "company", "--type", "oidc", "--issuer", "CompanyIssuer", "--client-id", "CompanyClientId", "--client-secret", "CompanySecret",
                "--claim", "email=mail", "--claim", "displayName=full_name", "--roles-claim", "groups",
                "--role", "axis-admins=administrator", "--role", "ops-team=operator",
                "--provider", "partner", "--type", "saml", "--metadata", "PartnerMetadata",
            ],
            extra: _directory);

        Assert.Equal(0, first.Exit);
        Assert.Equal([$"{id} sign-in ok"], first.Output);
        Assert.Empty(first.Error);
        var row = await scenario.RowAsync(id);
        Assert.True(row.PlatformAccounts);
        Assert.Equal(before.Version + 1, row.Version);
        var providers = await scenario.PlatformProvidersAsync(id);
        Assert.Equal(["company", "partner"], providers.Select(provider => provider.Name));
        var company = providers[0];
        Assert.Equal("oidc", company.Type);
        Assert.Equal(("CompanyIssuer", "CompanyClientId", "CompanySecret", null, null), (company.IssuerBinding, company.ClientIdBinding, company.ClientSecretBinding, company.ClientKeyBinding, company.MetadataBinding));
        Assert.Equal(("full_name", "mail", null, "groups"), (company.ClaimDisplayName, company.ClaimEmail, company.ClaimLocale, company.RolesClaim));
        Assert.Equal(
            new Dictionary<string, string> { ["axis-admins"] = "administrator", ["ops-team"] = "operator" },
            JsonSerializer.Deserialize<Dictionary<string, string>>(company.RoleValues));
        var partner = providers[1];
        Assert.Equal(("saml", "PartnerMetadata", "{}"), (partner.Type, partner.MetadataBinding, partner.RoleValues));
        var records = await scenario.AuditRecordsAsync();
        var record = Assert.Single(records, record => record.Command == "tenant sign-in set");
        Assert.Equal(id, record.TenantId);
        Assert.Equal(["accounts", "provider:company", "provider:partner"], record.Changed);
        string[] values = ["CompanyIssuer", "CompanyClientId", "CompanySecret", "PartnerMetadata", "full_name", "groups", "ops-team", "operator"];
        Assert.All(values, value => Assert.DoesNotContain(value, record.Text, StringComparison.Ordinal));

        var second = await scenario.RunAsync(
            ["tenant", "sign-in", "set", id, "--accounts", "false", "--provider", "other", "--type", "oidc", "--issuer", "I", "--client-id", "C", "--client-secret", "S"],
            extra: _directory);

        Assert.Equal(0, second.Exit);
        var replaced = await scenario.RowAsync(id);
        Assert.False(replaced.PlatformAccounts);
        Assert.Equal(row.Version + 1, replaced.Version);
        var remaining = Assert.Single(await scenario.PlatformProvidersAsync(id));
        Assert.Equal(("other", "S", null, "{}"), (remaining.Name, remaining.ClientSecretBinding, remaining.ClientKeyBinding, remaining.RoleValues));
        Assert.Equal(["accounts", "provider:other"], (await scenario.AuditRecordsAsync())[^1].Changed);

        // The same provider name again is a replacement, not a clash.
        var third = await scenario.RunAsync(
            ["tenant", "sign-in", "set", id, "--accounts", "true", "--provider", "other", "--type", "saml", "--metadata", "M"],
            extra: _directory);

        Assert.Equal(0, third.Exit);
        Assert.Equal("saml", Assert.Single(await scenario.PlatformProvidersAsync(id)).Type);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_platform_provider_naming_a_binding_the_tenant_lacks_exits_with_2_and_stores_nothing()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", id, "CompanyIssuer", "--url", "https://idp.example.test/realms/company"], extra: _directory)).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", id, "WrongKind", "--url", "https://idp.example.test/"], extra: _directory)).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", id, "CompanyClientId", "--secret"], input: "client-id-value", extra: _directory)).Exit);
        var version = (await scenario.RowAsync(id)).Version;
        var snapshot = await scenario.SnapshotAsync();
        var records = (await scenario.AuditRecordsAsync()).Count;
        var requests = scenario.IdentityProvider.Paths.Count;
        string[] oidc = ["tenant", "sign-in", "set", id, "--accounts", "true", "--provider", "company", "--type", "oidc"];

        var refusals = new[]
        {
            // CompanySecret is missing.
            await scenario.RunAsync([.. oidc, "--issuer", "CompanyIssuer", "--client-id", "CompanyClientId", "--client-secret", "CompanySecret"], extra: _directory),
            // WrongKind is a url binding, not a secret.
            await scenario.RunAsync([.. oidc, "--issuer", "CompanyIssuer", "--client-id", "CompanyClientId", "--client-secret", "WrongKind"], extra: _directory),
            // The realm needs a client secret, and a key is not enough.
            await scenario.RunAsync([.. oidc, "--issuer", "CompanyIssuer", "--client-id", "CompanyClientId", "--client-key", "CompanyClientId"], extra: _directory),
        };

        Assert.All(refusals, refusal =>
        {
            Assert.Equal(2, refusal.Exit);
            Assert.Empty(refusal.Output);
            Assert.NotEmpty(refusal.Error);
        });
        Assert.Contains("'CompanySecret'", refusals[0].Error[0], StringComparison.Ordinal);
        Assert.Contains("'WrongKind'", refusals[1].Error[0], StringComparison.Ordinal);
        Assert.Contains("--client-secret", refusals[2].Error[0], StringComparison.Ordinal);
        Assert.Equal(version, (await scenario.RowAsync(id)).Version);
        Assert.Empty(await scenario.PlatformProvidersAsync(id));
        Assert.Equal(snapshot, await scenario.SnapshotAsync());
        Assert.Equal(records, (await scenario.AuditRecordsAsync()).Count);
        Assert.Equal(requests, scenario.IdentityProvider.Paths.Count);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task No_provider_with_accounts_off_exits_with_2_and_changes_neither_the_directory_nor_the_audit_records()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var version = (await scenario.RowAsync(id)).Version;
        var snapshot = await scenario.SnapshotAsync();
        var records = (await scenario.AuditRecordsAsync()).Count;

        var result = await scenario.RunAsync(["tenant", "sign-in", "set", id, "--accounts", "false"], extra: _directory);

        Assert.Equal(2, result.Exit);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
        Assert.Equal(version, (await scenario.RowAsync(id)).Version);
        Assert.Equal(snapshot, await scenario.SnapshotAsync());
        Assert.Equal(records, (await scenario.AuditRecordsAsync()).Count);
    }

    [Fact]
    public async Task A_refused_set_exits_with_2_and_changes_neither_the_directory_nor_the_audit_records()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var preview = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.PreviewArgs(preview, id, $"{preview}.example.test"))).Exit);
        var snapshot = await scenario.SnapshotAsync();
        var records = (await scenario.AuditRecordsAsync()).Count;
        string[] oidc = ["--type", "oidc", "--issuer", "I", "--client-id", "C"];

        var refusals = new[]
        {
            await scenario.RunAsync(["tenant", "sign-in", "set", id, "--accounts", "true", "--provider", "a", .. oidc, "--client-secret", "S", "--provider", "A", "--type", "saml", "--metadata", "M"], extra: _directory),
            await scenario.RunAsync(["tenant", "sign-in", "set", id, "--accounts", "true", "--provider", "a", "--type", "ldap"], extra: _directory),
            await scenario.RunAsync(["tenant", "sign-in", "set", id, "--accounts", "true", "--provider", "a", "--type", "accounts"], extra: _directory),
            await scenario.RunAsync(["tenant", "sign-in", "set", id, "--accounts", "true", "--provider", "a", .. oidc], extra: _directory),
            await scenario.RunAsync(["tenant", "sign-in", "set", id, "--accounts", "true", "--provider", "a", .. oidc, "--client-secret", "S", "--client-key", "K"], extra: _directory),
            await scenario.RunAsync(["tenant", "sign-in", "set", id, "--accounts", "true", "--provider", "a", .. oidc, "--client-secret", "S", "--role", "x=manager"], extra: _directory),
            await scenario.RunAsync(["tenant", "sign-in", "set", id, "--accounts", "true", "--provider", "a", .. oidc, "--client-secret", "S", "--claim", "nickname=nick"], extra: _directory),
            await scenario.RunAsync(["tenant", "sign-in", "set", id, "--accounts", "true", "--provider", "a", "--type", "saml", "--metadata", "M", "--issuer", "I"], extra: _directory),
            await scenario.RunAsync(["tenant", "sign-in", "set", preview, "--accounts", "true"], extra: _directory),
            await scenario.RunAsync(["tenant", "sign-in", "set", "t-unknown", "--accounts", "true"], extra: _directory),
            await scenario.RunAsync(["tenant", "sign-in", "set", id, "--accounts", "true"]),
            await scenario.RunAsync(["tenant", "sign-in", "set", id], extra: _directory),
            await scenario.RunAsync(["tenant", "sign-in", "set", id, "--accounts", "true", "--accounts", "true"], extra: _directory),
            await scenario.RunAsync(["tenant", "sign-in", "set", id, "--accounts", "true", "--type", "oidc"], extra: _directory),
        };

        Assert.All(refusals, refusal =>
        {
            Assert.Equal(2, refusal.Exit);
            Assert.Empty(refusal.Output);
            Assert.NotEmpty(refusal.Error);
        });
        Assert.Contains("preview tenant", refusals[8].Error[0], StringComparison.Ordinal);
        Assert.Equal(snapshot, await scenario.SnapshotAsync());
        Assert.Equal(records, (await scenario.AuditRecordsAsync()).Count);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Tenant_create_leaves_the_new_tenant_with_accounts_on_and_no_platform_providers()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();

        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);

        Assert.True((await scenario.RowAsync(id)).PlatformAccounts);
        Assert.Empty(await scenario.PlatformProvidersAsync(id));
    }

    private static async Task SetBindingsAsync(AdminScenario scenario, string id, string issuer, string clientId, string clientSecret)
    {
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", id, issuer, "--url", "https://idp.example.test/realms/company"], extra: _directory)).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", id, clientId, "--secret"], input: $"{clientId}-value-{id}", extra: _directory)).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", id, clientSecret, "--secret"], input: $"{clientSecret}-value-{id}", extra: _directory)).Exit);
    }
}
