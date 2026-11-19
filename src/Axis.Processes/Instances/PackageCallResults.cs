using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Core.Telemetry;
using Axis.Data.Records;
using Axis.Extensions;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>What a runner's result did to the call it names.</summary>
public enum PackageCallResultOutcome
{
    /// <summary>The output fit. The call is <c>succeeded</c> and the instance runs on to <c>next</c>.</summary>
    Succeeded,

    /// <summary>
    /// The call is <c>failed</c> and its instance failed. The runner posted <c>failed</c>, a <c>retryable</c> result had no attempt left, or the output
    /// did not fit, or the release, the manifest or the record write refused it.
    /// </summary>
    Failed,

    /// <summary>A <c>retryable</c> result set the call back to <c>pending</c> and queued a <c>package.call</c> item, due after the step's backoff.</summary>
    Retrying,

    /// <summary>No call has the lease, or its runner is another one. Nothing was written.</summary>
    UnknownLease,

    /// <summary>The call is no longer <c>sending</c>, its lease has ended, or its instance no longer waits on the step. Nothing was written.</summary>
    Conflict,
}

/// <summary>The result of a customer-hosted runner for the call it took.</summary>
public abstract record PackageCallResult
{
    /// <summary>The runner finished the operation. <paramref name="Output"/> is in the form of a package runner's <c>result</c>: integers are strings.</summary>
    public sealed record Succeeded(JsonObject Output) : PackageCallResult;

    /// <summary>The operation did not act and may be tried again. It counts as a <see cref="RetryableException"/>.</summary>
    public sealed record Retryable : PackageCallResult;

    /// <summary>The operation failed. It counts as any other exception, and <paramref name="Error"/> is the exception type.</summary>
    public sealed record Failed(string Error) : PackageCallResult;
}

/// <summary>
/// Records the result of a customer-hosted runner for the call it took, in the caller's transaction, see
/// docs/reference/process-execution.md#staging-a-package-call and #package-call-outcomes. The call is found and locked by its lease, after its
/// instance. A <c>succeeded</c> output is decoded and checked against the manifest as a package runner's <c>result</c> is, and a fitting output is
/// written with <see cref="PackageCallRecords"/>, the same writes that the worker makes after a run. A <c>retryable</c> result counts as a
/// <see cref="RetryableException"/>: while the step's retry policy has attempts left, the call is set back to <c>pending</c> and a
/// <c>package.call</c> item is queued, due after the backoff, which holds the call back from the next take until the worker deletes it, see
/// <see cref="PackageCallHandler"/>. A <c>failed</c> result fails the call and the instance at once, with the posted error. An output that does not
/// fit, a release or manifest that does not match the call, a record write that is refused, and a <c>retryable</c> result with no attempt left fail
/// the call and the instance, and the caller still commits. A refused write rolls back to a savepoint first. The caller commits after every outcome
/// except <see cref="PackageCallResultOutcome.UnknownLease"/> and <see cref="PackageCallResultOutcome.Conflict"/>, which write nothing and are rolled back.
/// Once the lease checks pass, the call's <c>package.call</c> items are deleted first, among them the one the take queued for the end of the lease. The
/// worker records an unknown outcome when that item comes due with the call still <c>sending</c>, so a result that arrives after it answers
/// <see cref="PackageCallResultOutcome.Conflict"/>.
/// </summary>
public static partial class PackageCallResults
{
    private const string Savepoint = "result";

    /// <param name="runnerId">The runner that posts the result, which must be the one that holds the lease.</param>
    /// <param name="leaseId">The lease the runner got from its take.</param>
    /// <param name="result">What the runner posted.</param>
    /// <param name="installed">The installed packages, which hold the manifest of an installed package.</param>
    /// <param name="releaseOf">Gives the model of a release.</param>
    /// <param name="cipher">Encrypts the sensitive fields the output writes, or <see langword="null"/> when none is configured.</param>
    public static async Task<PackageCallResultOutcome> RecordAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        Guid runnerId,
        Guid leaseId,
        PackageCallResult result,
        PackageCatalog installed,
        Func<Guid, CancellationToken, Task<ApplicationModel>> releaseOf,
        RecordCipher? cipher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(releaseOf);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var leased = await PackageCalls.LockLeasedAsync(transaction, leaseId, cancellationToken);
        if (leased is null || leased.Call.RunnerId != runnerId)
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

        // The lease holds, so no lease-end item may run on the call from here on.
        await PackageCalls.DeleteItemsAsync(transaction, call.Id, cancellationToken);

        var logger = loggerFactory.CreateLogger(typeof(PackageCallResults));
        var model = await releaseOf(call.ReleaseId, cancellationToken);
        var processName = model.Processes.FirstOrDefault(candidate => candidate.Id == instance.ProcessId)?.Name;
        var attempt = new PackageCallAttempt(call, instance.Revision, call.Attempts, instance.Now, model.Manifest.Name);

        // The take counted the attempt, so a call that cannot be checked here fails with an attempt row.
        var manifest = await PackageCallTakes.FindManifestAsync(transaction, installed, call.ReleaseId, call.Package, call.Version, cancellationToken);
        if (manifest is null)
        {
            return await FailAsync(
                transaction,
                tenantId,
                instance,
                attempt,
                processName,
                $"The package '{call.Package}' {call.Version} is not installed.",
                null,
                CallAttempts.Failed,
                logger,
                cancellationToken);
        }

