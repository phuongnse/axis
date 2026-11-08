using System.Collections.Concurrent;
using Axis.Tenancy;

namespace Axis.Agent;

/// <summary>
/// Claims authoring requests with a lease and runs the step of their state, see <c>docs/reference/authoring-requests.md#durable-requests</c>.
/// A round takes the tenants in turn and claims at most one request in each. A host that stops, or a step that throws, writes nothing more:
/// the lease lapses and the next claim closes the unfinished step as <c>interrupted</c> and runs it again. Every write is fenced by the claim.
/// </summary>
internal sealed partial class AuthoringRequestRunner(
    AuthoringRequestStore store,
    AuthoringSteps steps,
    AuthoringLoopOptions options,
    TimeProvider time,
    ILogger<AuthoringRequestRunner> logger)
{
    // The last failure logged for each tenant, so a tenant that fails in every round is logged once until it changes or recovers.
    private readonly ConcurrentDictionary<string, string> _failures = new(StringComparer.Ordinal);

    /// <summary>Runs one round over the tenants and answers whether it claimed any request.</summary>
    public async Task<bool> RunRoundAsync(IReadOnlyList<TenantEntry> tenants, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        var claimedAny = false;
        foreach (var tenant in tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            claimedAny |= await RunTenantAsync(tenant.Id, cancellationToken);
        }

        return claimedAny;
    }

    private async Task<bool> RunTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        // A new value for each claim keeps the fence sound when the same host claims the request again, and where the process id is 1 and the machine name repeats.
        var claimedBy = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.CreateVersion7():N}";
        AuthoringRequestToWork? request;
        try
        {
            request = await store.ClaimAsync(tenantId, steps.States, claimedBy, options.Lease, cancellationToken);
            _failures.TryRemove(tenantId, out _);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The role check refusal names the tenant and the role. Any other failure may name the database host, so only its type is logged.
            var cause = exception is InvalidOperationException ? exception.Message : exception.GetType().Name;
            if (!_failures.TryGetValue(tenantId, out var last) || last != cause)
            {
                _failures[tenantId] = cause;
                LogTenantFailed(tenantId, cause);
            }

            return false;
        }

        if (request is null)
        {
            return false;
        }

        await RunRequestAsync(tenantId, request, claimedBy, cancellationToken);
        return true;
    }

    private async Task RunRequestAsync(string tenantId, AuthoringRequestToWork request, string claimedBy, CancellationToken cancellationToken)
    {
        try
        {
            if (!await store.CloseInterruptedAsync(tenantId, request.Id, claimedBy, cancellationToken))
            {
                LogClaimLost(request.Id);
                return;
            }

            var current = request;
            while (steps.Find(current.State) is { } step)
            {
                var stepId = await store.StartStepAsync(tenantId, request.Id, claimedBy, step.Kind, cancellationToken);
                if (stepId is null)
                {
                    LogClaimLost(request.Id);
                    return;
                }

                var result = await RunStepAsync(step, tenantId, current, stepId.Value, claimedBy, cancellationToken);
                if (!AuthoringRequestStore.States.Contains(result.NextState, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"The authoring step '{step.Kind}' answered '{result.NextState}', which is not a state of a request.");
                }

                var keepClaim = steps.Has(result.NextState);
                if (!await store.FinishStepAsync(tenantId, request.Id, stepId.Value, claimedBy, result.NextState, result.Outcome, result.InputSummary, keepClaim, options.Lease, cancellationToken))
                {
                    LogClaimLost(request.Id);
                    return;
                }

                current = current with { State = result.NextState };
            }
        }
        catch (OperationCanceledException)
        {
            // The host stopped, or the claim was lost. Nothing more is written, and the lease lapses.
            LogStopped(request.Id);
        }
        catch (AuthoringClaimLostException)
        {
            LogClaimLost(request.Id);
        }
        catch (Exception exception)
        {
            // Only the type is logged: a message may hold a connection value. The lease lapses, and the next claim runs the step again.
            LogStepFailed(request.Id, exception.GetType().Name);
        }
    }

    // The renewal runs beside the step and cancels the step's token when the claim is gone.
    private async Task<AuthoringStepResult> RunStepAsync(IAuthoringStep step, string tenantId, AuthoringRequestToWork request, Guid stepId, string claimedBy, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewal = RenewAsync(tenantId, request.Id, claimedBy, linked);
        try
        {
            var context = new AuthoringStepContext(
                tenantId,
                request,
                (tokens, token) => store.AddTokensAsync(tenantId, request.Id, stepId, claimedBy, tokens, token));
            return await step.RunAsync(context, linked.Token);
        }
        finally
        {
            await linked.CancelAsync();
            await renewal;
        }
    }

    private async Task RenewAsync(string tenantId, Guid requestId, string claimedBy, CancellationTokenSource linked)
    {
        var interval = options.Lease / 3;
        try
        {
            while (true)
            {
                await Task.Delay(interval, time, linked.Token);
                try
                {
                    if (!await store.RenewAsync(tenantId, requestId, claimedBy, options.Lease, linked.Token))
                    {
                        LogClaimLost(requestId);
                        await linked.CancelAsync();
                        return;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The next tick tries again. The claim is lost only when a renewal finds it gone.
                    LogRenewFailed(requestId, exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The step ended or the host stopped.
        }
    }

    [LoggerMessage(LogLevel.Error, "Claiming an authoring request of tenant {TenantId} failed. {Cause}")]
    private partial void LogTenantFailed(string tenantId, string cause);

    [LoggerMessage(LogLevel.Warning, "The agent host no longer holds the claim on authoring request {RequestId}. It writes nothing more to it.")]
    private partial void LogClaimLost(Guid requestId);

    [LoggerMessage(LogLevel.Information, "The agent host stopped working on authoring request {RequestId}. The lease lapses and the next claim resumes it.")]
    private partial void LogStopped(Guid requestId);

    [LoggerMessage(LogLevel.Error, "A step of authoring request {RequestId} failed ({Cause}). The lease lapses and the next claim runs the step again.")]
    private partial void LogStepFailed(Guid requestId, string cause);

    [LoggerMessage(LogLevel.Warning, "Renewing the lease of authoring request {RequestId} failed ({Cause}). The host tries again.")]
    private partial void LogRenewFailed(Guid requestId, string cause);
}
