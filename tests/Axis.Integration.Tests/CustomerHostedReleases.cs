using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Microsoft.EntityFrameworkCore;

namespace Axis.Integration.Tests;

/// <summary>
/// Compiles and activates releases of one application whose own package <c>tools</c> is customer-hosted, in a tenant database, for the tests of
/// the registration check and the take in <c>docs/reference/extension-packages.md#customer-hosted-runners</c>. A release can also hold a process whose
/// step calls the package, and a call that staging would leave for it, see <c>docs/reference/process-execution.md#staging-a-package-call</c>.
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
    public async Task<Guid> StageToolsSumCallAsync(string connectionString, Guid releaseId, string version, CancellationToken cancellationToken)
    {
        await using var context = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options);
        var instanceId = Guid.CreateVersion7();
        var callId = Guid.CreateVersion7();
        context.Add(new ProcessInstanceRow
        {
            Id = instanceId,
            ApplicationId = ApplicationId,
            ProcessId = _toolsSumProcessId,
            ReleaseId = releaseId,
            State = ProcessStarts.Waiting,
            Revision = 2,
            Step = "sum",
        });
        await context.SaveChangesAsync(cancellationToken);
        context.Add(new PackageCallRow
        {
            Id = callId,
            ProcessInstanceId = instanceId,
            Step = "sum",
            StagedRevision = 2,
            ReleaseId = releaseId,
            Package = "tools",
            Version = version,
            Operation = "sum",
            State = "pending",
            Attempts = 0,
            RetryAttempts = 3,
            RetryBackoff = TimeSpan.FromSeconds(30),
            Idempotent = true,
            Input = """{ "a": 2, "b": 3 }""",
        });
        await context.SaveChangesAsync(cancellationToken);
        return callId;
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
        var application = $$"""
            { "id": "{{ApplicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1, "packages": {{packages}} }
            """;
        var resources = new List<ResourceContent> { new("application.json", application) };
        resources.AddRange(files.Select(file => new ResourceContent(file.Path, file.Content)));

        await using var context = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connectionString).Options);
        var compiled = await ReleaseCompiler.CompileAsync(resources, context, versions, [], cancellationToken);
        Assert.Empty(compiled.Diagnostics);
        var releaseId = compiled.Release!.Id;
        var set = await new ActiveReleaseStore(context).TrySetAsync(ApplicationId, _name, releaseId, [], DateTimeOffset.UtcNow, ActivationActor.Operator, cancellationToken);
        Assert.Equal(SetActiveReleaseResult.Set, set);
        return releaseId;
    }
}
