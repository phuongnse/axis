using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Record = Axis.Data.Records.Record;

namespace Axis.Integration.Tests;

/// <summary>
/// Two tenant databases, <c>a</c> and <c>b</c>, each with the same application: a copy of the StepApp fixture that holds the Python test
/// package as a package of its own under <c>packages/python-test-package</c>, see
/// docs/reference/extension-packages.md#an-applications-packages. The copy adds an integer field <c>runnerPid</c> to <c>Request</c> and these
/// processes: <c>ApplicationPid</c>, which runs <c>python-test-package/pid</c> and writes the process id of its runner to <c>runnerPid</c>,
/// and <c>ApplicationProbeOwnOtherHost</c>, which runs <c>python-test-package/probe</c> in the mode <c>ownOtherHost</c>, whose request to a
/// host that no <c>url</c> binding declares goes through the worker's egress proxy. Each release stores the package's built files, which are
/// the Python test package and its dependency laid out as a build leaves them. Tenant <c>a</c> holds two more releases. The first,
/// <see cref="NoBuiltFilesReleaseA"/>, stores the source and no built file, as <c>activate</c> does. The second, <see cref="ChangedBuildReleaseA"/>,
/// stores built files that differ by one file while the package's version stays the same. The workers have no installed package, run the
/// packages of the two tenants and send an application's package to the host that the test sets in <c>Extensions:Runners:Application</c>.
/// The fixture starts no worker host, so a test runs the work items itself.
/// </summary>
public sealed class ApplicationPackageCallFixture : IAsyncLifetime
{
    /// <summary>The first tenant.</summary>
    public const string TenantA = "a";

    /// <summary>The second tenant.</summary>
    public const string TenantB = "b";

    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private const string PackageFolder = "packages/" + TestPackages.PythonId + "/";

