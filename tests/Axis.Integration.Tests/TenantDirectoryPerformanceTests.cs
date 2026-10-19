using System.Diagnostics;
using System.Net;
using Axis.Processes.Storage;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A directory of 200 tenants resolves a thousand requests within the time limit and runs no platform database query for
/// them. A refresh that admits 199 new tenants, and a refresh that changes nothing, each finish within their time limit.
/// </summary>
public sealed class TenantDirectoryPerformanceTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const int NewTenants = 199;
    private const int Rounds = 5;

    // A request finds its tenant in the snapshot that the last refresh built, so a thousand requests took about 1.4 s when
    // measured (the session and the record API cost, not the lookup). The bound is ten times that, so shared CI runners do
    // not make the test flaky. It still catches a lookup that scans the directory or a request that waits for the
    // platform database.
    private static readonly TimeSpan _resolveTime = TimeSpan.FromSeconds(15);

    // A refresh that admits tenants reads the directory and checks the runtime role of each new tenant, and it took about
    // 4.7 s for 199 tenants when measured. The bound is more than ten times that, so shared CI runners do not make the
    // test flaky. It still catches a refresh that adds a round trip or a connection per host.
    private static readonly TimeSpan _admitTime = TimeSpan.FromMinutes(1);

    // A refresh that changes nothing reads the directory and compares it with the snapshot, and it took about 5 ms when
    // measured. The bound is far above that, so shared CI runners do not make the test flaky. It still catches a refresh
    // that checks every tenant again, which costs seconds, as the admitting refresh shows.
    private static readonly TimeSpan _idleTime = TimeSpan.FromSeconds(1);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_thousand_requests_over_two_hundred_tenant_hosts_resolve_within_the_time_limit_and_run_no_platform_query()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var hosts = await CreateDirectoryAsync(scenario);
        var platformName = new NpgsqlConnectionStringBuilder(scenario.Platform).Database!;
        using var spans = new SpanCollector();
        await using var factory = CreateServer(scenario.Platform);
        using var client = factory.CreateClient().SignedInAs("anna");

        // The control: a refresh does query the platform database, so the span check can fail.
        await factory.Services.GetRequiredService<TenantDirectoryRefresh>().RefreshAsync(CancellationToken);
        var before = PlatformSpans(spans, platformName);
        Assert.True(before > 0, "A directory refresh produced no span that names the platform database.");

        var failed = 0;
        var stopwatch = Stopwatch.StartNew();
        for (var round = 0; round < Rounds; round++)
        {
            foreach (var host in hosts)
            {
                using var response = await client.SendAsync(Request("/api/me", host), CancellationToken);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    failed++;
                }
            }
        }

        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"{Rounds * hosts.Length} requests took {stopwatch.Elapsed}.");
        Assert.Equal(0, failed);
        Assert.True(stopwatch.Elapsed < _resolveTime, $"{Rounds * hosts.Length} requests took {stopwatch.Elapsed}.");
        Assert.Equal(before, PlatformSpans(spans, platformName));
    }

    [Fact]
    public async Task A_refresh_that_admits_199_tenants_finishes_within_the_time_limit_and_a_refresh_that_changes_nothing_within_a_smaller_one()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var seed = await CreateSeedAsync(scenario);
        await using var factory = CreateServer(scenario.Platform);
        using var client = factory.CreateClient().SignedInAs("anna");
        var refresh = factory.Services.GetRequiredService<TenantDirectoryRefresh>();
        var hosts = await AddTenantsAsync(scenario, seed);

        var stopwatch = Stopwatch.StartNew();
        await refresh.RefreshAsync(CancellationToken);
        stopwatch.Stop();
        var admitted = stopwatch.Elapsed;

        TestContext.Current.TestOutputHelper?.WriteLine($"The refresh that admits {NewTenants} tenants took {admitted}.");
        Assert.True(admitted < _admitTime, $"The refresh that admits {NewTenants} tenants took {admitted}.");
        foreach (var host in new[] { hosts[0], hosts[^1] })
        {
            using var response = await client.SendAsync(Request("/api/me", host), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        stopwatch.Restart();
        await refresh.RefreshAsync(CancellationToken);
        stopwatch.Stop();
        var idle = stopwatch.Elapsed;

        TestContext.Current.TestOutputHelper?.WriteLine($"The refresh that changes nothing took {idle}.");
        Assert.True(idle < _idleTime, $"The refresh that changes nothing took {idle}.");
    }

    // A server on the directory. An hour, so the timer never races the refresh that the test triggers itself.
    private static WebApplicationFactory<Program> CreateServer(string platform) =>
        AdminScenario.CreateServer(platform).WithWebHostBuilder(builder => builder.UseSetting("Tenancy:RefreshInterval", "01:00:00"));

    // One tenant made by `tenant create`, which the other tenants copy their database and roles from.
    private static async Task<TenantRow> CreateSeedAsync(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, Host(id))).Exit);
        return await scenario.RowAsync(id);
    }

    // The seed tenant and 199 new tenants, and the 200 hosts in id order.
    private static async Task<string[]> CreateDirectoryAsync(AdminScenario scenario)
    {
        var seed = await CreateSeedAsync(scenario);
        return [Host(seed.Id), .. await AddTenantsAsync(scenario, seed)];
    }

    // Adds 199 active tenants on the seed tenant's database and runtime role. Each has its own id, host and secrets, which
    // are encrypted for that id. Resolution and refresh cost depend on the directory rows, not on separate databases.
    private static async Task<string[]> AddTenantsAsync(AdminScenario scenario, TenantRow seed)
    {
        var runtime = AdminScenario.Cipher.Decrypt(seed.Id, TenantSecretFields.RuntimePassword, seed.RuntimePassword).Reveal();
        var schema = AdminScenario.Cipher.Decrypt(seed.Id, TenantSecretFields.SchemaPassword, seed.SchemaPassword).Reveal();
        var ids = Enumerable.Range(0, NewTenants).Select(n => $"p-{n:D3}").ToArray();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options);
        foreach (var id in ids)
        {
            context.Tenants.Add(new TenantRow
            {
                Id = id,
                Status = "active",
                SchemaVersion = TenantSchemaVersion.Current,
                DatabaseHost = seed.DatabaseHost,
                DatabasePort = seed.DatabasePort,
                DatabaseName = seed.DatabaseName,
                RuntimeUser = seed.RuntimeUser,
                SchemaUser = seed.SchemaUser,
                RuntimePassword = AdminScenario.Cipher.Encrypt(id, TenantSecretFields.RuntimePassword, runtime),
                SchemaPassword = AdminScenario.Cipher.Encrypt(id, TenantSecretFields.SchemaPassword, schema),
                OidcAuthority = AdminScenario.Authority,
                OidcClientId = "axis",
                OidcClientSecret = AdminScenario.Cipher.Encrypt(id, TenantSecretFields.OidcClientSecret, "test"),
            });
            context.TenantHosts.Add(new TenantHostRow { TenantId = id, Host = Host(id) });
        }

        await context.SaveChangesAsync(CancellationToken);
        return [.. ids.Select(Host)];
    }

    private static string Host(string id) => $"{id}.example.test";

    // The finished Npgsql spans that name the platform database.
    private static int PlatformSpans(SpanCollector spans, string platformName) =>
        spans.Spans.Count(span =>
            span.Source.Name == "Npgsql"
            && string.Equals((span.GetTagItem("db.namespace") ?? span.GetTagItem("db.name")) as string, platformName, StringComparison.Ordinal));
}
