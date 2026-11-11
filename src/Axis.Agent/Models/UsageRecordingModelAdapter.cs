using Axis.Tenancy.Platform;

namespace Axis.Agent.Models;

/// <summary>
/// Wraps the adapter of a binding for one tenant and adds the tokens of every answer to the tenant's usage of the UTC day, by binding
/// and model, see <c>docs/reference/tenant-quotas.md#usage</c>. <paramref name="ownKey"/> is true when the tenant's own key paid the call.
/// A usage write that fails logs a warning that names the tenant and the exception type, and the answer still goes on. A call that fails records nothing.
/// </summary>
internal sealed partial class UsageRecordingModelAdapter(
    IModelAdapter inner,
    string tenantId,
    string binding,
    string model,
    bool ownKey,
    TenantUsageStore usage,
    TimeProvider time,
    ILogger logger) : IModelAdapter
{
    public async Task<ModelAnswer> CallAsync(ModelCall request, CancellationToken cancellationToken)
    {
        var answer = await inner.CallAsync(request, cancellationToken);
        try
        {
            await usage.AddTokensAsync(tenantId, binding, model, ownKey, answer.Tokens.Input, answer.Tokens.Output, time.GetUtcNow(), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogUsageNotWritten(logger, tenantId, exception.GetType().Name);
        }

        return answer;
    }

    [LoggerMessage(LogLevel.Warning, "The tokens of a model call of tenant {TenantId} were not added to its usage ({ExceptionType}).")]
    private static partial void LogUsageNotWritten(ILogger logger, string tenantId, string exceptionType);
}