    private const string PidProcess = """
        {
          "id": "4c9e2a71-0b3d-4f58-9a16-7e2d8c5b1f03",
          "kind": "process",
          "name": "ApplicationPid",
          "formatVersion": 1,
          "entity": "Request",
          "start": "pid",
          "steps": [
            {
              "name": "pid",
              "type": "operation",
              "operation": "python-test-package/pid",
              "input": {},
              "output": { "pid": "runnerPid" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private const string ProbeProcess = """
        {
          "id": "8a1d6f35-2e90-4b47-a3c8-5d7b0e9f4a12",
          "kind": "process",
          "name": "ApplicationProbeOwnOtherHost",
          "formatVersion": 1,
          "entity": "Request",
          "start": "probe",
          "steps": [
            {
              "name": "probe",
              "type": "operation",
              "operation": "python-test-package/probe",
              "input": { "mode": "'ownOtherHost'" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private readonly ProcessStepFixture _a = new(TenantA);
    private readonly ProcessStepFixture _b = new(TenantB);
    private readonly TemporaryFolder _noPackages = new();
    private readonly Dictionary<string, (Guid ReleaseId, ApplicationModel Model)> _releases = [];

    /// <summary>The release of tenant <c>a</c> with built files, which <see cref="StageAsync"/> uses when no release is given.</summary>
    public Guid ReleaseIdA => _releases[TenantA].ReleaseId;

    /// <summary>The release of tenant <c>a</c> that holds the package's source and no built file.</summary>
    public (Guid ReleaseId, ApplicationModel Model) NoBuiltFilesReleaseA { get; private set; }

    /// <summary>The release of tenant <c>a</c> whose built files have one file more than <see cref="ReleaseIdA"/>, with the same package version.</summary>
    public (Guid ReleaseId, ApplicationModel Model) ChangedBuildReleaseA { get; private set; }

    /// <summary>Every log entry of the workers.</summary>
    internal LogCollector Logs { get; } = new();

    /// <summary>The database of <paramref name="tenant"/>.</summary>
    public ProcessStepFixture Tenant(string tenant) => tenant == TenantA ? _a : _b;

    public async ValueTask InitializeAsync()
    {
        await _a.InitializeAsync();
        await _b.InitializeAsync();

        var source = await SourceFilesAsync();
        var built = BuiltFiles();
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            _releases[tenant] = await Tenant(tenant).ActivateFilesAsync(source, built, Bindings(tenant));
        }

        var note = new ResourceContent(PackageFolder + "note.txt", "A note that makes the content hash differ.");
        NoBuiltFilesReleaseA = await _a.ActivateFilesAsync([.. source, note], new Dictionary<string, byte[]>(), Bindings(TenantA));
        var changed = new Dictionary<string, byte[]>(built) { [PackageFolder + "build-note.txt"] = "A file that changes the build."u8.ToArray() };
        ChangedBuildReleaseA = await _a.ActivateFilesAsync([.. source, note with { Content = note.Content + " Changed." }], changed, Bindings(TenantA));
    }

    public async ValueTask DisposeAsync()
    {
        await _a.DisposeAsync();
        await _b.DisposeAsync();
        _noPackages.Dispose();
    }

    /// <summary>
    /// A worker for both tenants with no installed package, whose application packages go to the package host at
    /// <paramref name="applicationHost"/>, or to none when it is <see langword="null"/>. The caller disposes it, so a test can run several
    /// calls on one worker, and its idle runners stay until then.
    /// </summary>
    internal ServiceProvider BuildWorker(string? applicationHost)
    {
        var values = new Dictionary<string, string?>
        {
            [PackageCatalog.PathSetting] = _noPackages.Path,
            [PackageIsolation.IsolationSetting] = "process",
            [PackageIsolation.ProxySetting] = "0",
            [ApplicationPackageSettings.RunnerSetting] = applicationHost,
        };
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            values[$"Tenants:{tenant}:Hosts:0"] = $"{tenant}.example.test";
            values[$"Tenants:{tenant}:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(Tenant(tenant).ConnectionString);
            values[$"Tenants:{tenant}:Bindings:TestUrl:Kind"] = "url";
            values[$"Tenants:{tenant}:Bindings:TestUrl:Value"] = "https://test.example.test";
            values[$"Tenants:{tenant}:Bindings:TestToken:Kind"] = "secret";
            values[$"Tenants:{tenant}:Bindings:TestToken:Value"] = "test-token";
        }

        var settings = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace))
            .AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = "Development", ContentRootPath = AppContext.BaseDirectory })
            .BuildServiceProvider();
    }

    /// <summary>
    /// Starts <paramref name="process"/> in <paramref name="tenant"/> for a new request and runs its step, which stages the package call.
    /// Work items that earlier tests left queued in the tenant are deleted first. The instance starts on <paramref name="release"/> when it is
    /// given, and on the tenant's release with built files otherwise.
    /// </summary>
    public async Task<(Guid SubjectId, Guid InstanceId)> StageAsync(
        string tenant,
        string process,
        CancellationToken cancellationToken,
        (Guid ReleaseId, ApplicationModel Model)? release = null)
    {
        var database = Tenant(tenant);
        await database.ScalarAsync("DELETE FROM axis.process_work_items");
        var subjectId = await database.InsertRequestAsync(500m, 2);
        var (releaseId, model) = release ?? _releases[tenant];
        var instanceId = await database.StartAsync(process, subjectId, releaseId, model);
        Assert.True(await database.RunNextStepAsync(cancellationToken));
        Assert.Equal(ProcessStarts.Waiting, (await database.InstanceAsync(instanceId)).State);
        return (subjectId, instanceId);
    }

    /// <summary>Runs the next due package call item of <paramref name="tenant"/> on <paramref name="worker"/>, which <see cref="BuildWorker"/> built.</summary>
    internal async Task<WorkItemOutcome?> RunCallOnAsync(string tenant, ServiceProvider worker, CancellationToken cancellationToken)
    {
        var item = await Tenant(tenant).ClaimAsync(ProcessStarts.PackageCallWorkItemKind, _lease, cancellationToken);
        return item is null ? null : await worker.GetRequiredService<WorkItemRunner>().ExecuteAsync(item, cancellationToken);
    }

    /// <summary>Reads the request <paramref name="id"/> of <paramref name="tenant"/> with its <c>runnerPid</c> field.</summary>
    public async Task<Record> RequestAsync(string tenant, Guid id)
    {
        var model = _releases[tenant].Model;
        Assert.True(model.TryGetEntity("Request", out var request));
        await using var connection = await Tenant(tenant).DataSource.OpenConnectionAsync();
        return await RecordQueries.GetAsync(connection, model, request, id)
            ?? throw new InvalidOperationException($"No request has the id '{id}'.");
    }

    /// <summary>The <c>runnerPid</c> of the request <paramref name="id"/> of <paramref name="tenant"/>.</summary>
    public async Task<int> RunnerPidAsync(string tenant, Guid id) =>
        (int)(await RequestAsync(tenant, id)).Values["runnerPid"]!.GetValue<long>();

    private static ActivationBindings Bindings(string tenant) =>
        ProcessStepFixture.PackageBindings(tenant, new Uri("https://test.example.test"), allowHttp: false);

    /// <summary>The StepApp files with the field and processes of this fixture and the source of the Python test package.</summary>
    private static async Task<List<ResourceContent>> SourceFilesAsync()
    {
        var files = new List<ResourceContent>();
        foreach (var file in Directory.EnumerateFiles(ProcessStepFixture.StepAppFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(ProcessStepFixture.StepAppFolder, file).Replace('\\', '/');
            var content = await File.ReadAllTextAsync(file);
            if (relative == "entities/request.json")
            {
                var request = JsonNode.Parse(content)!.AsObject();
                request["fields"]!.AsArray().Add(new JsonObject { ["name"] = "runnerPid", ["type"] = "integer" });
                content = request.ToJsonString();
            }

            files.Add(new ResourceContent(relative, content));
        }

        files.Add(new ResourceContent("processes/application-pid.json", PidProcess));
        files.Add(new ResourceContent("processes/application-probe-own-other-host.json", ProbeProcess));
        var package = Path.Combine(AppContext.BaseDirectory, "PythonPackage", "package");
        foreach (var name in new[] { "axis-package.json", "adapter.py" })
        {
            files.Add(new ResourceContent(PackageFolder + name, await File.ReadAllTextAsync(Path.Combine(package, name))));
        }

        return files;
    }

    /// <summary>The built files of the Python test package, which <c>TestPackages.InstallPython</c> lays out in one folder, keyed as a release stores them.</summary>
    private static Dictionary<string, byte[]> BuiltFiles()
    {
        var built = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var folder in new[] { "package", "dependency" })
        {
            var root = Path.Combine(AppContext.BaseDirectory, "PythonPackage", folder);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                built[PackageFolder + Path.GetRelativePath(root, file).Replace('\\', '/')] = File.ReadAllBytes(file);
            }
        }

        return built;
    }
}
