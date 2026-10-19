using System.Net;
using Axis.Admin;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class AdminTenantStatusTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Suspend_makes_the_host_answer_503_after_one_refresh_and_resume_serves_it_again()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, "shop.example.test")).Exit);
        await using var factory = AdminScenario.CreateServer(scenario.Platform);
        using var client = factory.CreateClient().SignedInAs("anna");
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, "shop.example.test"));

        var suspend = await scenario.RunAsync(["tenant", "suspend", id]);
        await RefreshAsync(factory);

        Assert.Equal(0, suspend.Exit);
        Assert.Equal([$"{id} status ok"], suspend.Output);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await StatusAsync(client, "shop.example.test"));

        var resume = await scenario.RunAsync(["tenant", "resume", id]);
        await RefreshAsync(factory);

        Assert.Equal(0, resume.Exit);
        Assert.Equal([$"{id} status ok"], resume.Output);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, "shop.example.test"));

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Suspend_and_resume_raise_the_version_by_one_and_repeating_them_changes_nothing()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, "shop.example.test")).Exit);
        var created = await scenario.RowAsync(id);

        Assert.Equal(0, (await scenario.RunAsync(["tenant", "suspend", id])).Exit);
        var suspended = await scenario.RowAsync(id);
        var snapshot = await scenario.SnapshotAsync();
        var again = await scenario.RunAsync(["tenant", "suspend", id]);

        Assert.Equal("suspended", suspended.Status);
        Assert.Equal(created.Version + 1, suspended.Version);
        Assert.Equal(0, again.Exit);
        Assert.Equal([$"{id} status ok"], again.Output);
        Assert.Equal(snapshot, await scenario.SnapshotAsync());

        Assert.Equal(0, (await scenario.RunAsync(["tenant", "resume", id])).Exit);
        var resumed = await scenario.RowAsync(id);
        snapshot = await scenario.SnapshotAsync();
        var resumeAgain = await scenario.RunAsync(["tenant", "resume", id]);

        Assert.Equal("active", resumed.Status);
        Assert.Equal(suspended.Version + 1, resumed.Version);
        Assert.Equal(0, resumeAgain.Exit);
        Assert.Equal(snapshot, await scenario.SnapshotAsync());

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task List_prints_every_tenant_in_id_order_with_status_hosts_and_schema_version_and_no_secret()
    {
        await using var scenario = await NewScenarioAsync();
        var ids = new[] { scenario.NewTenantId(), scenario.NewTenantId() }.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(0, (await scenario.CreateAsync(ids[0], "b.example.test", "a.example.test")).Exit);
        Assert.Equal(0, (await scenario.CreateAsync(ids[1], "c.example.test")).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "suspend", ids[1]])).Exit);
        var first = await scenario.RowAsync(ids[0]);
        var second = await scenario.RowAsync(ids[1]);

        var result = await scenario.RunAsync(["tenant", "list"]);

        Assert.Equal(0, result.Exit);
        Assert.Equal(3, result.Output.Length);
        Assert.Equal(["id", "status", "hosts", "schema version"], Cells(result.Output[0]));
        Assert.Equal([ids[0], "active", "a.example.test,b.example.test", first.SchemaVersion], Cells(result.Output[1]));
        Assert.Equal([ids[1], "suspended", "c.example.test", second.SchemaVersion], Cells(result.Output[2]));

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task List_of_an_empty_directory_prints_only_the_header()
    {
        await using var scenario = await NewScenarioAsync();

        var result = await scenario.RunAsync(["tenant", "list"]);

        Assert.Equal(0, result.Exit);
        Assert.Equal(["id", "status", "hosts", "schema version"], Cells(Assert.Single(result.Output)));
    }

    [Fact]
    public async Task Suspend_of_an_unknown_tenant_exits_2_and_resume_of_a_provisioning_tenant_exits_1_and_nothing_changes()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        using var stop = new CancellationTokenSource();
        await scenario.RunAsync(AdminScenario.CreateArgs(id, "shop.example.test"), new CancelOnLineWriter(stop), stop);
        Assert.Equal("provisioning", (await scenario.RowAsync(id)).Status);
        var before = await scenario.SnapshotAsync();

        var unknown = await scenario.RunAsync(["tenant", "suspend", "t-unknown"]);
        var resume = await scenario.RunAsync(["tenant", "resume", id]);
        var suspend = await scenario.RunAsync(["tenant", "suspend", id]);

        Assert.Equal(2, unknown.Exit);
        Assert.Equal(["The tenant does not exist."], unknown.Error);
        Assert.Equal(1, resume.Exit);
        Assert.Contains("tenant create", Assert.Single(resume.Output), StringComparison.Ordinal);
        Assert.Equal(1, suspend.Exit);
        Assert.Equal(before, await scenario.SnapshotAsync());

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_wrong_shape_exits_2_with_the_usage_and_the_commands_need_only_the_platform_setting()
    {
        await using var scenario = await NewScenarioAsync();

        var noId = await scenario.RunAsync(["tenant", "suspend"]);
        var extra = await scenario.RunAsync(["tenant", "list", "t-x"]);
        await using var unmigrated = await AdminScenario.NewAsync(database, migrate: false, CancellationToken);
        var noTables = await unmigrated.RunAsync(["tenant", "list"]);

        Assert.Equal(2, noId.Exit);
        Assert.StartsWith("Usage:", noId.Error[0], StringComparison.Ordinal);
        Assert.Equal(2, extra.Exit);
        Assert.StartsWith("Usage:", extra.Error[0], StringComparison.Ordinal);
        Assert.Equal(2, noTables.Exit);
        Assert.Contains("platform tables do not exist", noTables.Error[0], StringComparison.Ordinal);

        var onlyPlatform = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:PlatformSchema"] = scenario.Platform })
            .Build();
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, await AdminCommands.RunAsync(["tenant", "list"], onlyPlatform, output, error, CancellationToken));
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string host)
    {
        using var response = await client.SendAsync(Request("/api/me", host), CancellationToken);
        return response.StatusCode;
    }

    private static Task RefreshAsync(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<TenantDirectoryRefresh>().RefreshAsync(CancellationToken);

    private static string[] Cells(string line) => line.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private Task<AdminScenario> NewScenarioAsync() => AdminScenario.NewAsync(database, migrate: true, CancellationToken);
}
