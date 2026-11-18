using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// Compiles and activates releases of one application whose own package <c>tools</c> is customer-hosted, in a tenant database, for the tests of
/// the registration check and the take in <c>docs/reference/extension-packages.md#customer-hosted-runners</c>. A release can also hold a process whose
/// step calls the package, and a call that staging would leave for it, see <c>docs/reference/process-execution.md#staging-a-package-call</c>.
/// A release can also hold the entity <c>Order</c> and the process <c>OrderSum</c>, whose subject is an order that the step writes its output to.
/// A release can also pin an installed package with a <c>probe</c> operation, such as <c>test-package</c>, as customer-hosted, with the entity <c>Probe</c> and the process <c>ProbeCall</c>.
/// </summary>
internal sealed class CustomerHostedReleases
{
    private readonly string _name = $"Registered{Guid.NewGuid():N}";

    /// <summary>The id of the application whose releases this activates.</summary>
    public Guid ApplicationId { get; } = Guid.NewGuid();

    /// <summary>The manifest of the own package <c>tools</c> at <paramref name="version"/>, with the one operation <paramref name="operation"/>.</summary>
    public static string ToolsManifest(string version, string operation) =>
        $$"""
        {
          "id": "tools",
          "version": "{{version}}",
          "label": "Tools",
          "language": "python",
          "runner": "customer",
          "adapter": "adapter.py",
          "lock": "requirements.txt",
          "operations": [
            {
              "name": "{{operation}}",
              "type": "adapter.{{operation}}",
              "idempotent": true,
              "input": [
                { "name": "a", "type": "integer", "required": true },
                { "name": "b", "type": "integer", "required": true }
              ],
              "output": [{ "name": "total", "type": "integer" }]
            }
          ]
        }
        """;

    private static readonly Guid _toolsSumProcessId = Guid.Parse("4b9d6e21-7c58-4a03-b1f4-8e2a5d70c963");

