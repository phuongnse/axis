using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
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
/// One tenant database with a copy of the StepApp fixture that pins <c>test-package</c> 1.0.0. The
/// copy adds a <c>total</c> field to <c>Request</c> and these processes: <c>PackageSum</c>, which runs
/// <c>test-package/sum</c> on the divisor and the divisor plus one and writes the result to
/// <c>total</c>, <c>PackageFail</c>, which runs <c>test-package/fail</c>, <c>PackageFlaky</c> and
/// <c>PackageFlakyShort</c>, which run <c>test-package/flaky</c> with the divisor as the number of
/// failures and 3 or 2 attempts, <c>PackageSlow</c>, which runs <c>test-package/slow</c>, and
/// <c>PackageDescribe</c>, which waits on a task, then runs <c>test-package/describe</c> and writes its
/// <c>version</c> to <c>packageVersion</c>. Versions 1.0.0 and 1.1.0 of the package are installed. The
/// worker has them installed, its log entries go to <see cref="Logs"/>, and its host is never started,
/// so a test runs the work items itself. A second worker has no package installed.
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

    private const string FlakyProcessTemplate = """
        {
          "id": "{id}",
          "kind": "process",
          "name": "{name}",
          "formatVersion": 1,
          "entity": "Request",
          "start": "flaky",
          "steps": [
            {
              "name": "flaky",
              "type": "operation",
              "operation": "test-package/flaky",
              "input": { "failures": "divisor" },
              "output": { "attempts": "total" },
              "retry": { "attempts": {attempts}, "backoff": "PT10S" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private const string SlowProcess = """
        {
          "id": "e4a7d1b9-6c25-4f83-b0d6-2a9c5e8f7143",
          "kind": "process",
          "name": "PackageSlow",
          "formatVersion": 1,
          "entity": "Request",
          "start": "slow",
          "steps": [
            {
              "name": "slow",
              "type": "operation",
              "operation": "test-package/slow",
              "input": { "a": "divisor" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private const string DescribeProcess = """
        {
          "id": "7e3a9c14-5b02-4d68-8f71-0a6d2c9e4b35",
          "kind": "process",
          "name": "PackageDescribe",
          "formatVersion": 1,
          "entity": "Request",
          "start": "review",
          "steps": [
            {
              "name": "review",
              "type": "task",
              "label": { "textKey": "request.approve" },
              "assignee": { "role": "finance" },
              "form": "RequestReview",
              "outcomes": [{ "name": "approve", "label": { "textKey": "task.approve" }, "next": "describe" }]
            },
            {
              "name": "describe",
              "type": "operation",
              "operation": "test-package/describe",
              "input": { "name": "title" },
              "output": { "version": "packageVersion" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private static readonly ActivationBindings _bindings =
        ProcessStepFixture.PackageBindings(ProcessStepFixture.TenantId, new Uri("https://test.example.test"), allowHttp: false);

    private readonly ProcessStepFixture _tenant = new();
    private readonly TemporaryFolder _packages = new TemporaryFolder().Install("1.0.0", "1.1.0");
    private readonly TemporaryFolder _application = new();
    private readonly TemporaryFolder _noPackages = new();
    private PackageCatalog? _catalog;
    private ServiceProvider? _worker;
    private ServiceProvider? _workerWithoutPackages;
    private ApplicationModel? _model;

    /// <summary>Every log entry of the worker.</summary>
    internal LogCollector Logs { get; } = new();

    /// <summary>The tenant database.</summary>
    public ProcessStepFixture Tenant => _tenant;

    /// <summary>The release of the StepApp copy that pins the package.</summary>
    public Guid ReleaseId { get; private set; }

    /// <summary>The model of the release <see cref="ReleaseId"/>.</summary>
    public ApplicationModel Model => _model ?? throw new InvalidOperationException("The fixture is not initialized.");

    public async ValueTask InitializeAsync()
    {
        await _tenant.InitializeAsync();
        foreach (var file in Directory.EnumerateFiles(_appFolder, "*", SearchOption.AllDirectories))
        {
            _application.With(Path.GetRelativePath(_appFolder, file), await File.ReadAllTextAsync(file));
        }

        Edit("application.json", application => application["packages"] = new JsonArray(new JsonObject { ["id"] = TestPackages.Id, ["version"] = "1.0.0" }));
        Edit("entities/request.json", request =>
        {
            var fields = request["fields"]!.AsArray();
            fields.Add(new JsonObject { ["name"] = "total", ["type"] = "integer" });
            fields.Add(new JsonObject { ["name"] = "packageVersion", ["type"] = "text" });
        });
        _application
            .With("processes/package-sum.json", SumProcess)
            .With("processes/package-fail.json", FailProcess)
            .With("processes/package-flaky.json", Flaky("PackageFlaky", "b3f8a2c1-7d46-4e09-8a15-6c0d9e2f4b78", 3))
            .With("processes/package-flaky-short.json", Flaky("PackageFlakyShort", "a91c4e7d-2b58-4f36-9d80-5e3a7b1c6f24", 2))
            .With("processes/package-slow.json", SlowProcess)
            .With("processes/package-describe.json", DescribeProcess);

        var catalog = _catalog = PackageCatalog.Load(_packages.Path);
        Assert.Empty(catalog.Diagnostics);
        (ReleaseId, _model) = await _tenant.ActivateAsync(_application.Path, catalog.Versions, _bindings);

        _worker = BuildWorker(_packages.Path);
        _workerWithoutPackages = BuildWorker(_noPackages.Path);
    }

    public async ValueTask DisposeAsync()
    {
        if (_worker is not null)
        {
            await _worker.DisposeAsync();
        }

        if (_workerWithoutPackages is not null)
        {
            await _workerWithoutPackages.DisposeAsync();
        }

        await _tenant.DisposeAsync();
        _application.Dispose();
        _noPackages.Dispose();
        _packages.Dispose();
    }

    /// <summary>Runs the next due step item.</summary>
    public Task<WorkItemOutcome?> RunStepAsync(CancellationToken cancellationToken) => RunAsync(ProcessStarts.StepWorkItemKind, cancellationToken);

    /// <summary>Runs the next due package call item.</summary>
    public Task<WorkItemOutcome?> RunCallAsync(CancellationToken cancellationToken) => RunAsync(ProcessStarts.PackageCallWorkItemKind, cancellationToken);

    /// <summary>Runs the next due package call item on a worker that holds no package version.</summary>
    public Task<WorkItemOutcome?> RunCallWithoutPackagesAsync(CancellationToken cancellationToken) =>
        RunAsync(ProcessStarts.PackageCallWorkItemKind, cancellationToken, _workerWithoutPackages);

    /// <summary>Makes the work item of the call <paramref name="callId"/> due now.</summary>
    public Task MakeDueAsync(Guid callId) =>
        _tenant.ScalarAsync("UPDATE axis.process_work_items SET due_at = now() WHERE package_call_id = @id", ("id", callId));

    /// <summary>The due time of the work item of the call <paramref name="callId"/>, or <see langword="null"/> when it has none.</summary>
    public async Task<DateTime?> DueAtAsync(Guid callId) =>
        (DateTime?)await _tenant.ScalarAsync("SELECT due_at FROM axis.process_work_items WHERE package_call_id = @id", ("id", callId));

    /// <summary>The current time by the clock of the tenant database.</summary>
    public async Task<DateTime> DatabaseNowAsync() =>
        (DateTime)(await _tenant.ScalarAsync("SELECT now()"))!;

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

    /// <summary>
    /// Compiles and activates a copy of the StepApp copy with one more file, <paramref name="relativePath"/>
    /// holding <paramref name="content"/>, against the installed package. The new release becomes the
    /// active one. Returns its id.
    /// </summary>
    public async Task<Guid> ActivateCopyWithAsync(string relativePath, string content)
    {
        var catalog = _catalog ?? throw new InvalidOperationException("The fixture is not initialized.");
        using var folder = new TemporaryFolder();
        foreach (var file in Directory.EnumerateFiles(_application.Path, "*", SearchOption.AllDirectories))
        {
            folder.With(Path.GetRelativePath(_application.Path, file), await File.ReadAllTextAsync(file));
        }

        folder.With(relativePath, content);
        var (releaseId, _) = await _tenant.ActivateAsync(folder.Path, catalog.Versions, _bindings);
        Assert.NotEqual(ReleaseId, releaseId);
        return releaseId;
    }

    /// <summary>
    /// Compiles and activates a copy of the StepApp copy that pins <c>test-package</c> exactly at
    /// <paramref name="version"/>, against the installed packages. The new release becomes the active one.
    /// Returns its id and model.
    /// </summary>
    public async Task<(Guid ReleaseId, ApplicationModel Model)> ActivatePinnedAsync(string version)
    {
        var catalog = _catalog ?? throw new InvalidOperationException("The fixture is not initialized.");
        using var folder = new TemporaryFolder();
        foreach (var file in Directory.EnumerateFiles(_application.Path, "*", SearchOption.AllDirectories))
        {
            folder.With(Path.GetRelativePath(_application.Path, file), await File.ReadAllTextAsync(file));
        }

        var path = Path.Combine(folder.Path, "application.json");
        var application = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        application["packages"]!.AsArray()[0]!["version"] = version;
        await File.WriteAllTextAsync(path, application.ToJsonString());

        var activated = await _tenant.ActivateAsync(folder.Path, catalog.Versions, _bindings);
        Assert.NotEqual(ReleaseId, activated.ReleaseId);
        return activated;
    }

    /// <summary>
    /// Completes the open task of <paramref name="instanceId"/> with the outcome <c>approve</c> as the
    /// task endpoint does, which resumes the instance on the step <c>describe</c>.
    /// </summary>
    public async Task CompleteTaskAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var taskId = Assert.Single(await _tenant.TasksAsync(instanceId)).Id;
        await using var connection = await _tenant.DataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var task = await TaskQueries.LockAsync(transaction, taskId, cancellationToken);
        Assert.NotNull(task);
        Assert.True(await ProcessTasks.TryCompleteAsync(transaction, taskId, "approve", "maria", cancellationToken));
        await ProcessTasks.ResumeAsync(
            transaction,
            task,
            new TaskCompletion("approve", "describe", "maria", "Maria", null, null, []),
            _tenant.Tenant,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Reads the request <paramref name="id"/> with its <c>total</c> and <c>packageVersion</c> fields.</summary>
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

    private static string Flaky(string name, string id, int attempts) =>
        FlakyProcessTemplate
            .Replace("{id}", id, StringComparison.Ordinal)
            .Replace("{name}", name, StringComparison.Ordinal)
            .Replace("{attempts}", attempts.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private ServiceProvider BuildWorker(string packagesPath)
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Tenants:{_tenant.Tenant}:Hosts:0"] = $"{_tenant.Tenant}.example.test",
                [$"Tenants:{_tenant.Tenant}:ConnectionString"] = _tenant.ConnectionString,
                [$"Tenants:{_tenant.Tenant}:Bindings:TestUrl:Kind"] = "url",
                [$"Tenants:{_tenant.Tenant}:Bindings:TestUrl:Value"] = "https://test.example.test",
                [$"Tenants:{_tenant.Tenant}:Bindings:TestToken:Kind"] = "secret",
                [$"Tenants:{_tenant.Tenant}:Bindings:TestToken:Value"] = "test-token",
                [PackageCatalog.PathSetting] = packagesPath,
            })
            .Build();
        return new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace))
            .AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory })
            .BuildServiceProvider();
    }

    private async Task<WorkItemOutcome?> RunAsync(string kind, CancellationToken cancellationToken, ServiceProvider? worker = null)
    {
        var runner = (worker ?? _worker ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<WorkItemRunner>();
        var item = await _tenant.ClaimAsync(kind, _lease, cancellationToken);
        return item is null ? null : await runner.ExecuteAsync(item, cancellationToken);
    }
}
