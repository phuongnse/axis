using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
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
/// One tenant database with a copy of the StepApp fixture that pins <c>test-package</c> 1.0.0. The
/// copy adds a <c>total</c> field to <c>Request</c> and two processes: <c>PackageSum</c>, which runs
/// <c>test-package/sum</c> on the divisor and the divisor plus one and writes the result to
/// <c>total</c>, and <c>PackageFail</c>, which runs <c>test-package/fail</c>. The worker has the
/// package installed, its log entries go to <see cref="Logs"/>, and its host is never started, so a
/// test runs the work items itself.
/// </summary>
public sealed class PackageCallFixture : IAsyncLifetime
{
    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "StepApp");

    private const string SumProcess = """
        {
          "id": "5d1f7c52-8a3e-4b6d-9f20-1c4e8a7b3d61",
          "kind": "process",
          "name": "PackageSum",
          "formatVersion": 1,
          "entity": "Request",
          "start": "sum",
          "steps": [
            {
              "name": "sum",
              "type": "operation",
              "operation": "test-package/sum",
              "input": { "a": "divisor", "b": "divisor + 1" },
              "output": { "total": "total" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private const string FailProcess = """
        {
          "id": "c8b2e9a4-3f71-4d05-a6c8-7e92b1d40f35",
          "kind": "process",
          "name": "PackageFail",
          "formatVersion": 1,
          "entity": "Request",
          "start": "fail",
          "steps": [
            {
              "name": "fail",
              "type": "operation",
              "operation": "test-package/fail",
              "input": { "a": "divisor" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private readonly ProcessStepFixture _tenant = new();
    private readonly TemporaryFolder _packages = new TemporaryFolder().Install(1);
    private readonly TemporaryFolder _application = new();
    private ServiceProvider? _worker;
    private ApplicationModel? _model;

    /// <summary>Every log entry of the worker.</summary>
    internal LogCollector Logs { get; } = new();

    /// <summary>The tenant database.</summary>
    public ProcessStepFixture Tenant => _tenant;

    /// <summary>The release of the StepApp copy that pins the package.</summary>
    public Guid ReleaseId { get; private set; }

    public async ValueTask InitializeAsync()
    {
        await _tenant.InitializeAsync();
        foreach (var file in Directory.EnumerateFiles(_appFolder, "*", SearchOption.AllDirectories))
        {
            _application.With(Path.GetRelativePath(_appFolder, file), await File.ReadAllTextAsync(file));
        }

        Edit("application.json", application => application["packages"] = new JsonArray(new JsonObject { ["id"] = TestPackages.Id, ["version"] = "^1.0.0" }));
        Edit("entities/request.json", request => request["fields"]!.AsArray().Add(new JsonObject { ["name"] = "total", ["type"] = "integer" }));
        _application.With("processes/package-sum.json", SumProcess).With("processes/package-fail.json", FailProcess);

        var catalog = PackageCatalog.Load(_packages.Path);
        Assert.Empty(catalog.Diagnostics);
        (ReleaseId, _model) = await _tenant.ActivateAsync(_application.Path, catalog.Versions);

        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Tenants:{_tenant.Tenant}:Hosts:0"] = $"{_tenant.Tenant}.example.test",
                [$"Tenants:{_tenant.Tenant}:ConnectionString"] = _tenant.ConnectionString,
                [PackageCatalog.PathSetting] = _packages.Path,
            })
            .Build();
        _worker = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace))
            .AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory })
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        if (_worker is not null)
        {
            await _worker.DisposeAsync();
        }

        await _tenant.DisposeAsync();
        _application.Dispose();
        _packages.Dispose();
    }

    /// <summary>Runs the next due step item.</summary>
    public Task<WorkItemOutcome?> RunStepAsync(CancellationToken cancellationToken) => RunAsync(ProcessStarts.StepWorkItemKind, cancellationToken);

    /// <summary>Runs the next due package call item.</summary>
    public Task<WorkItemOutcome?> RunCallAsync(CancellationToken cancellationToken) => RunAsync(ProcessStarts.PackageCallWorkItemKind, cancellationToken);

    /// <summary>
    /// Starts <paramref name="process"/> for a new request with the divisor 2 and runs its step,
    /// which stages the package call. Work items that earlier tests left queued are deleted first,
    /// so the test runs only its own. The tests of a class run one after the other.
    /// </summary>
    public async Task<(Guid SubjectId, Guid InstanceId)> StageAsync(string process, CancellationToken cancellationToken)
    {
        await _tenant.ScalarAsync("DELETE FROM axis.process_work_items");
        var subjectId = await _tenant.InsertRequestAsync(500m, 2);
        var instanceId = await _tenant.StartAsync(process, subjectId, ReleaseId, _model);
        Assert.Equal(WorkItemOutcome.Completed, await RunStepAsync(cancellationToken));
        Assert.Equal(ProcessStarts.Waiting, (await _tenant.InstanceAsync(instanceId)).State);
        return (subjectId, instanceId);
    }

    /// <summary>Reads the request <paramref name="id"/> with its <c>total</c> field.</summary>
    public async Task<Record> RequestAsync(Guid id)
    {
        Assert.True((_model ?? throw new InvalidOperationException("The fixture is not initialized.")).TryGetEntity("Request", out var request));
        await using var connection = await _tenant.DataSource.OpenConnectionAsync();
        return await RecordQueries.GetAsync(connection, _model, request, id)
            ?? throw new InvalidOperationException($"No request has the id '{id}'.");
    }

    private void Edit(string relativePath, Action<JsonObject> edit)
    {
        var path = Path.Combine(_application.Path, relativePath);
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(json);
        File.WriteAllText(path, json.ToJsonString());
    }

    private async Task<WorkItemOutcome?> RunAsync(string kind, CancellationToken cancellationToken)
    {
        var runner = (_worker ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<WorkItemRunner>();
        var item = await _tenant.ClaimAsync(kind, _lease, cancellationToken);
        return item is null ? null : await runner.ExecuteAsync(item, cancellationToken);
    }
}
