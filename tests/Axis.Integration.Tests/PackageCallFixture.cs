using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Records;
using Axis.PackageHost;
using Axis.Policy;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Tenancy;
using Axis.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Record = Axis.Data.Records.Record;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database with a copy of the StepApp fixture that pins <c>test-package</c> 1.0.0. The
/// copy adds a <c>total</c> field to <c>Request</c> and these processes: <c>PackageSum</c>, which runs
/// <c>test-package/sum</c> on the divisor and the divisor plus one and writes the result to
/// <c>total</c>, <c>PackageFail</c>, which runs <c>test-package/fail</c>, <c>PackageFlaky</c> and
/// <c>PackageFlakyShort</c>, which run <c>test-package/flaky</c> with the divisor as the number of
/// failures and 3 or 2 attempts, <c>PackageSlow</c>, which runs <c>test-package/slow</c>,
/// <c>PackageSlowIdempotent</c>, <c>PackageExit</c> and <c>PackageExitIdempotent</c>, which run the
/// operations <c>slowIdempotent</c>, <c>exit</c> and <c>exitIdempotent</c> that the copy of the package adds
/// to its manifest (a slow operation that is idempotent and an operation that ends its own runner process,
/// idempotent or not), and <c>PackageDescribe</c>, which waits on a task, then runs <c>test-package/describe</c> and writes its
/// <c>version</c> to <c>packageVersion</c>. <c>PackageNodeSum</c> and <c>PackageNodeGreet</c> run <c>node-probe-package/sum</c> on the
/// same inputs as <c>PackageSum</c> and write its result to <c>total</c>, and <c>node-test-package/greet</c> on the title and write its
/// greeting to <c>greeting</c>. The copy also pins <c>python-test-package</c> 1.0.0, whose <c>greet</c> and
/// <c>flaky</c> operations the processes <c>PythonGreet</c> and <c>PythonFlaky</c> run: <c>PythonGreet</c> writes the greeting for the
/// title to <c>comment</c>, and <c>PythonFlaky</c> fails with a retryable error and has 2 attempts. Versions 1.0.0 and 1.1.0 of
/// <c>test-package</c> are installed, and version 1.0.0 of the two Node.js packages, with the dependencies of
/// <c>node-test-package</c> installed by <c>npm ci</c>, so <c>npm</c> must be on the PATH, and version 1.0.0 of
/// <c>python-test-package</c>. The copy also pins the sample package <c>phone-numbers</c> 1.0.0, installed from the folder
/// <c>SamplePythonPackage</c> of the test output, which links <c>artifacts/extensions/phone-numbers/1.0.0</c>. It adds a
/// <c>phone</c> field to <c>Request</c> and the processes <c>SamplePhoneNumber</c> and <c>SamplePhoneNumberUnparsed</c>, which run
/// <c>phone-numbers/normalize</c> on a US number and on a text that is no number and write the result to <c>phone</c>. The
/// worker has them installed, its log entries go to <see cref="Logs"/>, and its host is never started,
/// so a test runs the work items itself. A second worker has no package installed. With <c>throughPgBouncer</c>, the
/// workers connect to the tenant database as the runtime role through <see cref="PgBouncerServer"/>, and the fixture claims
/// work items through it too. Everything else, such as starting an instance and reading a record, uses the schema role directly.
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

    private const string OperationProcessTemplate = """
        {
          "id": "{id}",
          "kind": "process",
          "name": "{name}",
          "formatVersion": 1,
          "entity": "Request",
          "start": "{operation}",
          "steps": [
            {
              "name": "{operation}",
              "type": "operation",
              "operation": "test-package/{operation}",
              "input": { "a": "divisor" },
              "retry": { "attempts": 2, "backoff": "PT10S" },
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

    private const string NodeSumProcess = """
        {
          "id": "3a8e5c91-d204-4b7f-86e3-9f1c7d02a5b8",
          "kind": "process",
          "name": "PackageNodeSum",
          "formatVersion": 1,
          "entity": "Request",
          "start": "sum",
          "steps": [
            {
              "name": "sum",
              "type": "operation",
              "operation": "node-probe-package/sum",
              "input": { "a": "divisor", "b": "divisor + 1" },
              "output": { "total": "total" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private const string NodeGreetProcess = """
        {
          "id": "b6d27f40-1e93-4a58-9c0b-5e8a3d7f1c26",
          "kind": "process",
          "name": "PackageNodeGreet",
          "formatVersion": 1,
          "entity": "Request",
          "start": "greet",
          "steps": [
            {
              "name": "greet",
              "type": "operation",
              "operation": "node-test-package/greet",
              "input": { "name": "title" },
              "output": { "greeting": "greeting" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private const string PythonGreetProcess = """
        {
          "id": "0b8e5d27-91c4-4a3f-b6e0-7d2a4c9f1e58",
          "kind": "process",
          "name": "PythonGreet",
          "formatVersion": 1,
          "entity": "Request",
          "start": "greet",
          "steps": [
            {
              "name": "greet",
              "type": "operation",
              "operation": "python-test-package/greet",
              "input": { "name": "title" },
              "output": { "greeting": "comment" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private const string PythonFlakyProcess = """
        {
          "id": "6f3c9a81-d5e2-4b70-8a14-c0e7b2d96a35",
          "kind": "process",
          "name": "PythonFlaky",
          "formatVersion": 1,
          "entity": "Request",
          "start": "flaky",
          "steps": [
            {
              "name": "flaky",
              "type": "operation",
              "operation": "python-test-package/flaky",
              "input": { "a": "title" },
              "retry": { "attempts": 2, "backoff": "PT10S" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private const string PhoneNumberProcessTemplate = """
        {
          "id": "{id}",
          "kind": "process",
          "name": "{name}",
          "formatVersion": 1,
          "entity": "Request",
          "start": "normalize",
          "steps": [
            {
              "name": "normalize",
              "type": "operation",
              "operation": "phone-numbers/normalize",
              "input": { "number": "'{number}'", "region": "'US'" },
              "output": { "number": "phone" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private static readonly ActivationBindings _bindings =
        ProcessStepFixture.PackageBindings(ProcessStepFixture.TenantId, new Uri("https://test.example.test"), allowHttp: false);

    private const string SamplePhoneNumbersId = "phone-numbers";

    private readonly ProcessStepFixture _tenant = new();
    private readonly TemporaryFolder _packages = new TemporaryFolder().Install("1.0.0", "1.1.0").InstallPython();
    private readonly TemporaryFolder _application = new();
    private readonly TemporaryFolder _noPackages = new();
    private PackageCatalog? _catalog;
    private readonly bool _throughPgBouncer;
    private NpgsqlDataSource? _claimDataSource;
    private ServiceProvider? _worker;
    private string? _runtime;
    private ServiceProvider? _workerWithoutPackages;
    private ApplicationModel? _model;

    public PackageCallFixture()
    {
    }

    internal PackageCallFixture(bool throughPgBouncer) => _throughPgBouncer = throughPgBouncer;

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

        foreach (var version in new[] { "1.0.0", "1.1.0" })
        {
            _packages.EditManifest(version, manifest =>
            {
                var operations = manifest["operations"]!.AsArray();
                operations.Add(ManifestOperation("slowIdempotent", "TestPackage.SlowOperation", true, "PT1S"));
                operations.Add(ManifestOperation("exit", "TestPackage.ExitOperation", false));
                operations.Add(ManifestOperation("exitIdempotent", "TestPackage.ExitOperation", true));
            });
        }

        Edit("application.json", application => application["packages"] = new JsonArray(
            new JsonObject { ["id"] = TestPackages.Id, ["version"] = "1.0.0" },
            new JsonObject { ["id"] = TestPackages.PythonId, ["version"] = "1.0.0" },
            new JsonObject { ["id"] = SamplePhoneNumbersId, ["version"] = "1.0.0" },
            new JsonObject { ["id"] = "node-probe-package", ["version"] = "1.0.0" },
            new JsonObject { ["id"] = "node-test-package", ["version"] = "1.0.0" }));
        Edit("entities/request.json", request =>
        {
            var fields = request["fields"]!.AsArray();
            fields.Add(new JsonObject { ["name"] = "total", ["type"] = "integer" });
            fields.Add(new JsonObject { ["name"] = "packageVersion", ["type"] = "text" });
            fields.Add(new JsonObject { ["name"] = "greeting", ["type"] = "text" });
            fields.Add(new JsonObject { ["name"] = "phone", ["type"] = "text" });
        });
        _application
            .With("processes/package-sum.json", SumProcess)
            .With("processes/package-fail.json", FailProcess)
            .With("processes/package-flaky.json", Flaky("PackageFlaky", "b3f8a2c1-7d46-4e09-8a15-6c0d9e2f4b78", 3))
            .With("processes/package-flaky-short.json", Flaky("PackageFlakyShort", "a91c4e7d-2b58-4f36-9d80-5e3a7b1c6f24", 2))
            .With("processes/package-slow.json", SlowProcess)
            .With("processes/package-slow-idempotent.json", OperationProcess("PackageSlowIdempotent", "slowIdempotent", "2f6b9d03-4a1e-4c78-b5d2-8e07a3c91f46"))
            .With("processes/package-exit.json", OperationProcess("PackageExit", "exit", "9c14e8a7-0d35-4b62-a1f9-3b7d5e208c14"))
            .With("processes/package-exit-idempotent.json", OperationProcess("PackageExitIdempotent", "exitIdempotent", "d70a3f5e-8b19-4e26-9c43-1f5a8b6e2d97"))
            .With("processes/package-describe.json", DescribeProcess)
            .With("processes/package-node-sum.json", NodeSumProcess)
            .With("processes/package-node-greet.json", NodeGreetProcess)
            .With("processes/python-greet.json", PythonGreetProcess)
            .With("processes/python-flaky.json", PythonFlakyProcess)
            .With("processes/sample-phone-number.json", PhoneNumberProcess("SamplePhoneNumber", "8c4e1a97-3d60-4b2f-a5e8-92f7c0d13b64", "(650) 253-0000"))
            .With("processes/sample-phone-number-unparsed.json", PhoneNumberProcess("SamplePhoneNumberUnparsed", "e17b5d38-0a94-4c62-8f3d-6b2c9a4e7d01", "not a number"));

        var nodeProbe = Path.Combine(_packages.Path, "node-probe-package", "1.0.0");
        PackageBuildCommand.Copy(Path.Combine(AppContext.BaseDirectory, "NodeProbePackage"), nodeProbe);
        var installed = await NodeInstall.InstallAsync(
            Path.Combine(AppContext.BaseDirectory, "NodeTestPackage"),
            Path.Combine(_packages.Path, "node-test-package", "1.0.0"),
            CancellationToken.None);
        Assert.Null(installed.Failure);

        var samplePhoneNumbers = Path.Combine(AppContext.BaseDirectory, "SamplePythonPackage");
        if (!File.Exists(Path.Combine(samplePhoneNumbers, "axis-package.json")))
        {
            throw new InvalidOperationException("The sample package phone-numbers is not installed in artifacts/extensions. Run scripts/build.sh or scripts/integration.sh.");
        }

        PackageBuildCommand.Copy(samplePhoneNumbers, Path.Combine(_packages.Path, SamplePhoneNumbersId, "1.0.0"));

        var catalog = _catalog = PackageCatalog.Load(_packages.Path);
        Assert.Empty(catalog.Diagnostics);
        (ReleaseId, _model) = await _tenant.ActivateAsync(_application.Path, catalog.Versions, _bindings);

        var runtime = _throughPgBouncer
            ? await PgBouncerServer.RuntimeConnectionStringAsync(_tenant.ConnectionString)
            : PostgreSqlServer.RuntimeConnectionString(_tenant.ConnectionString);
        if (_throughPgBouncer)
        {
            _claimDataSource = NpgsqlDataSource.Create(TenantPoolSettings.Default.Apply(runtime));
        }

        _runtime = runtime;
        _worker = BuildWorker(_packages.Path, runtime);
        _workerWithoutPackages = BuildWorker(_noPackages.Path, runtime);
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

        if (_claimDataSource is not null)
        {
            await _claimDataSource.DisposeAsync();
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

    /// <summary>The folder that acts as <c>Extensions:Path</c>, with versions 1.0.0 and 1.1.0 of the test package and version 1.0.0 of the Python test package and of the sample package <c>phone-numbers</c>.</summary>
    internal string PackagesPath => _packages.Path;

    /// <summary>
    /// Runs the next due package call item on a worker that has the installed packages and the settings in
    /// <paramref name="extra"/>, which add to or replace the fixture worker's. The worker is disposed afterwards.
    /// </summary>
    internal async Task<WorkItemOutcome?> RunCallOnAsync(IReadOnlyDictionary<string, string?> extra, CancellationToken cancellationToken)
    {
        await using var worker = BuildWorkerWith(extra);
        return await RunCallOnAsync(worker, cancellationToken);
    }

    /// <summary>
    /// A worker that has the installed packages and the settings in <paramref name="extra"/>, which add to or replace the
    /// fixture worker's. The caller disposes it, so a test can run several calls on one worker.
    /// </summary>
    internal ServiceProvider BuildWorkerWith(IReadOnlyDictionary<string, string?> extra) =>
        BuildWorker(_packages.Path, _runtime ?? throw new InvalidOperationException("The fixture is not initialized."), extra);

    /// <summary>Runs the next due package call item on <paramref name="worker"/>, which <see cref="BuildWorkerWith"/> built.</summary>
    internal Task<WorkItemOutcome?> RunCallOnAsync(ServiceProvider worker, CancellationToken cancellationToken) =>
        RunAsync(ProcessStarts.PackageCallWorkItemKind, cancellationToken, worker);

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
    /// Starts <paramref name="process"/> for a new request with the divisor <paramref name="divisor"/> and runs its step,
    /// which stages the package call. Work items that earlier tests left queued are deleted first,
    /// so the test runs only its own. The tests of a class run one after the other.
    /// </summary>
    public async Task<(Guid SubjectId, Guid InstanceId)> StageAsync(string process, CancellationToken cancellationToken, int divisor = 2)
    {
        await _tenant.ScalarAsync("DELETE FROM axis.process_work_items");
        var subjectId = await _tenant.InsertRequestAsync(500m, divisor);
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
    /// Retries the failed instance <paramref name="instanceId"/> of <paramref name="process"/> as an operator
    /// with full access would, which queues its step again. Asserts the retry and commits it.
    /// </summary>
    public async Task RetryAsync(string process, Guid instanceId, CancellationToken cancellationToken)
    {
        var model = Model;
        Assert.True(model.TryGetProcess(process, out var processModel));
        Assert.True(model.TryGetEntity(processModel.Entity!.Name, out var entity));
        await using var connection = await _tenant.DataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var result = await ProcessRetryCommand.RetryAsync(
            connection,
            transaction,
            _tenant.Tenant,
            model,
            processModel,
            entity,
            instanceId,
            new RecordAccess(true, null),
            "maria",
            "Fixed",
            null,
            NullLoggerFactory.Instance,
            cancellationToken);
        Assert.Equal(ProcessRetryOutcome.Retried, result.Outcome);
        await transaction.CommitAsync(cancellationToken);
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
            new TaskCompletion("approve", "describe", "maria", null, null, []),
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

    private static string OperationProcess(string name, string operation, string id) =>
        OperationProcessTemplate
            .Replace("{id}", id, StringComparison.Ordinal)
            .Replace("{name}", name, StringComparison.Ordinal)
            .Replace("{operation}", operation, StringComparison.Ordinal);

    private static string PhoneNumberProcess(string name, string id, string number) =>
        PhoneNumberProcessTemplate
            .Replace("{id}", id, StringComparison.Ordinal)
            .Replace("{name}", name, StringComparison.Ordinal)
            .Replace("{number}", number, StringComparison.Ordinal);

    private static JsonObject ManifestOperation(string name, string type, bool idempotent, string? timeout = null)
    {
        var operation = new JsonObject
        {
            ["name"] = name,
            ["type"] = type,
            ["idempotent"] = idempotent,
        };
        if (timeout is not null)
        {
            operation["timeout"] = timeout;
        }

        operation["input"] = new JsonArray(new JsonObject { ["name"] = "a", ["type"] = "integer", ["required"] = true });
        return operation;
    }

    private ServiceProvider BuildWorker(string packagesPath, string runtimeConnectionString, IReadOnlyDictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>
        {
            [$"Tenants:{_tenant.Tenant}:Hosts:0"] = $"{_tenant.Tenant}.example.test",
            [$"Tenants:{_tenant.Tenant}:ConnectionString"] = runtimeConnectionString,
            [$"Tenants:{_tenant.Tenant}:Bindings:TestUrl:Kind"] = "url",
            [$"Tenants:{_tenant.Tenant}:Bindings:TestUrl:Value"] = "https://test.example.test",
            [$"Tenants:{_tenant.Tenant}:Bindings:TestToken:Kind"] = "secret",
            [$"Tenants:{_tenant.Tenant}:Bindings:TestToken:Value"] = "test-token",
            [PackageCatalog.PathSetting] = packagesPath,
            [PackageIsolation.IsolationSetting] = "inProcess",
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var settings = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace))
            .AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = "Development", ContentRootPath = AppContext.BaseDirectory })
            .BuildServiceProvider();
    }

    private async Task<WorkItemOutcome?> RunAsync(string kind, CancellationToken cancellationToken, ServiceProvider? worker = null)
    {
        var runner = (worker ?? _worker ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<WorkItemRunner>();
        var item = _claimDataSource is null ? await _tenant.ClaimAsync(kind, _lease, cancellationToken) : await ClaimThroughPgBouncerAsync(kind, cancellationToken);
        return item is null ? null : await runner.ExecuteAsync(item, cancellationToken);
    }

    private async Task<ClaimedWorkItem?> ClaimThroughPgBouncerAsync(string kind, CancellationToken cancellationToken)
    {
        await using var connection = await _claimDataSource!.OpenConnectionAsync(cancellationToken);
        return await WorkItemQueue.ClaimAsync(connection, _tenant.Tenant, [kind], _lease, cancellationToken);
    }
}