    // A process without a subject, which takes its input from its parameters, so the release needs no entity.
    private static readonly string _toolsSumProcess =
        $$"""
        {
          "id": "{{_toolsSumProcessId}}",
          "kind": "process",
          "name": "ToolsSum",
          "formatVersion": 1,
          "parameters": [{ "name": "first", "type": "integer" }, { "name": "second", "type": "integer" }],
          "start": "sum",
          "steps": [
            { "name": "sum", "type": "operation", "operation": "tools/sum", "input": { "a": "first", "b": "second" }, "next": "done" },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    /// <summary>The id of the entity <c>Order</c>, which names its table.</summary>
    public static Guid OrderEntityId { get; } = Guid.Parse("a3c1f5d7-2e84-4b69-8d10-6f7b9e2c4a58");


    private static readonly Guid _orderSumProcessId = Guid.Parse("e5a7b9c1-4d26-4f83-9b05-3c8d1a6e7f24");

    private static readonly string _orderEntity =
        $$"""
        {
          "id": "{{OrderEntityId}}",
          "kind": "entity",
          "name": "Order",
          "formatVersion": 1,
          "fields": [
            { "name": "a", "type": "integer" },
            { "name": "b", "type": "integer" },
            { "name": "total", "type": "integer" }
          ]
        }
        """;

    // A process whose subject is an order, so the step's output is written to the record.
    private static readonly string _orderSumProcess =
        $$"""
        {
          "id": "{{_orderSumProcessId}}",
          "kind": "process",
          "name": "OrderSum",
          "formatVersion": 1,
          "entity": "Order",
          "start": "sum",
          "steps": [
            { "name": "sum", "type": "operation", "operation": "tools/sum", "input": { "a": "a", "b": "b" }, "output": { "total": "total" }, "next": "done" },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    /// <summary>The id of the entity <c>Probe</c>, which names its table.</summary>
    public static Guid ProbeEntityId { get; } = Guid.Parse("7d2e4a60-9b13-4c85-a1f7-2e6c8b0d5f39");

    private static readonly Guid _probeCallProcessId = Guid.Parse("c8f0a2d4-6b31-4e97-8a52-1d9e3b7c5f60");

    private static readonly string _probeEntity =
        $$"""
        {
          "id": "{{ProbeEntityId}}",
          "kind": "entity",
          "name": "Probe",
          "formatVersion": 1,
          "fields": [
            { "name": "mode", "type": "text" },
            { "name": "status", "type": "integer" }
          ]
        }
        """;

    // A process whose subject is a probe: the step passes its mode to the probe of the package and writes the status it gets to the record.
    private static string ProbeProcess(string package) =>
        $$"""
        {
          "id": "{{_probeCallProcessId}}",
          "kind": "process",
          "name": "ProbeCall",
          "formatVersion": 1,
          "entity": "Probe",
          "start": "probe",
          "steps": [
            { "name": "probe", "type": "operation", "operation": "{{package}}/probe", "input": { "mode": "mode" }, "output": { "status": "status" }, "next": "done" },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    /// <summary>
    /// Compiles the application with the own package files, the entity <c>Order</c> and the process <c>OrderSum</c>, and activates it with the
    /// release activator, so the table of <c>Order</c> exists. The caller grants the runtime role again, to reach the table.
    /// </summary>
    public Task<Guid> ActivateOwnWithOrderProcessAsync(string connectionString, string tenantId, string manifest, CancellationToken cancellationToken) =>
        ActivateWithTablesAsync(
            connectionString,
            tenantId,
            [],
            "[]",
            cancellationToken,
            ("packages/tools/axis-package.json", manifest),
            ("entities/order.json", _orderEntity),
            ("processes/order-sum.json", _orderSumProcess));

    /// <summary>
    /// Compiles the application with the installed <paramref name="package"/> 1.0.0 of <paramref name="catalog"/>, pinned as customer-hosted,
    /// the entity <c>Probe</c> and the process <c>ProbeCall</c>, whose step <c>probe</c> runs <c>&lt;package&gt;/probe</c> with the record's <c>mode</c>
    /// and writes its <c>status</c> to the record. It activates the release with the release activator, so the table of <c>Probe</c> exists. The caller
    /// grants the runtime role again, to reach the table.
    /// </summary>
    public Task<Guid> ActivateInstalledProbeAsync(string connectionString, string tenantId, PackageCatalog catalog, CancellationToken cancellationToken, string package = TestPackages.Id) =>
        ActivateWithTablesAsync(
            connectionString,
            tenantId,
            catalog.Versions,
            $$"""[{ "id": "{{package}}", "version": "1.0.0", "runner": "customer" }]""",
            cancellationToken,
            ("entities/probe.json", _probeEntity),
            ("processes/probe-call.json", ProbeProcess(package)));

    // Compiles the application and activates it with the release activator, which makes the tables of its entities. The activation enlists both
    // contexts in one transaction, so they share a connection.
    private async Task<Guid> ActivateWithTablesAsync(
        string connectionString,
        string tenantId,
        IReadOnlyCollection<PackageVersion> versions,
        string packages,
        CancellationToken cancellationToken,
        params (string Path, string Content)[] files)
    {
        var resources = Resources(packages, files);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var compiled = await ReleaseCompiler.CompileAsync(resources, configuration, versions, [], cancellationToken);
        Assert.Empty(compiled.Diagnostics);
        var activated = await ReleaseActivator.ActivateAsync(
            compiled,
            new ActiveReleaseStore(configuration),
            data,
            new ActivationBindings(tenantId, new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase), AllowHttp: false),
            OpenInstanceCounter.Instance,
            cancellationToken);
        Assert.Empty(activated.Diagnostics);
        return compiled.Release!.Id;
    }

    /// <summary>
    /// Leaves the state that staging leaves for a call of <c>OrderSum</c> at <paramref name="releaseId"/>: an order with <c>a</c> 2, <c>b</c> 3 and no
    /// <c>total</c>, an instance <c>waiting</c> on <c>sum</c> with the order as its subject, and a <c>pending</c> call with the input
    /// <c>{ "a": 2, "b": 3 }</c> and no work item. Returns the call's id, the instance's id and the order's id.
    /// </summary>
    public async Task<(Guid CallId, Guid InstanceId, Guid OrderId)> StageOrderSumCallAsync(
        string connectionString, Guid releaseId, string version, CancellationToken cancellationToken)
    {
        var orderId = Guid.NewGuid();
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(OrderEntityId));
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var insert = new NpgsqlCommand(
                $"INSERT INTO {table} ({EntityNaming.Quote(EntityNaming.IdColumn)}, {EntityNaming.Quote(EntityNaming.Column("a"))}, {EntityNaming.Quote(EntityNaming.Column("b"))}) VALUES (@id, 2, 3)",
                connection);
            insert.Parameters.AddWithValue("id", orderId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        var (callId, instanceId) = await StageCallAsync(
            connectionString, releaseId, _orderSumProcessId, OrderEntityId, orderId, "tools", version, "sum", """{ "a": 2, "b": 3 }""", idempotent: true, cancellationToken);
        return (callId, instanceId, orderId);
    }

    /// <summary>
    /// Leaves the state that staging leaves for a call of <c>ProbeCall</c> at <paramref name="releaseId"/>: a probe with <c>mode</c> <c>declared</c> and no
    /// <c>status</c>, an instance <c>waiting</c> on <c>probe</c> with the probe as its subject, and a <c>pending</c> call of <c>&lt;package&gt;/probe</c> with
    /// the input <c>{ "mode": "declared" }</c> and no work item. Returns the call's id, the instance's id and the probe's id.
    /// </summary>
    public async Task<(Guid CallId, Guid InstanceId, Guid ProbeId)> StageProbeCallAsync(
        string connectionString, Guid releaseId, CancellationToken cancellationToken, string package = TestPackages.Id)
    {
        var probeId = Guid.NewGuid();
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(ProbeEntityId));
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var insert = new NpgsqlCommand(
                $"INSERT INTO {table} ({EntityNaming.Quote(EntityNaming.IdColumn)}, {EntityNaming.Quote(EntityNaming.Column("mode"))}) VALUES (@id, 'declared')",
                connection);
            insert.Parameters.AddWithValue("id", probeId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        var (callId, instanceId) = await StageCallAsync(
            connectionString, releaseId, _probeCallProcessId, ProbeEntityId, probeId, package, "1.0.0", "probe", """{ "mode": "declared" }""", idempotent: false, cancellationToken);
        return (callId, instanceId, probeId);
    }

    /// <summary>Compiles and activates the application with the own package files and the process <c>ToolsSum</c>, which runs <c>tools/sum</c> in its step <c>sum</c>.</summary>
    public Task<Guid> ActivateOwnWithProcessAsync(string connectionString, string manifest, CancellationToken cancellationToken) =>
        ActivateAsync(
            connectionString,
            [],
            "[]",
            cancellationToken,
            ("packages/tools/axis-package.json", manifest),
            ("processes/tools-sum.json", _toolsSumProcess));

    /// <summary>
    /// Leaves the state that staging leaves for a call of <c>ToolsSum</c> at <paramref name="releaseId"/>: an instance <c>waiting</c> on <c>sum</c>
    /// and a <c>pending</c> call with the input <c>{ "a": 2, "b": 3 }</c> and no work item. Returns the call's id.
    /// </summary>
    public async Task<Guid> StageToolsSumCallAsync(string connectionString, Guid releaseId, string version, CancellationToken cancellationToken) =>
        (await StageCallAsync(
            connectionString, releaseId, _toolsSumProcessId, null, null, "tools", version, "sum", """{ "a": 2, "b": 3 }""", idempotent: true, cancellationToken)).CallId;

    // The step and the operation of the call have one name, which the processes of this class share.
    private async Task<(Guid CallId, Guid InstanceId)> StageCallAsync(
        string connectionString,
        Guid releaseId,
        Guid processId,
        Guid? subjectEntityId,
        Guid? subjectId,
        string package,
        string version,
        string operation,
        string input,
        bool idempotent,
        CancellationToken cancellationToken)
    {
        await using var context = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options);
        var instanceId = Guid.CreateVersion7();
        var callId = Guid.CreateVersion7();
        context.Add(new ProcessInstanceRow
        {
            Id = instanceId,
            ApplicationId = ApplicationId,
            ProcessId = processId,
            SubjectEntityId = subjectEntityId,
            SubjectId = subjectId,
            ReleaseId = releaseId,
            State = ProcessStarts.Waiting,
            Revision = 2,
            Step = operation,
        });
        await context.SaveChangesAsync(cancellationToken);
        context.Add(new PackageCallRow
        {
            Id = callId,
            ProcessInstanceId = instanceId,
            Step = operation,
            StagedRevision = 2,
            ReleaseId = releaseId,
            Package = package,
            Version = version,
            Operation = operation,
            State = "pending",
            Attempts = 0,
            RetryAttempts = 3,
            RetryBackoff = TimeSpan.FromSeconds(30),
            Idempotent = idempotent,
            Input = input,
        });
        await context.SaveChangesAsync(cancellationToken);
        return (callId, instanceId);
    }

    /// <summary>Compiles and activates the application with the own package files, and no installed package pinned.</summary>
    public Task<Guid> ActivateOwnAsync(string connectionString, string manifest, CancellationToken cancellationToken) =>
        ActivateAsync(connectionString, [], "[]", cancellationToken, ("packages/tools/axis-package.json", manifest));

    /// <summary>Compiles and activates the application with <paramref name="packages"/> as its <c>packages</c> entry, and returns the release's id.</summary>
    public async Task<Guid> ActivateAsync(
        string connectionString,
        IReadOnlyCollection<PackageVersion> versions,
        string packages,
        CancellationToken cancellationToken,
        params (string Path, string Content)[] files)
    {
        var resources = Resources(packages, files);

        await using var context = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connectionString).Options);
        var compiled = await ReleaseCompiler.CompileAsync(resources, context, versions, [], cancellationToken);
        Assert.Empty(compiled.Diagnostics);
        var releaseId = compiled.Release!.Id;
        var set = await new ActiveReleaseStore(context).TrySetAsync(ApplicationId, _name, releaseId, [], DateTimeOffset.UtcNow, ActivationActor.Operator, cancellationToken);
        Assert.Equal(SetActiveReleaseResult.Set, set);
        return releaseId;
    }

    private List<ResourceContent> Resources(string packages, params (string Path, string Content)[] files)
    {
        var application = $$"""
            { "id": "{{ApplicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1, "packages": {{packages}} }
            """;
        var resources = new List<ResourceContent> { new("application.json", application) };
        resources.AddRange(files.Select(file => new ResourceContent(file.Path, file.Content)));
        return resources;
    }
}