        PackageCallTarget target;
        try
        {
            target = PackageCallRecords.FindTarget(model, instance, call, manifest);
        }
        catch (InvalidOperationException exception)
        {
            return await FailAsync(transaction, tenantId, instance, attempt, processName, exception.Message, null, CallAttempts.Failed, logger, cancellationToken);
        }

        // The take set the end of the lease to its start plus the step's timeout, which gives the start of the attempt.
        attempt = attempt with { StartedAt = leased.LeaseUntil - target.Step.Timeout };
        switch (result)
        {
            case PackageCallResult.Retryable:
                return await RetryAsync(transaction, tenantId, instance, attempt, processName, logger, cancellationToken);
            case PackageCallResult.Failed failed:
                return await FailAsync(transaction, tenantId, instance, attempt, processName, failed.Error, StepProblem.Exception, CallAttempts.Failed, logger, cancellationToken);
        }

        var output = ((PackageCallResult.Succeeded)result).Output;
        var decoded = PackageCallRecords.DecodeRunnerOutput(target.Operation, output);
        var values = decoded is null ? null : PackageCallRecords.Fit(decoded, target.Operation, target.Step, target.Entity);
        if (values is null)
        {
            return await FailAsync(
                transaction,
                tenantId,
                instance,
                attempt,
                processName,
                PackageCallRecords.Misfit(manifest, target.Operation),
                null,
                CallAttempts.Failed,
                logger,
                cancellationToken);
        }

        await transaction.SaveAsync(Savepoint, cancellationToken);
        StepFailure? failure;
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
                RunnerValues.Encode(target.Operation.Output, decoded!),
                cipher,
                async (applicationId, token) =>
                    await StartLock.TakeSharedAsync(transaction, applicationId, token) is { } releaseId ? (releaseId, await releaseOf(releaseId, token)) : null,
                logger,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The message can hold record values, so only the type is kept.
            failure = new StepFailure(PackageCallRecords.ErrorOf(exception), StepProblem.Exception);
        }

        if (failure is null)
        {
            await transaction.ReleaseAsync(Savepoint, cancellationToken);
            LogSucceeded(logger, call.Id, instance.Id);
            return PackageCallResultOutcome.Succeeded;
        }

        await transaction.RollbackAsync(Savepoint, cancellationToken);
        await transaction.ReleaseAsync(Savepoint, cancellationToken);
        return await FailAsync(transaction, tenantId, instance, attempt, processName, failure.Error, failure.Problem, CallAttempts.Failed, logger, cancellationToken);
    }

    /// <summary>
    /// Counts the result as a <see cref="RetryableException"/>. The call is set back to <c>pending</c> and a <c>package.call</c> item is queued, due
    /// after the step's backoff, while the retry policy has attempts left. Otherwise the call and the instance fail.
    /// </summary>
    private static async Task<PackageCallResultOutcome> RetryAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        LoadedInstance instance,
        PackageCallAttempt attempt,
        string? processName,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var error = typeof(RetryableException).FullName!;
        if (PackageCallRecords.RetryDelay(attempt) is not { } delay)
        {
            return await FailAsync(transaction, tenantId, instance, attempt, processName, error, StepProblem.Exception, CallAttempts.Retryable, logger, cancellationToken);
        }

        var call = attempt.Call;
        await PackageCallRecords.WriteRetryAsync(
            transaction, tenantId, attempt, error, PackageCallRecords.AttemptRow(attempt, CallAttempts.Retryable, error, StepProblem.Exception), cancellationToken);
        await WorkItemQueue.EnqueueAsync(
            transaction.Connection!,
            transaction,
            tenantId,
            ProcessStarts.PackageCallWorkItemKind,
            DateTimeOffset.UtcNow + delay,
            cancellationToken,
            processInstanceId: instance.Id,
            packageCallId: call.Id);
        LogRetrying(logger, call.Id, instance.Id, attempt.Attempts, delay);
        return PackageCallResultOutcome.Retrying;
    }

    private static async Task<PackageCallResultOutcome> FailAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        LoadedInstance instance,
        PackageCallAttempt attempt,
        string? processName,
        string error,
        StepProblem? problem,
        string outcome,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!await PackageCallRecords.WriteFailureAsync(
                transaction, tenantId, instance, attempt, error, PackageCallRecords.AttemptRow(attempt, outcome, error, problem), cancellationToken))
        {
            return PackageCallResultOutcome.Conflict;
        }

        AxisMetrics.RecordInstanceEnded(tenantId, attempt.Application, processName, ProcessStarts.Failed);
        LogFailed(logger, attempt.Call.Id, instance.Id, error);
        return PackageCallResultOutcome.Failed;
    }

    [LoggerMessage(LogLevel.Information, "Package call {CallId} of process instance {InstanceId} succeeded by a runner's result.")]
    private static partial void LogSucceeded(ILogger logger, Guid callId, Guid instanceId);

    [LoggerMessage(LogLevel.Information, "Package call {CallId} of process instance {InstanceId} set back to pending by a runner's retryable result after attempt {Attempts}, due in {Delay}.")]
    private static partial void LogRetrying(ILogger logger, Guid callId, Guid instanceId, int attempts, TimeSpan delay);

    [LoggerMessage(LogLevel.Warning, "Package call {CallId} of process instance {InstanceId} failed by a runner's result: {Error}")]
    private static partial void LogFailed(ILogger logger, Guid callId, Guid instanceId, string error);
}
