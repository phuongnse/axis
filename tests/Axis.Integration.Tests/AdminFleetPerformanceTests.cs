using System.Diagnostics;
using System.Globalization;
using Axis.Processes.Storage;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>migrate</c> over 20 tenant databases, with one and with four tenants at a time, and <c>activate</c> of the purchase
/// request sample over the same 20 tenants each finish within the time limit and report every tenant done.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class AdminFleetPerformanceTests(AdminFleetFixture fixture) : IClassFixture<AdminFleetFixture>
{
    private const string OldVersion = "20000101000000_Old";

    private static readonly string[] _steps = ["version", "compile", "activate", "seed"];

    // A tenant that is marked as on an old schema runs every migrate step, but its database has no EF Core migration left
    // to apply. The 20 tenants took about 2.6 s with --parallel 1 and about 1.5 s with --parallel 4 when measured. The
    // bound is more than ten times the slower time, so shared CI runners do not make the test flaky. It still catches a
    // step that costs a connection pool or a migration run per tenant. The test does not check that --parallel 4 is
    // faster, because that would be flaky on a shared runner.
    private static readonly TimeSpan _migrateTime = TimeSpan.FromSeconds(30);

    // Activate compiles the sample for each tenant, activates it and seeds it, one tenant at a time. The 20 tenants took
    // about 2.3 s when measured. The bound is more than ten times that, so shared CI runners do not make the test flaky.
    // It still catches an activation that costs seconds per tenant, such as a compile that repeats its work.
    private static readonly TimeSpan _activateTime = TimeSpan.FromSeconds(30);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Migrate_over_20_tenants_exits_0_within_the_time_limit_and_leaves_every_tenant_on_the_current_schema(int parallel)
    {
        var scenario = fixture.Scenario;
        foreach (var id in fixture.Ids)
        {
            await scenario.SetSchemaVersionAsync(id, OldVersion);
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await scenario.RunAsync(["migrate", "--parallel", parallel.ToString(CultureInfo.InvariantCulture)]);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"Migrate with --parallel {parallel} took {stopwatch.Elapsed}.");
        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.True(stopwatch.Elapsed < _migrateTime, $"Migrate with --parallel {parallel} took {stopwatch.Elapsed}.");
        foreach (var id in fixture.Ids)
        {
            Assert.Equal(TenantSchemaVersion.Current, (await scenario.RowAsync(id)).SchemaVersion);
        }
    }

    [Fact]
    public async Task Activate_of_the_purchase_request_sample_over_20_tenants_exits_0_within_the_time_limit_and_reports_every_step_ok()
    {
        var scenario = fixture.Scenario;
        var sample = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

        var stopwatch = Stopwatch.StartNew();
        var result = await scenario.RunAsync(["activate", sample]);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"Activate over {fixture.Ids.Length} tenants took {stopwatch.Elapsed}.");
        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.True(stopwatch.Elapsed < _activateTime, $"Activate over {fixture.Ids.Length} tenants took {stopwatch.Elapsed}.");
        Assert.Equal(fixture.Ids.SelectMany(id => _steps.Select(step => $"{id} {step} ok")), result.Output);
    }
}

/// <summary>
/// One platform database with 20 active tenants made by <c>tenant create</c>, each with the bindings that the purchase request
/// sample needs, so <c>activate</c> can run on all of them.
/// </summary>
public sealed class AdminFleetFixture : IAsyncLifetime
{
    private const int Tenants = 20;
    private const string UrlBinding = "PurchasingSystemUrl";
    private const string SecretBinding = "PurchasingSystemToken";

    private readonly PostgreSqlFixture _database = new();

    /// <summary>The tenant ids in the order <c>migrate</c> and <c>activate</c> run them.</summary>
    public string[] Ids { get; private set; } = [];

    internal AdminScenario Scenario { get; private set; } = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        Scenario = await AdminScenario.NewAsync(_database, migrate: true, CancellationToken);
        Ids = [.. Enumerable.Range(0, Tenants).Select(_ => Scenario.NewTenantId()).Order(StringComparer.Ordinal)];
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(Scenario.Platform).Options);
        foreach (var id in Ids)
        {
            Assert.Equal(0, (await Scenario.CreateAsync(id, $"{id}.example.test")).Exit);
            context.TenantBindings.AddRange(
                new TenantBindingRow { TenantId = id, Name = UrlBinding, Kind = "url", Url = "https://purchasing.example.test" },
                new TenantBindingRow
                {
                    TenantId = id,
                    Name = SecretBinding,
                    Kind = "secret",
                    SecretValue = AdminScenario.Cipher.Encrypt(id, TenantSecretFields.Binding(SecretBinding), "binding-secret-value"),
                });
        }

        await context.SaveChangesAsync(CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Scenario is not null)
        {
            await Scenario.DisposeAsync();
        }

        await _database.DisposeAsync();
    }
}
