using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Data.Storage;
using Axis.Processes.Work;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// What a customer-hosted runner gets when it takes a call: the lease, the package, the version, the operation, the operation identity,
/// the input and the end of the lease. The input is encoded as in a <c>run</c> message and holds no binding value, see
/// docs/reference/process-execution.md#staging-a-package-call.
/// </summary>
public sealed record PackageCallTake(
    Guid LeaseId,
    string Package,
    string Version,
    string Operation,
    Guid OperationId,
    JsonObject Input,
    DateTimeOffset LeaseUntil);

/// <summary>
/// Lets a customer-hosted runner take the oldest <c>pending</c> call to a package it registered and holds, in the caller's transaction, see
/// docs/reference/process-execution.md#staging-a-package-call. The call becomes <c>sending</c> with one more attempt, and stores the runner, a
/// new lease and the end of the lease, which is the step's timeout from now. The take also queues a <c>package.call</c> work item that is due when the
/// lease ends, which a posted result deletes, see docs/reference/process-execution.md#package-call-outcomes. The caller commits. A call whose step is not customer-hosted, whose
/// manifest cannot be found or whose stored input no longer fits the manifest is passed over and stays <c>pending</c>.
/// </summary>
public static class PackageCallTakes
{
    /// <param name="tenantId">The tenant of the database, which the lease's work item names.</param>
    /// <param name="held">The packages the runner holds, which the call's package and version must be among.</param>
    /// <param name="installed">The installed packages, which hold the manifest of an installed package.</param>
    /// <param name="releaseOf">Gives the model of the release a call is pinned to.</param>
    /// <returns>The call that was taken, or <see langword="null"/> when no call waits for the runner.</returns>
    public static async Task<PackageCallTake?> TakeAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        Guid runnerId,
        IReadOnlyCollection<PackageRunnerPackage> held,
        PackageCatalog installed,
        Func<Guid, CancellationToken, Task<ApplicationModel>> releaseOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(releaseOf);

        var heldJson = JsonSerializer.Serialize(held.Select(package => new { id = package.Id, version = package.Version }));
        var skipped = new List<Guid>();
        while (await PackageCalls.FindTakeableAsync(transaction, runnerId, heldJson, [.. skipped], cancellationToken) is { } call)
        {
            var model = await releaseOf(call.ReleaseId, cancellationToken);
            if (model.Processes.FirstOrDefault(candidate => candidate.Id == call.ProcessId) is { } process
                && process.TryGetStep(call.Step, out var found)
                && found is PackageOperationStepModel { CustomerHosted: true } step
                && await FindManifestAsync(transaction, installed, call.ReleaseId, call.Package, call.Version, cancellationToken) is { } manifest
                && manifest.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, call.Operation, StringComparison.OrdinalIgnoreCase)) is { } operation
                && PackageCallHandler.ReadInput(call.Input, operation) is { } values)
            {
                var leaseId = Guid.CreateVersion7();
                if (await PackageCalls.TakeAsync(transaction, call.Id, runnerId, leaseId, step.Timeout, cancellationToken) is { } leaseUntil)
                {
                    await WorkItemQueue.EnqueueAsync(
                        transaction.Connection!,
                        transaction,
                        tenantId,
                        ProcessStarts.PackageCallWorkItemKind,
                        leaseUntil,
                        cancellationToken,
                        processInstanceId: call.ProcessInstanceId,
                        packageCallId: call.Id);
                    return new PackageCallTake(
                        leaseId, call.Package, call.Version, call.Operation, call.Id, RunnerValues.Encode(operation.Input, values), leaseUntil);
                }
            }

            skipped.Add(call.Id);
        }

        return null;
    }

    /// <summary>
    /// The manifest of the package <paramref name="package"/> <paramref name="version"/> of a call pinned to <paramref name="releaseId"/>: the
    /// installed package's, or for an application's own package the one in the release's source. It must name the package's id and version.
    /// </summary>
    internal static async Task<PackageManifest?> FindManifestAsync(
        NpgsqlTransaction transaction, PackageCatalog installed, Guid releaseId, string package, string version, CancellationToken cancellationToken)
    {
        var manifest = installed.Find(package, version)?.Manifest
            ?? await ReleasePackageFiles.LoadSourceManifestAsync(transaction, releaseId, package, cancellationToken);
        return manifest is not null && manifest.Id == package && manifest.Version == version ? manifest : null;
    }
}
