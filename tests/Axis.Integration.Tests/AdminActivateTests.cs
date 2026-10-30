using System.Net;
using System.Text.RegularExpressions;
using Axis.Data.Naming;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class AdminActivateTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string UrlBinding = "PurchasingSystemUrl";
    private const string Site = "/api/sites/purchasing";
    private const string Departments = "/api/apps/PurchaseRequests/entities/Department/records";

    private static readonly string[] _steps = ["version", "compile", "activate", "seed", "indexes"];

    private static readonly string _sample = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Activate_serves_the_sample_with_its_seeds_on_every_tenant_host_and_names_no_secret()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);

        var result = await scenario.RunAsync(["activate", _sample]);

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Equal(ids.SelectMany(id => _steps.Select(step => $"{id} {step} ok")), result.Output);
        foreach (var id in ids)
        {
            Assert.Equal(1L, await CountAsync(scenario, id, "SELECT count(*) FROM axis.release_activations"));
            Assert.Equal(1L, await CountAsync(scenario, id, "SELECT count(*) FROM axis.release_activations WHERE actor = 'operator' AND number = 1 AND request_id IS NULL"));
        }

        await using var factory = CreateFactory(scenario.Platform);
        using var client = factory.CreateClient().SignedInAs("requester");
        foreach (var id in ids)
        {
            using var site = await client.SendAsync(Request(Site, Host(id)), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, site.StatusCode);
            using var department = await client.SendAsync(Request($"{Departments}/{SampleAppFixture.Operations:D}", Host(id)), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, department.StatusCode);
        }

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Activate_of_a_folder_that_does_not_compile_fails_each_tenant_with_the_diagnostic_and_keeps_its_active_release()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        var folder = Path.Combine(Path.GetTempPath(), $"axis-activate-{Guid.NewGuid():N}");
        try
        {
            CopyFolder(_sample, folder);
            Assert.Equal(0, (await scenario.RunAsync(["activate", folder])).Exit);
            var before = await ActiveReleasesAsync(scenario, ids);
            var file = Path.Combine(folder, "entities", "purchase-request.json");
            var text = await File.ReadAllTextAsync(file, CancellationToken);
            var broken = text.Replace("\"name\": \"title\", \"type\": \"text\"", "\"name\": \"title\", \"type\": \"nonsense\"", StringComparison.Ordinal);
            Assert.NotEqual(text, broken);
            await File.WriteAllTextAsync(file, broken, CancellationToken);

            var result = await scenario.RunAsync(["activate", folder]);

            Assert.Equal(1, result.Exit);
            foreach (var id in ids)
            {
                var lines = result.Output.Where(line => line.StartsWith($"{id} ", StringComparison.Ordinal)).ToArray();
                Assert.Equal($"{id} version ok", lines[0]);
                Assert.Contains(lines, line => FailedCompile(id).IsMatch(line) && line.Contains("purchase-request.json", StringComparison.Ordinal));
                Assert.DoesNotContain(lines, line => line.Contains(" activate ", StringComparison.Ordinal) || line.Contains(" seed ", StringComparison.Ordinal));
            }

            Assert.Equal(before, await ActiveReleasesAsync(scenario, ids));
            await scenario.AssertNoSecretsAsync();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Activate_for_a_tenant_on_another_schema_version_fails_at_version_naming_migrate_and_stores_no_release()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        var old = ids[0];
        await scenario.SetSchemaVersionAsync(old, "20000101000000_Old");

        var result = await scenario.RunAsync(["activate", _sample]);

        Assert.Equal(1, result.Exit);
        var oldLines = result.Output.Where(line => line.StartsWith($"{old} ", StringComparison.Ordinal)).ToArray();
        var line = Assert.Single(oldLines);
        Assert.StartsWith($"{old} version failed: ", line, StringComparison.Ordinal);
        Assert.Contains("migrate", line, StringComparison.Ordinal);
        Assert.Equal(0L, await CountAsync(scenario, old, "SELECT count(*) FROM axis.releases"));

        // The other tenant still runs.
        var other = ids[1];
        Assert.Equal(_steps.Select(step => $"{other} {step} ok"), result.Output.Where(l => l.StartsWith($"{other} ", StringComparison.Ordinal)));
        Assert.Equal(1L, await CountAsync(scenario, other, "SELECT count(*) FROM axis.releases"));

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Activate_builds_a_new_index_that_activation_deferred_on_a_table_above_the_limit()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = (await CreateTenantsAsync(scenario))[0];
        var folder = Path.Combine(Path.GetTempPath(), $"axis-activate-{Guid.NewGuid():N}");
        try
        {
            CopyFolder(_sample, folder);
            Assert.Equal(0, (await scenario.RunAsync(["activate", folder, "--tenant", id])).Exit);
            var table = EntityNaming.Table(Guid.Parse("2deb5acd-dfd8-4c5a-b546-a1aa03df6964"));
            await ScalarAsync(
                scenario,
                id,
                $"INSERT INTO {EntityNaming.QualifiedTable(table)} (id, f_code, f_name) SELECT gen_random_uuid(), 'IX-' || n, 'Supplier ' || n FROM generate_series(1, 25) n");
            var file = Path.Combine(folder, "entities", "supplier.json");
            var text = await File.ReadAllTextAsync(file, CancellationToken);
            var indexed = text.Replace("\"fields\": [", "\"indexes\": [{ \"fields\": [\"name\"] }],\n  \"fields\": [", StringComparison.Ordinal);
            Assert.NotEqual(text, indexed);
            await File.WriteAllTextAsync(file, indexed, CancellationToken);

            var result = await scenario.RunAsync(
                ["activate", folder, "--tenant", id], extra: new Dictionary<string, string?> { ["Activation:InlineRecomputeLimit"] = "10" });

            Assert.Equal(0, result.Exit);
            Assert.Equal(_steps.Select(step => $"{id} {step} ok"), result.Output);
            var index = EntityNaming.Index(table, ["f_name"]);
            Assert.Equal(true, await ScalarAsync(
                scenario,
                id,
                $"SELECT x.indisvalid FROM pg_index x JOIN pg_class i ON i.oid = x.indexrelid WHERE i.relname = '{index}'"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_directory_server_starts_and_serves_the_activated_site_when_no_tenant_schema_role_can_log_in()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        Assert.Equal(0, (await scenario.RunAsync(["activate", _sample])).Exit);
        foreach (var id in ids)
        {
            await PostgreSqlServer.AdminScalarAsync(scenario.Platform, $"ALTER ROLE \"axis_{id}_schema\" NOLOGIN", CancellationToken);
        }

        await using var factory = CreateFactory(scenario.Platform);
        using var client = factory.CreateClient().SignedInAs("requester");

        using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        foreach (var id in ids)
        {
            using var site = await client.SendAsync(Request(Site, Host(id)), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, site.StatusCode);
        }
    }

    [Fact]
    public async Task Activate_with_a_tenant_option_runs_only_that_tenant_and_accepts_a_suspended_one()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = await CreateTenantsAsync(scenario);
        await scenario.SetStatusAsync(ids[1], "suspended");

        var all = await scenario.RunAsync(["activate", _sample]);
        Assert.Equal(0, all.Exit);
        Assert.All(all.Output, line => Assert.StartsWith($"{ids[0]} ", line, StringComparison.Ordinal));

        var one = await scenario.RunAsync(["activate", _sample, "--tenant", ids[1]]);
        Assert.Equal(0, one.Exit);
        Assert.Equal(_steps.Select(step => $"{ids[1]} {step} ok"), one.Output);
        Assert.Equal(1L, await CountAsync(scenario, ids[1], "SELECT count(*) FROM axis.releases"));

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Activate_with_a_missing_folder_an_unknown_or_provisioning_tenant_a_bad_path_or_a_bad_option_exits_2_and_changes_no_directory_row()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var active = scenario.NewTenantId();
        var provisioning = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(active, Host(active))).Exit);
        await StopCreateAsync(scenario, provisioning);
        var before = await scenario.SnapshotAsync();

        var missing = await scenario.RunAsync(["activate", Path.Combine(Path.GetTempPath(), $"axis-missing-{Guid.NewGuid():N}")]);
        var unknown = await scenario.RunAsync(["activate", _sample, "--tenant", "nobody"]);
        var notReady = await scenario.RunAsync(["activate", _sample, "--tenant", provisioning]);
        var packages = await scenario.RunAsync(
            ["activate", _sample], extra: new Dictionary<string, string?> { ["Extensions:Path"] = Path.Combine(Path.GetTempPath(), $"axis-nopackages-{Guid.NewGuid():N}") });
        var modules = await scenario.RunAsync(
            ["activate", _sample], extra: new Dictionary<string, string?> { ["Modules:Path"] = Path.Combine(Path.GetTempPath(), $"axis-nomodules-{Guid.NewGuid():N}") });
        var limit = await scenario.RunAsync(["activate", _sample], extra: new Dictionary<string, string?> { ["Activation:InlineRecomputeLimit"] = "0" });
        var none = await scenario.RunAsync(["activate"]);
        var repeated = await scenario.RunAsync(["activate", _sample, "--tenant", active, "--tenant", active]);
        var two = await scenario.RunAsync(["activate", _sample, _sample]);
        var option = await scenario.RunAsync(["activate", _sample, "--all"]);

        Assert.All([missing, unknown, notReady, packages, modules, limit, none, repeated, two, option], result =>
        {
            Assert.Equal(2, result.Exit);
            Assert.Empty(result.Output);
        });
        Assert.Contains("does not exist", missing.Error[0], StringComparison.Ordinal);
        Assert.Contains("does not exist", unknown.Error[0], StringComparison.Ordinal);
        Assert.Contains("provisioning", notReady.Error[0], StringComparison.Ordinal);
        Assert.Contains("Extensions:Path", packages.Error[0], StringComparison.Ordinal);
        Assert.DoesNotContain("axis-nopackages", packages.Error[0], StringComparison.Ordinal);
        Assert.Contains("Modules:Path", modules.Error[0], StringComparison.Ordinal);
        Assert.DoesNotContain("axis-nomodules", modules.Error[0], StringComparison.Ordinal);
        Assert.Contains("Activation:InlineRecomputeLimit", limit.Error[0], StringComparison.Ordinal);
        Assert.All([none, repeated, two, option], result => Assert.StartsWith("Usage:", result.Error[0], StringComparison.Ordinal));
        Assert.Equal(before, await scenario.SnapshotAsync());

        await scenario.AssertNoSecretsAsync();
    }

    private static string Host(string id) => $"{id}.example.test";

    // Two active tenants with the purchase request sample's bindings, in id order.
    private static async Task<string[]> CreateTenantsAsync(AdminScenario scenario)
    {
        var ids = new[] { scenario.NewTenantId(), scenario.NewTenantId() }.Order(StringComparer.Ordinal).ToArray();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options);
        foreach (var id in ids)
        {
            Assert.Equal(0, (await scenario.CreateAsync(id, Host(id))).Exit);
            context.TenantBindings.Add(new TenantBindingRow { TenantId = id, Name = UrlBinding, Kind = "url", Url = "https://purchasing.example.test" });
            context.TenantBindings.AddRange(PurchasingBindings.Rows(id));
            context.TenantBindings.AddRange(CompanyBindings.Rows(id));
        }

        await context.SaveChangesAsync(CancellationToken);
        return ids;
    }

    private static async Task<string> ActiveReleasesAsync(AdminScenario scenario, string[] ids)
    {
        var releases = new List<string>();
        foreach (var id in ids)
        {
            releases.Add($"{id}={await ScalarAsync(scenario, id, "SELECT release_id FROM axis.active_releases")}");
        }

        return string.Join('\n', releases);
    }

    private static async Task<long> CountAsync(AdminScenario scenario, string id, string sql) => (long)(await ScalarAsync(scenario, id, sql))!;

    private static Task<object?> ScalarAsync(AdminScenario scenario, string id, string sql) =>
        PostgreSqlServer.AdminScalarAsync(new NpgsqlConnectionStringBuilder(scenario.Platform) { Database = $"axis_{id}" }.ConnectionString, sql, CancellationToken);

    private static void CopyFolder(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
    }

    // Stops a create right after its entry is written, which leaves a provisioning tenant and no database.
    private static async Task StopCreateAsync(AdminScenario scenario, string id)
    {
        using var stop = new CancellationTokenSource();
        var result = await scenario.RunAsync(AdminScenario.CreateArgs(id, Host(id)), new CancelAfterEntryWriter(stop), stop);
        Assert.Equal(1, result.Exit);
        Assert.Equal("provisioning", (await scenario.RowAsync(id)).Status);
    }

    private static WebApplicationFactory<Program> CreateFactory(string platform) =>
        AdminScenario.CreateServer(platform).WithWebHostBuilder(builder =>
            // An hour, so the timer never races the refresh that the test triggers itself.
            builder.UseSetting("Tenancy:RefreshInterval", "01:00:00"));

    private static Regex FailedCompile(string id) => new($@"^{Regex.Escape(id)} compile failed: AXC\d{{4}} ", RegexOptions.CultureInvariant);

    private sealed class CancelAfterEntryWriter(CancellationTokenSource source) : StringWriter
    {
        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            if (value is not null && value.EndsWith(" entry ok", StringComparison.Ordinal))
            {
                source.Cancel();
            }
        }
    }
}
