using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

/// <summary>
/// Two tenants, <c>a</c> and <c>b</c>, each with its own StepApp database, and a
/// <see cref="ConnectorStub"/> that stands in for an external system. Both activate the same copy of
/// StepApp that pins <c>test-package</c> 1.0.0. The copy has the processes <c>PackageProbeDeclared</c>,
/// <c>PackageProbeOtherHost</c>, <c>PackageProbeUndeclaredSecret</c> and <c>PackageProbeSwallow</c>, which
/// run <c>test-package/probe</c> in the mode <c>declared</c>, <c>otherHost</c>, <c>undeclaredSecret</c>
/// and <c>swallow</c>. Each tenant's <c>TestUrl</c> binding points at its own path on the stub,
/// <c>/tenant-a</c> or <c>/tenant-b</c>, and its <c>TestToken</c> secret holds its own token. One worker
/// runs in Development for both tenants and a second one with the same bindings runs as Production, so
/// it refuses the stub's <c>http</c> URL. The worker's log entries go to <see cref="Logs"/>, and the
/// hosts are never started, so a test runs the work items itself.
/// </summary>
public sealed class PackageCapabilityFixture : IAsyncLifetime
{
    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "StepApp");

    private const string ProbeProcessTemplate = """
        {
          "id": "{id}",
          "kind": "process",
          "name": "{name}",
          "formatVersion": 1,
          "entity": "Request",
          "start": "probe",
          "steps": [
            {
              "name": "probe",
              "type": "operation",
              "operation": "test-package/probe",
              "input": { "mode": "'{mode}'" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private readonly Dictionary<string, ProcessStepFixture> _tenants = new(StringComparer.Ordinal)
    {
        ["a"] = new ProcessStepFixture("a"),
        ["b"] = new ProcessStepFixture("b"),
    };

    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Guid ReleaseId, ApplicationModel Model)> _releases = new(StringComparer.Ordinal);
    private readonly TemporaryFolder _packages = new TemporaryFolder().Install("1.0.0");
    private readonly TemporaryFolder _application = new();
    private ConnectorStub? _stub;
    private PackageCatalog? _catalog;
    private ServiceProvider? _development;
    private ServiceProvider? _production;

    public ConnectorStub Stub => _stub ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>Every log entry of both workers.</summary>
    internal LogCollector Logs { get; } = new();

    public async ValueTask InitializeAsync()
    {
        foreach (var tenant in _tenants.Values)
        {
            await tenant.InitializeAsync();
        }

        _stub = await ConnectorStub.StartAsync();
        foreach (var file in Directory.EnumerateFiles(_appFolder, "*", SearchOption.AllDirectories))
        {
            _application.With(Path.GetRelativePath(_appFolder, file), await File.ReadAllTextAsync(file));
        }

        var applicationPath = Path.Combine(_application.Path, "application.json");
        var application = JsonNode.Parse(await File.ReadAllTextAsync(applicationPath))!.AsObject();
        application["packages"] = new JsonArray(new JsonObject { ["id"] = TestPackages.Id, ["version"] = "1.0.0" });
        await File.WriteAllTextAsync(applicationPath, application.ToJsonString());
        _application
            .With("processes/package-probe-declared.json", Probe("PackageProbeDeclared", "3b9e6a14-72c5-4d80-9a1f-5e0c8d2b7a41", "declared"))
            .With("processes/package-probe-other-host.json", Probe("PackageProbeOtherHost", "8d4f1c27-0b63-4e95-a7d2-1f6a9c3e5b08", "otherHost"))
            .With("processes/package-probe-undeclared-secret.json", Probe("PackageProbeUndeclaredSecret", "f2a58d93-6e14-4b07-8c3a-9d7b1e4f0a62", "undeclaredSecret"))
            .With("processes/package-probe-swallow.json", Probe("PackageProbeSwallow", "6c1b7e40-d985-4a32-b6f8-3a0e5d9c2f17", "swallow"));

        var catalog = _catalog = PackageCatalog.Load(_packages.Path);
        Assert.Empty(catalog.Diagnostics);

        var values = new Dictionary<string, string?> { [PackageCatalog.PathSetting] = _packages.Path };
        foreach (var (tenantId, tenant) in _tenants)
        {
            _tokens[tenantId] = $"token-{tenantId}-{Guid.NewGuid():N}";
            var testUrl = $"{_stub.BaseUrl}/tenant-{tenantId}";
            _releases[tenantId] = await tenant.ActivateAsync(
                _application.Path,
                catalog.Versions,
                ProcessStepFixture.PackageBindings(tenantId, new Uri(testUrl), allowHttp: true));
            values[$"Tenants:{tenantId}:Hosts:0"] = $"{tenantId}.example.test";
            values[$"Tenants:{tenantId}:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(tenant.ConnectionString);
            values[$"Tenants:{tenantId}:Bindings:TestUrl:Kind"] = "url";
            values[$"Tenants:{tenantId}:Bindings:TestUrl:Value"] = testUrl;
            values[$"Tenants:{tenantId}:Bindings:TestToken:Kind"] = "secret";
            values[$"Tenants:{tenantId}:Bindings:TestToken:Value"] = _tokens[tenantId];
        }

        var settings = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        _development = Worker(settings, "Development");
        _production = Worker(settings, "Production");
    }

    public async ValueTask DisposeAsync()
    {
        if (_development is not null)
        {
            await _development.DisposeAsync();
        }

        if (_production is not null)
        {
            await _production.DisposeAsync();
        }

        if (_stub is not null)
        {
            await _stub.DisposeAsync();
        }

        foreach (var tenant in _tenants.Values)
        {
            await tenant.DisposeAsync();
        }

        _application.Dispose();
        _packages.Dispose();
    }

    /// <summary>The StepApp database of <paramref name="tenantId"/>.</summary>
    public ProcessStepFixture Tenant(string tenantId) => _tenants[tenantId];

    /// <summary>The value of the <c>TestToken</c> secret of <paramref name="tenantId"/>.</summary>
    public string Token(string tenantId) => _tokens[tenantId];

    /// <summary>Runs the tenant's next due step item on the Development worker.</summary>
    public Task<WorkItemOutcome?> RunStepAsync(string tenantId, CancellationToken cancellationToken) =>
        RunAsync(_development, tenantId, ProcessStarts.StepWorkItemKind, cancellationToken);

    /// <summary>Runs the tenant's next due package call item on the Development worker.</summary>
    public Task<WorkItemOutcome?> RunCallAsync(string tenantId, CancellationToken cancellationToken) =>
        RunAsync(_development, tenantId, ProcessStarts.PackageCallWorkItemKind, cancellationToken);

    /// <summary>Runs the tenant's next due package call item on the Production worker.</summary>
    public Task<WorkItemOutcome?> RunCallOutsideDevelopmentAsync(string tenantId, CancellationToken cancellationToken) =>
        RunAsync(_production, tenantId, ProcessStarts.PackageCallWorkItemKind, cancellationToken);

    /// <summary>
    /// Starts <paramref name="process"/> for a new request of <paramref name="tenantId"/> and runs its
    /// step, which stages the call. Work items that earlier tests left queued are deleted first, so
    /// the test runs only its own. The tests of a class run one after the other.
    /// </summary>
    public async Task<(Guid SubjectId, Guid InstanceId, Guid CallId)> StageAsync(string tenantId, string process, CancellationToken cancellationToken)
    {
        var tenant = Tenant(tenantId);
        var (releaseId, model) = _releases[tenantId];
        await tenant.ScalarAsync("DELETE FROM axis.process_work_items");
        var subjectId = await tenant.InsertRequestAsync(500m, 1);
        var instanceId = await tenant.StartAsync(process, subjectId, releaseId, model);
        Assert.Equal(WorkItemOutcome.Completed, await RunStepAsync(tenantId, cancellationToken));
        Assert.Equal(ProcessStarts.Waiting, (await tenant.InstanceAsync(instanceId)).State);
        var callId = (Guid)(await tenant.ScalarAsync("SELECT id FROM axis.package_calls WHERE process_instance_id = @id", ("id", instanceId)))!;
        return (subjectId, instanceId, callId);
    }

    /// <summary>Compiles the same copy of StepApp again for <paramref name="tenantId"/> and tries to activate it with <paramref name="bindings"/>.</summary>
    internal async Task<ActivationResult> TryActivateAsync(string tenantId, ActivationBindings bindings) =>
        (await Tenant(tenantId).TryActivateAsync(_application.Path, (_catalog ?? throw new InvalidOperationException("The fixture is not initialized.")).Versions, bindings)).Activated;

    private static string Probe(string name, string id, string mode) =>
        ProbeProcessTemplate
            .Replace("{id}", id, StringComparison.Ordinal)
            .Replace("{name}", name, StringComparison.Ordinal)
            .Replace("{mode}", mode, StringComparison.Ordinal);

    private ServiceProvider Worker(IConfiguration settings, string environment) =>
        new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace))
            .AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = environment, ContentRootPath = AppContext.BaseDirectory })
            .BuildServiceProvider();

    private async Task<WorkItemOutcome?> RunAsync(ServiceProvider? worker, string tenantId, string kind, CancellationToken cancellationToken)
    {
        var runner = (worker ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<WorkItemRunner>();
        var item = await Tenant(tenantId).ClaimAsync(kind, _lease, cancellationToken);
        return item is null ? null : await runner.ExecuteAsync(item, cancellationToken);
    }
}
