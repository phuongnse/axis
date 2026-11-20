using System.Globalization;
using System.Net;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Axis.Data.Naming;
using Axis.PackageHost;
using Axis.PackageHost.Tests;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Server.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>connect</c> command of <c>Axis.PackageHost</c> against the server, with real PostgreSQL and Keycloak and the real runners of the C#, Node.js
/// and Python test packages, see <c>docs/reference/package-runners.md#customer-hosted-runners</c>. The command runs in the test process and talks to the server
/// in memory. The server and the command each hold their own copy of the installed package. Each test makes the realm of its tenant with a unique
/// name and deletes it at the end. Two instances may share one runner credential and each takes a different call, and a revoke of the runner stops both,
/// see <c>docs/reference/package-runners.md#customer-hosted-runners</c> ("The program" and "Revoked").
/// </summary>
public sealed class ConnectCommandTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private const string NodeProbePackageId = "node-probe-package";

    private readonly PackageRunnerServer _server = new(database, keycloak);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _server.Dispose();

    [Theory]
    [InlineData(TestPackages.Id)]
    [InlineData(NodeProbePackageId)]
    [InlineData(TestPackages.PythonId)]
    public async Task Connect_runs_the_probe_of_a_customer_hosted_package_in_the_runner_of_its_language_and_the_instance_completes_with_the_probes_status(string package)
    {
        await _server.PrepareAsync();
        var (id, tenantDatabase) = await _server.AddTenantAsync();
        try
        {
            using var serverPackages = Install(package);
            using var runnerPackages = Install(package);
            var releases = new CustomerHostedReleases();
            var releaseId = await releases.ActivateInstalledProbeAsync(
                tenantDatabase, id, PackageCatalog.Load(serverPackages.Path, PackageLoadMode.MetadataOnly), CancellationToken, package);
            await PostgreSqlServer.GrantRuntimeRoleAsync(tenantDatabase, CancellationToken);
            var (callId, instanceId, probeId) = await releases.StageProbeCallAsync(tenantDatabase, releaseId, CancellationToken, package);
            await using var stub = await ConnectorStub.StartAsync();
            stub.Respond(202, "{}");
            await using var factory = _server.Start(serverPackages.Path);
            using var admin = PackageRunnerServer.Client(factory, id).SignedInAs("admin");
            var runner = await PackageRunnerServer.CreateRunnerClientAsync(admin);
            var error = new StringWriter();
            using var cancellation = new CancellationTokenSource();
            var command = ConnectCommand.RunAsync(
                Settings(id, runner, runnerPackages.Path, stub),
                development: true,
                TestRunnerCommand.Create(),
                error,
                NullLogger.Instance,
                cancellation.Token,
                server: factory.Server.CreateHandler());
            int exitCode;
            try
            {
                await Polling.WaitUntilAsync(
                    async () => command.IsCompleted || await CallStateAsync(tenantDatabase, callId) == "succeeded",
                    "The call did not succeed.",
                    TimeSpan.FromSeconds(90));
            }
            finally
            {
                await cancellation.CancelAsync();
                exitCode = await command;
            }

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error.ToString());
            Assert.Equal("succeeded", await CallStateAsync(tenantDatabase, callId));
            var request = Assert.Single(stub.Requests);
            Assert.Equal("POST", request.Method);
            Assert.Equal("/tenant-x/probe", request.Path);
            Assert.Equal("Bearer stub-token", request.Authorization);
            Assert.Equal(callId.ToString("D"), request.IdempotencyKey);
            Assert.Equal("202", await StatusOfAsync(tenantDatabase, probeId));

            Assert.Equal(WorkItemOutcome.Completed, await PackageRunnerServer.RunItemAsync(id, tenantDatabase, ProcessStarts.StepWorkItemKind, serverPackages.Path));
            Assert.Equal("completed", await TextAsync(tenantDatabase, $"SELECT state FROM axis.process_instances WHERE id = '{instanceId}'"));
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task Connect_exits_with_code_1_and_names_the_package_when_its_manifest_differs_from_the_pin()
    {
        await _server.PrepareAsync();
        var (id, tenantDatabase) = await _server.AddTenantAsync();
        try
        {
            using var serverPackages = new TemporaryFolder().Install("1.0.0");
            using var runnerPackages = new TemporaryFolder().Install("1.0.0")
                .EditManifest("1.0.0", manifest => manifest["operations"]!.AsArray().Single(operation => (string?)operation!["name"] == "probe")!["idempotent"] = true);
            var releases = new CustomerHostedReleases();
            await releases.ActivateInstalledProbeAsync(
                tenantDatabase, id, PackageCatalog.Load(serverPackages.Path, PackageLoadMode.MetadataOnly), CancellationToken);
            await using var stub = await ConnectorStub.StartAsync();
            await using var factory = _server.Start(serverPackages.Path);
            using var admin = PackageRunnerServer.Client(factory, id).SignedInAs("admin");
            var runner = await PackageRunnerServer.CreateRunnerClientAsync(admin);
            var error = new StringWriter();

            var exitCode = await ConnectCommand.RunAsync(
                Settings(id, runner, runnerPackages.Path, stub),
                development: true,
                TestRunnerCommand.Create(),
                error,
                NullLogger.Instance,
                CancellationToken,
                server: factory.Server.CreateHandler());

            Assert.Equal(1, exitCode);
            Assert.Contains("test-package", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("1.0.0", error.ToString(), StringComparison.Ordinal);
            Assert.Empty(stub.Requests);
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task Two_connect_instances_with_one_credential_take_a_different_call_each_and_both_instances_complete()
    {
        await WithTwoInstancesAsync(async two =>
        {
            var requests = two.Stub.Requests.ToList();
            Assert.Equal(["/instance-a/probe", "/instance-b/probe"], requests.Select(request => request.Path).Order(StringComparer.Ordinal));
            Assert.Equal(
                two.Calls.Select(call => call.CallId.ToString("D")).Order(StringComparer.Ordinal),
                requests.Select(request => request.IdempotencyKey).Order(StringComparer.Ordinal));

            foreach (var call in two.Calls)
            {
                Assert.Equal("202", await StatusOfAsync(two.TenantDatabase, call.ProbeId));
                Assert.Equal(WorkItemOutcome.Completed, await PackageRunnerServer.RunItemAsync(two.TenantId, two.TenantDatabase, ProcessStarts.StepWorkItemKind, two.ServerPackages));
            }

            foreach (var call in two.Calls)
            {
                Assert.Equal("completed", await TextAsync(two.TenantDatabase, $"SELECT state FROM axis.process_instances WHERE id = '{call.InstanceId}'"));
            }

            await two.Cancellation.CancelAsync();
            Assert.All(await Task.WhenAll(two.CommandA, two.CommandB), exitCode => Assert.Equal(0, exitCode));
            Assert.Equal(string.Empty, two.ErrorA.ToString());
            Assert.Equal(string.Empty, two.ErrorB.ToString());
        });
    }

    [Fact]
    public async Task A_revoke_of_the_runner_makes_both_connect_instances_exit_with_code_1_and_no_further_call_is_taken()
    {
        await WithTwoInstancesAsync(async two =>
        {
            using (var revoke = await PackageRunnerServer.SendAsync(two.Admin, HttpMethod.Post, $"{PackageRunnerServer.Runners}/{two.Runner.Id}/revoke", csrf: true))
            {
                Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
            }

            var (lateCallId, _, _) = await two.Releases.StageProbeCallAsync(two.TenantDatabase, two.ReleaseId, CancellationToken);
            await Polling.WaitUntilAsync(
                () => two.CommandA.IsCompleted && two.CommandB.IsCompleted,
                "The instances did not exit after the revoke.",
                TimeSpan.FromSeconds(60));

            Assert.All(await Task.WhenAll(two.CommandA, two.CommandB), exitCode => Assert.Equal(1, exitCode));
            Assert.NotEqual(string.Empty, two.ErrorA.ToString());
            Assert.NotEqual(string.Empty, two.ErrorB.ToString());
            Assert.Equal("pending", await CallStateAsync(two.TenantDatabase, lateCallId));
            Assert.Equal("0", await TextAsync(two.TenantDatabase, $"SELECT attempts FROM axis.package_calls WHERE id = '{lateCallId}'"));
            Assert.Equal(2, two.Stub.Requests.Count);
        });
    }

    // Starts two instances with one runner credential and stages two probe calls. The stub holds the first probe request until the second one arrives,
    // and an instance runs one call at a time, so each instance takes one of the calls. It returns when both calls have succeeded.
    private async Task WithTwoInstancesAsync(Func<TwoInstances, Task> test)
    {
        await _server.PrepareAsync();
        var (id, tenantDatabase) = await _server.AddTenantAsync();
        try
        {
            using var serverPackages = new TemporaryFolder().Install("1.0.0");
            using var runnerPackages = new TemporaryFolder().Install("1.0.0");
            var releases = new CustomerHostedReleases();
            var releaseId = await releases.ActivateInstalledProbeAsync(
                tenantDatabase, id, PackageCatalog.Load(serverPackages.Path, PackageLoadMode.MetadataOnly), CancellationToken);
            await PostgreSqlServer.GrantRuntimeRoleAsync(tenantDatabase, CancellationToken);
            var calls = new List<(Guid CallId, Guid InstanceId, Guid ProbeId)>
            {
                await releases.StageProbeCallAsync(tenantDatabase, releaseId, CancellationToken),
                await releases.StageProbeCallAsync(tenantDatabase, releaseId, CancellationToken),
            };
            await using var stub = await ConnectorStub.StartAsync();
            stub.RespondMany((202, "{}"), (202, "{}"));
            var held = stub.Hold();
            await using var factory = _server.Start(serverPackages.Path);
            using var admin = PackageRunnerServer.Client(factory, id).SignedInAs("admin");
            var runner = await PackageRunnerServer.CreateRunnerClientAsync(admin);
            var errorA = new StringWriter();
            var errorB = new StringWriter();
            using var cancellation = new CancellationTokenSource();
            var commandA = ConnectCommand.RunAsync(
                Settings(id, runner, runnerPackages.Path, stub, "instance-a"),
                development: true,
                TestRunnerCommand.Create(),
                errorA,
                NullLogger.Instance,
                cancellation.Token,
                server: factory.Server.CreateHandler());
            var commandB = ConnectCommand.RunAsync(
                Settings(id, runner, runnerPackages.Path, stub, "instance-b"),
                development: true,
                TestRunnerCommand.Create(),
                errorB,
                NullLogger.Instance,
                cancellation.Token,
                server: factory.Server.CreateHandler());
            try
            {
                await held.Arrived.WaitAsync(TimeSpan.FromSeconds(90), CancellationToken);
                await Polling.WaitUntilAsync(
                    () => stub.Requests.Count >= 2 || commandA.IsCompleted || commandB.IsCompleted,
                    "The second instance did not take the second call.",
                    TimeSpan.FromSeconds(90));
                held.Release();
                await Polling.WaitUntilAsync(
                    async () => commandA.IsCompleted || commandB.IsCompleted
                        || (await CallStateAsync(tenantDatabase, calls[0].CallId) == "succeeded" && await CallStateAsync(tenantDatabase, calls[1].CallId) == "succeeded"),
                    "The calls did not succeed.",
                    TimeSpan.FromSeconds(90));
                await test(new TwoInstances(
                    id, tenantDatabase, serverPackages.Path, releaseId, releases, calls, stub, admin, runner,
                    commandA, commandB, errorA, errorB, cancellation));
            }
            finally
            {
                held.Release();
                await cancellation.CancelAsync();
                await Task.WhenAll(commandA, commandB);
            }
        }
        finally
        {
            await _server.DeleteRealmAsync(id);
        }
    }

    // The Node.js probe package is not one of the builds that TestPackages installs, so it is copied as the build command leaves it.
    private static TemporaryFolder Install(string package)
    {
        var folder = new TemporaryFolder();
        switch (package)
        {
            case TestPackages.Id:
                return folder.Install("1.0.0");
            case TestPackages.PythonId:
                return folder.InstallPython();
            default:
                PackageBuildCommand.Copy(Path.Combine(AppContext.BaseDirectory, "NodeProbePackage"), Path.Combine(folder.Path, package, "1.0.0"));
                return folder;
        }
    }

    private IConfiguration Settings(string tenantId, PackageRunnerServer.RunnerClient runner, string extensions, ConnectorStub stub, string path = "tenant-x") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Connect:Url"] = $"http://{tenantId}.vn.example.test",
            ["Connect:TokenUrl"] = _server.TokenUrl(tenantId).ToString(),
            ["Connect:ClientId"] = runner.ClientId,
            ["Connect:ClientSecret"] = runner.Secret,
            ["Extensions:Path"] = extensions,
            ["Connect:Urls:TestUrl"] = $"{stub.BaseUrl}/{path}",
            ["Connect:Secrets:TestToken"] = "stub-token",
        }).Build();

    private sealed record TwoInstances(
        string TenantId,
        string TenantDatabase,
        string ServerPackages,
        Guid ReleaseId,
        CustomerHostedReleases Releases,
        IReadOnlyList<(Guid CallId, Guid InstanceId, Guid ProbeId)> Calls,
        ConnectorStub Stub,
        HttpClient Admin,
        PackageRunnerServer.RunnerClient Runner,
        Task<int> CommandA,
        Task<int> CommandB,
        StringWriter ErrorA,
        StringWriter ErrorB,
        CancellationTokenSource Cancellation);

    private static Task<string?> CallStateAsync(string tenantDatabase, Guid callId) =>
        TextAsync(tenantDatabase, $"SELECT state FROM axis.package_calls WHERE id = '{callId}'");

    private static Task<string?> StatusOfAsync(string tenantDatabase, Guid probeId) =>
        TextAsync(
            tenantDatabase,
            $"SELECT {EntityNaming.Quote(EntityNaming.Column("status"))}::text FROM {EntityNaming.QualifiedTable(EntityNaming.Table(CustomerHostedReleases.ProbeEntityId))} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = '{probeId}'");

    private static async Task<string?> TextAsync(string tenantDatabase, string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(tenantDatabase, sql, CancellationToken), CultureInfo.InvariantCulture) is { Length: > 0 } text ? text : null;
}
