using System.Net;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.PurchasingStub;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Axis.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Npgsql;
using static Axis.Integration.Tests.AdminTenantRestoreTests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A restored tenant carries on from backup time once it resumes. A purchase request that waited for its manager at backup is
/// decided in the restored tenant and gets one purchase order. A step that was due and not yet run at backup runs after the
/// resume. The original tenant keeps its own process instances. Each work item of the archive names the old tenant, so the
/// restore gives it the new id and the worker of the new tenant claims it.
/// </summary>
public sealed class RestoredTenantWorkTests(AdminBackupFixture fixture) : IClassFixture<AdminBackupFixture>
{
    private const string App = "/api/apps/PurchaseRequests";

    private const string UrlBinding = "PurchasingSystemUrl";

    private const string Waiting = "waiting:managerApproval";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Work_at_backup_time_carries_on_in_the_restored_tenant_once_it_resumes_and_the_original_tenant_is_left_as_it_was()
    {
        var scenario = fixture.Scenario;
        var original = await scenario.RowAsync(fixture.TenantId);
        using var requester = fixture.CreateClient();

        // Request A waits for its manager at backup time. Request B starts with no worker running, so its first step is still due.
        var requestA = await AdminBackupFixture.CreateRequestAsync(requester);
        var instanceA = await StartAsync(requester, requestA);

        // The server limits the runtime role to reading the platform schema when it starts. The workers need that before any server runs.
        await using (var connection = new NpgsqlConnection(scenario.Platform))
        {
            await connection.OpenAsync(CancellationToken);
            await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
        }

        var first = await BuildWorkerAsync(scenario.Platform);
        try
        {
            await Polling.WaitUntilAsync(async () => await StateAsync(original, instanceA) == Waiting, "The original instance did not wait.");
        }
        finally
        {
            await first.StopAsync(CancellationToken);
            await first.DisposeAsync();
        }

        var requestB = await AdminBackupFixture.CreateRequestAsync(requester);
        var instanceB = await StartAsync(requester, requestB);
        Assert.NotEqual(Waiting, await StateAsync(original, instanceB));

        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        Assert.Equal(0, (await fixture.BackupAsync(file)).Exit);
        var newId = scenario.NewTenantId();
        const string newHost = "restored.example.test";
        var restore = await scenario.RunAsync(RestoreArgs(file, newId, newHost), path: fixture.Tools.PathSetting);
        Assert.Equal(0, restore.Exit);
        var restored = await scenario.RowAsync(newId);

        // The operator sets the bindings that the archive cannot carry: the purchasing system stub stands for the real one.
        await using var stub = PurchasingStubApp.Create(["--urls", "http://127.0.0.1:0"]);
        await stub.StartAsync(CancellationToken);
        var stubAddress = stub.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", newId, UrlBinding, "--url", stubAddress], extra: AdminScenario.Directory)).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", newId, PurchasingBindings.TokenUrl, "--url", $"{stubAddress}/oauth/token"], extra: AdminScenario.Directory)).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", newId, PurchasingBindings.WebhookUrl, "--url", $"{stubAddress}/webhooks/purchase-request-approved"], extra: AdminScenario.Directory)).Exit);
        foreach (var (name, value) in new[] { (PurchasingBindings.ClientId, PurchasingStubApp.DefaultClientId), (PurchasingBindings.ClientSecret, PurchasingStubApp.DefaultClientSecret), (PurchasingBindings.WebhookSecret, PurchasingStubApp.DefaultWebhookSecret) })
        {
            Assert.Equal(
                0,
                (await scenario.RunAsync(["tenant", "binding", "set", newId, name, "--secret"], input: value, extra: AdminScenario.Directory)).Exit);
        }
        foreach (var name in new[] { CompanyBindings.ClientId, CompanyBindings.ClientSecret })
        {
            Assert.Equal(
                0,
                (await scenario.RunAsync(["tenant", "binding", "set", newId, name, "--secret"], input: $"{name}-restored", extra: AdminScenario.Directory)).Exit);
        }

        Assert.Equal(0, (await scenario.RunAsync(["tenant", "resume", newId])).Exit);

        await using var server = CreateDirectoryServer(scenario.Platform);
        await RefreshAsync(server);
        var second = await BuildWorkerAsync(scenario.Platform);
        try
        {
            // B was due at backup time: it only reaches the manager if the worker of the new tenant claims its work item.
            await Polling.WaitUntilAsync(async () => await StateAsync(restored, instanceB) == Waiting, "The restored instance did not wait.");

            var task = (Guid)(await PostgreSqlServer.AdminScalarAsync(
                DatabaseOf(restored),
                $"SELECT id FROM axis.process_tasks WHERE process_instance_id = '{instanceA:D}' AND state = 'open'",
                CancellationToken))!;
            using var manager2 = server.CreateClient().SignedInAs("manager-2");
            using (var complete = await manager2.SendAsync(
                Request(HttpMethod.Post, $"{App}/tasks/{task:D}/complete", newHost, """{ "outcome": "approve" }"""),
                CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
            }

            await Polling.WaitUntilAsync(async () => (await StateAsync(restored, instanceA))?.StartsWith("completed:", StringComparison.Ordinal) == true, "The restored instance did not complete.");

            using var read = await manager2.SendAsync(Request($"{App}/entities/PurchaseRequest/records/{requestA:D}", newHost), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var record = JsonDocument.Parse(await read.Content.ReadAsStringAsync(CancellationToken));
            var values = record.RootElement.GetProperty("values");
            var number = values.GetProperty("number").GetString()!;
            var orderNumber = values.GetProperty("purchaseOrderNumber").GetString();

            using var stubClient = new HttpClient { BaseAddress = new Uri(stubAddress) };
            using var orders = JsonDocument.Parse(await stubClient.GetStringAsync("/purchase-orders", CancellationToken));
            var ordersOfRequest = orders.RootElement.EnumerateArray()
                .Where(order => order.GetProperty("requestNumber").GetString() == number)
                .Select(order => order.GetProperty("orderNumber").GetString())
                .ToList();
            Assert.Equal([orderNumber], ordersOfRequest);
            Assert.False(string.IsNullOrEmpty(orderNumber));

            // The original tenant is not touched.
            Assert.Equal(Waiting, await StateAsync(original, instanceA));
        }
        finally
        {
            await second.StopAsync(CancellationToken);
            await second.DisposeAsync();
        }

        await scenario.AssertNoSecretsAsync();
    }

    // Starts the approval process for the request through the server of the original tenant, and returns the instance id.
    private static async Task<Guid> StartAsync(HttpClient client, Guid subject)
    {
        using var request = Request(HttpMethod.Post, $"{App}/processes/PurchaseRequestApproval/instances", AdminBackupFixture.Host, $$"""{ "subjectId": "{{subject:D}}" }""");
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    // The same instance ids are in the original and in the restored database.
    private async Task<string?> StateAsync(TenantRow row, Guid instance) =>
        (string?)await PostgreSqlServer.AdminScalarAsync(
            DatabaseOf(row),
            $"SELECT state || ':' || step FROM axis.process_instances WHERE id = '{instance:D}'",
            CancellationToken);

    private string DatabaseOf(TenantRow row) =>
        new NpgsqlConnectionStringBuilder(fixture.Scenario.Platform) { Database = row.DatabaseName }.ConnectionString;

    // A worker on the tenant directory. It runs as Development, the only environment that lets a connector call an http URL.
    private static async Task<WebApplication> BuildWorkerAsync(string platform)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
            ["ConnectionStrings:Platform"] = PostgreSqlServer.RuntimeConnectionString(platform),
            ["Platform:SecretKey"] = AdminScenario.TestSecretKey,
            ["Extensions:Path"] = SamplePackages.Path,
            ["Modules:Path"] = SampleModules.Path,
            ["Health:Urls"] = "http://127.0.0.1:0",
            ["Worker:PollInterval"] = "00:00:00.05",
        });
        builder.Logging.ClearProviders();
        builder.Services.AddAxisWorker(
            builder.Configuration,
            new HostingEnvironment { EnvironmentName = Environments.Development, ContentRootPath = AppContext.BaseDirectory });
        var app = builder.Build();
        await app.StartAsync(CancellationToken);
        return app;
    }
}
