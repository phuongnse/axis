using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Core.Telemetry;
using Axis.Data.Records;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>What a runner's result did to the call it names.</summary>
public enum PackageCallResultOutcome
{
    /// <summary>The output fit. The call is <c>succeeded</c> and the instance runs on to <c>next</c>.</summary>
    Succeeded,

    /// <summary>The call is <c>failed</c> and its instance failed. The output did not fit, or the release, the manifest or the record write refused it.</summary>
    Failed,

    /// <summary>No call has the lease, or its runner is another one. Nothing was written.</summary>
    UnknownLease,

    /// <summary>The call is no longer <c>sending</c>, its lease has ended, or its instance no longer waits on the step. Nothing was written.</summary>
    Conflict,
}

/// <summary>
/// Records the <c>succeeded</c> result of a customer-hosted runner for the call it took, in the caller's transaction, see
/// docs/reference/process-execution.md#staging-a-package-call. The call is found and locked by its lease, after its instance. The output is
/// decoded and checked against the manifest as a package runner's <c>result</c> is, and a fitting output is written with
/// <see cref="PackageCallRecords"/>, the same writes that the worker makes after a run. An output that does not fit, a release or manifest that does
/// not match the call, and a record write that is refused fail the call and the instance, and the caller still commits. A refused write rolls back
/// to a savepoint first. The caller commits after <see cref="PackageCallResultOutcome.Succeeded"/> and <see cref="PackageCallResultOutcome.Failed"/>,
/// and rolls back after the other outcomes, which write nothing.
/// </summary>
public static partial class PackageCallResults
{
    private const string Savepoint = "result";

    /// <param name="runnerId">The runner that posts the result, which must be the one that holds the lease.</param>
    /// <param name="leaseId">The lease the runner got from its take.</param>
    /// <param name="output">The <c>output</c> of the result, in the form of a package runner's <c>result</c>: integers are strings.</param>
    /// <param name="installed">The installed packages, which hold the manifest of an installed package.</param>
    /// <param name="releaseOf">Gives the model of a release.</param>
    /// <param name="cipher">Encrypts the sensitive fields the output writes, or <see langword="null"/> when none is configured.</param>
    public static async Task<PackageCallResultOutcome> RecordSucceededAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        Guid runnerId,
        Guid leaseId,
        JsonObject output,
        PackageCatalog installed,
        Func<Guid, CancellationToken, Task<ApplicationModel>> releaseOf,
        RecordCipher? cipher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(releaseOf);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var leased = await PackageCalls.LockLeasedAsync(transaction, leaseId, cancellationToken);
        if (leased is null || leased.RunnerId != runnerId)
        {
            return PackageCallResultOutcome.UnknownLease;
        }

        var call = leased.Call;
        if (call.State != PackageCalls.Sending || leased.Ended)
        {
            return PackageCallResultOutcome.Conflict;
        }

        var instance = await ProcessSteps.LoadAsync(transaction, call.ProcessInstanceId, cancellationToken);
        if (instance is not { State: ProcessStarts.Waiting } || instance.Step != call.Step)
        {
            return PackageCallResultOutcome.Conflict;
        }

        var logger = loggerFactory.CreateLogger(typeof(PackageCallResults));
        var model = await releaseOf(call.ReleaseId, cancellationToken);
        var processName = model.Processes.FirstOrDefault(candidate => candidate.Id == instance.ProcessId)?.Name;
        var attempt = new PackageCallAttempt(call, instance.Revision, call.Attempts, instance.Now, model.Manifest.Name);

        // The take counted the attempt, so a call that cannot be checked here fails with an attempt row.
        var manifest = await PackageCallTakes.FindManifestAsync(transaction, installed, call.ReleaseId, call.Package, call.Version, cancellationToken);
        if (manifest is null)
        {
            return await FailAsync(
                transaction, tenantId, instance, attempt, processName, $"The package '{call.Package}' {call.Version} is not installed.", logger, cancellationToken);
        }

        PackageCallTarget target;
        try
        {
            target = PackageCallRecords.FindTarget(model, instance, call, manifest);
        }
        catch (InvalidOperationException exception)
        {
            return await FailAsync(transaction, tenantId, instance, attempt, processName, exception.Message, logger, cancellationToken);
        }

        // The take set the end of the lease to its start plus the step's timeout, which gives the start of the attempt.
        attempt = attempt with { StartedAt = leased.LeaseUntil - target.Step.Timeout };
        var values = PackageCallRecords.DecodeRunnerOutput(target.Operation, output) is { } decoded
            ? PackageCallRecords.Fit(decoded, target.Operation, target.Step, target.Entity)
            : null;
        if (values is null)
        {
            return await FailAsync(
                transaction, tenantId, instance, attempt, processName, PackageCallRecords.Misfit(manifest, target.Operation), logger, cancellationToken);
        }

        await transaction.SaveAsync(Savepoint, cancellationToken);
        string? failure;
        try
        {
            failure = await PackageCallRecords.WriteSuccessAsync(
                transaction,
                tenantId,
                instance,
                attempt,
                target.Model,
                target.Entity,
                target.Step,
                values,
                cipher,
                async (applicationId, token) =>
                    await StartLock.TakeSharedAsync(transaction, applicationId, token) is { } releaseId ? (releaseId, await releaseOf(releaseId, token)) : null,
                logger,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The message can hold record values, so only the type is kept.
            failure = PackageCallRecords.ErrorOf(exception);
        }

        if (failure is null)
        {
            await transaction.ReleaseAsync(Savepoint, cancellationToken);
            LogSucceeded(logger, call.Id, instance.Id);
            return PackageCallResultOutcome.Succeeded;
        }

        await transaction.RollbackAsync(Savepoint, cancellationToken);
        await transaction.ReleaseAsync(Savepoint, cancellationToken);
        return await FailAsync(transaction, tenantId, instance, attempt, processName, failure, logger, cancellationToken);
    }

    private static async Task<PackageCallResultOutcome> FailAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        LoadedInstance instance,
        PackageCallAttempt attempt,
        string? processName,
        string error,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!await PackageCallRecords.WriteFailureAsync(
                transaction, tenantId, instance, attempt, error, PackageCallRecords.AttemptRow(attempt, CallAttempts.Failed, error), cancellationToken))
        {
            return PackageCallResultOutcome.Conflict;
        }

        AxisMetrics.RecordInstanceEnded(tenantId, attempt.Application, processName, ProcessStarts.Failed);
        LogFailed(logger, attempt.Call.Id, instance.Id, error);
        return PackageCallResultOutcome.Failed;
    }

    [LoggerMessage(LogLevel.Information, "Package call {CallId} of process instance {InstanceId} succeeded by a runner's result.")]
    private static partial void LogSucceeded(ILogger logger, Guid callId, Guid instanceId);

    [LoggerMessage(LogLevel.Warning, "Package call {CallId} of process instance {InstanceId} failed by a runner's result: {Error}")]
    private static partial void LogFailed(ILogger logger, Guid callId, Guid instanceId, string error);
}
