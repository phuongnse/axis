using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant legal set</c> and <c>tenant legal clear</c> give a directory tenant, or one of its sites, its privacy and terms
/// addresses and the version of the terms, and remove them again.
/// </summary>
public sealed class AdminTenantLegalTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static readonly Dictionary<string, string?> _directory = new() { ["Tenancy:Source"] = "directory" };

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Set_stores_the_tenant_links_and_the_version_and_a_second_set_clears_what_it_leaves_out()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var before = await scenario.RowAsync(id);

        var result = await scenario.RunAsync(
            ["tenant", "legal", "set", id, "--privacy-url", "https://tenant.test/privacy", "--terms-url", " https://tenant.test/terms ", "--terms-version", "2026-01"],
            extra: _directory);

        Assert.Equal(0, result.Exit);
        Assert.Equal([$"{id} legal ok"], result.Output);
        Assert.Empty(result.Error);
        var row = await scenario.RowAsync(id);
        Assert.Equal("https://tenant.test/privacy", row.PrivacyUrl);
        Assert.Equal("https://tenant.test/terms", row.TermsUrl);
        Assert.Equal("2026-01", row.TermsVersion);
        Assert.Equal(before.Version + 1, row.Version);
        var records = await scenario.AuditRecordsAsync();
        var record = Assert.Single(records, record => record.Command == "tenant legal set");
        Assert.Equal(id, record.TenantId);
        Assert.Equal(["privacyUrl", "termsUrl", "termsVersion"], record.Changed);
        Assert.All(records, entry => Assert.DoesNotContain("tenant.test", entry.Text, StringComparison.Ordinal));

        // The second set replaces the whole block, so the terms it leaves out are gone.
        var second = await scenario.RunAsync(["tenant", "legal", "set", id, "--privacy-url", "https://tenant.test/privacy-2"], extra: _directory);

        Assert.Equal(0, second.Exit);
        var replaced = await scenario.RowAsync(id);
        Assert.Equal("https://tenant.test/privacy-2", replaced.PrivacyUrl);
        Assert.Null(replaced.TermsUrl);
        Assert.Null(replaced.TermsVersion);
        Assert.Equal(["privacyUrl"], (await scenario.AuditRecordsAsync())[^1].Changed);
        Assert.Empty(await SitesAsync(scenario));
    }

    [Fact]
    public async Task Set_with_a_site_stores_the_site_addresses_in_lower_case_and_leaves_the_tenant_block_alone()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var before = await scenario.RowAsync(id);

        var result = await scenario.RunAsync(
            ["tenant", "legal", "set", id, "--site", "Sales", "--privacy-url", "https://sales.test/privacy", "--terms-url", "https://sales.test/terms"],
            extra: _directory);

        Assert.Equal(0, result.Exit);
        Assert.Equal([$"{id} legal ok"], result.Output);
        var site = Assert.Single(await SitesAsync(scenario));
        Assert.Equal((id, "sales", "https://sales.test/privacy", "https://sales.test/terms"), (site.TenantId, site.SitePath, site.PrivacyUrl, site.TermsUrl));
        var row = await scenario.RowAsync(id);
        Assert.Null(row.PrivacyUrl);
        Assert.Null(row.TermsUrl);
        Assert.Null(row.TermsVersion);
        Assert.Equal(before.Version + 1, row.Version);
        var record = Assert.Single(await scenario.AuditRecordsAsync(), record => record.Command == "tenant legal set");
        Assert.Equal(["privacyUrl", "termsUrl", "site:sales"], record.Changed);

        // A second set for the same site replaces its row.
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "legal", "set", id, "--site", "SALES", "--terms-url", "https://sales.test/terms-2"], extra: _directory)).Exit);
        var replaced = Assert.Single(await SitesAsync(scenario));
        Assert.Null(replaced.PrivacyUrl);
        Assert.Equal("https://sales.test/terms-2", replaced.TermsUrl);
    }

    [Fact]
    public async Task Clear_empties_the_tenant_block_and_clear_with_a_site_removes_only_that_site()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "legal", "set", id, "--privacy-url", "https://tenant.test/privacy", "--terms-url", "https://tenant.test/terms", "--terms-version", "1"], extra: _directory)).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "legal", "set", id, "--site", "sales", "--privacy-url", "https://sales.test/privacy"], extra: _directory)).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "legal", "set", id, "--site", "support", "--privacy-url", "https://support.test/privacy"], extra: _directory)).Exit);
        var set = await scenario.RowAsync(id);

        var site = await scenario.RunAsync(["tenant", "legal", "clear", id, "--site", "Sales"], extra: _directory);

        Assert.Equal(0, site.Exit);
        Assert.Equal([$"{id} legal ok"], site.Output);
        Assert.Equal(["support"], (await SitesAsync(scenario)).Select(link => link.SitePath));
        var kept = await scenario.RowAsync(id);
        Assert.Equal("https://tenant.test/terms", kept.TermsUrl);
        Assert.Equal(set.Version + 1, kept.Version);
        var records = await scenario.AuditRecordsAsync();
        Assert.Equal(["legal", "site:sales"], Assert.Single(records, record => record.Command == "tenant legal clear").Changed);

        var tenant = await scenario.RunAsync(["tenant", "legal", "clear", id], extra: _directory);

        Assert.Equal(0, tenant.Exit);
        Assert.Equal([$"{id} legal ok"], tenant.Output);
        var cleared = await scenario.RowAsync(id);
        Assert.Null(cleared.PrivacyUrl);
        Assert.Null(cleared.TermsUrl);
        Assert.Null(cleared.TermsVersion);
        Assert.Equal(["support"], (await SitesAsync(scenario)).Select(link => link.SitePath));
        records = await scenario.AuditRecordsAsync();
        Assert.Equal(["legal"], records[^1].Changed);
        Assert.Equal(2, records.Count(record => record.Command == "tenant legal clear"));

        // Clearing what is already empty succeeds.
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "legal", "clear", id], extra: _directory)).Exit);
    }

    [Fact]
    public async Task A_refused_command_exits_with_2_and_changes_neither_the_tenant_nor_the_audit_records()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var provisioning = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        Assert.Equal(0, (await scenario.CreateAsync(provisioning, $"{provisioning}.example.test")).Exit);
        await scenario.SetStatusAsync(provisioning, "provisioning");
        var snapshot = await scenario.SnapshotAsync();
        var legal = await LegalAsync(scenario);
        var records = (await scenario.AuditRecordsAsync()).Count;
        const string privacy = "https://tenant.test/privacy";

        var refusals = new[]
        {
            await scenario.RunAsync(["tenant", "legal", "set", id, "--privacy-url", "http://tenant.test/privacy"], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "set", id, "--privacy-url", "/privacy"], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "set", id, "--terms-url", "https://tenant.test/terms"], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "set", id, "--terms-version", "1"], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "set", id, "--site", "sales", "--terms-url", "https://tenant.test/terms", "--terms-version", "1"], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "set", id, "--privacy-url", " "], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "set", id, "--privacy-url", privacy, "--terms-url", "https://tenant.test/terms", "--terms-version", " "], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "set", id, "--privacy-url", privacy, "--site", "api"], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "set", id, "--privacy-url", privacy, "--site", "not a path"], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "clear", id, "--site", "api"], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "set", "t-unknown", "--privacy-url", privacy], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "clear", "t-unknown"], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "set", provisioning, "--privacy-url", privacy], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "clear", provisioning], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "set", id], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "set", id, "--privacy-url", privacy, "--privacy-url", privacy], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "set", id, "--privacy-url"], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "set", id, "--unknown", "x"], extra: _directory),
            await scenario.RunAsync(["tenant", "legal", "clear", id, "--privacy-url", privacy], extra: _directory),
        };

        Assert.All(refusals, refusal =>
        {
            Assert.Equal(2, refusal.Exit);
            Assert.Empty(refusal.Output);
            Assert.NotEmpty(refusal.Error);
            Assert.DoesNotContain(refusal.Error, line => line.Contains("tenant.test", StringComparison.Ordinal));
        });
        Assert.Equal(snapshot, await scenario.SnapshotAsync());
        Assert.Equal(legal, await LegalAsync(scenario));
        Assert.Equal(records, (await scenario.AuditRecordsAsync()).Count);
    }

    private static async Task<List<TenantSiteLegalRow>> SitesAsync(AdminScenario scenario)
    {
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options);
        return await context.TenantSiteLegalLinks.AsNoTracking().OrderBy(link => link.TenantId).ThenBy(link => link.SitePath).ToListAsync(CancellationToken);
    }

    // The snapshot does not hold the legal fields, so they are compared on their own.
    private static async Task<string> LegalAsync(AdminScenario scenario)
    {
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options);
        var tenants = await context.Tenants.AsNoTracking().OrderBy(tenant => tenant.Id).ToListAsync(CancellationToken);
        var sites = await SitesAsync(scenario);
        return string.Join(
            '\n',
            tenants.Select(tenant => $"{tenant.Id}|{tenant.PrivacyUrl}|{tenant.TermsUrl}|{tenant.TermsVersion}")
                .Concat(sites.Select(site => $"{site.TenantId}|{site.SitePath}|{site.PrivacyUrl}|{site.TermsUrl}")));
    }
}
