using System.Globalization;
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
/// and Python test packages, see <c>docs/reference/extension-packages.md#customer-hosted-runners</c>. The command runs in the test process and talks to the server
/// in memory. The server and the command each hold their own copy of the installed package. Each test makes the realm of its tenant with a unique
/// name and deletes it at the end.
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

    private IConfiguration Settings(string tenantId, PackageRunnerServer.RunnerClient runner, string extensions, ConnectorStub stub) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Connect:Url"] = $"http://{tenantId}.vn.example.test",
            ["Connect:TokenUrl"] = _server.TokenUrl(tenantId).ToString(),
            ["Connect:ClientId"] = runner.ClientId,
            ["Connect:ClientSecret"] = runner.Secret,
            ["Extensions:Path"] = extensions,
            ["Connect:Urls:TestUrl"] = $"{stub.BaseUrl}/tenant-x",
            ["Connect:Secrets:TestToken"] = "stub-token",
        }).Build();

    private static Task<string?> CallStateAsync(string tenantDatabase, Guid callId) =>
        TextAsync(tenantDatabase, $"SELECT state FROM axis.package_calls WHERE id = '{callId}'");

    private static Task<string?> StatusOfAsync(string tenantDatabase, Guid probeId) =>
        TextAsync(
            tenantDatabase,
            $"SELECT {EntityNaming.Quote(EntityNaming.Column("status"))}::text FROM {EntityNaming.QualifiedTable(EntityNaming.Table(CustomerHostedReleases.ProbeEntityId))} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = '{probeId}'");

    private static async Task<string?> TextAsync(string tenantDatabase, string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(tenantDatabase, sql, CancellationToken), CultureInfo.InvariantCulture) is { Length: > 0 } text ? text : null;
}
