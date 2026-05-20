using Axis.WorkflowEngine.Application.Services;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Infrastructure.Services.StepExecutors;

/// <summary>
/// JavaScript Script step executor. Executes sandboxed JS with access to context.
/// US-060: full sandbox implementation (no network, no filesystem, no process) pending;
/// currently returns an empty output to keep the execution moving.
/// </summary>
internal sealed class ScriptExecutor(ILogger<ScriptExecutor> logger) : IScriptExecutor
{
    public Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
        IReadOnlyDictionary<string, object?>? stepConfig,
        IReadOnlyDictionary<string, object?> context,
        CancellationToken ct = default)
    {
        if (stepConfig is null || !stepConfig.TryGetValue("script", out object? scriptRaw))
            throw new InvalidOperationException("Script step requires a 'script' config key.");

        string script = scriptRaw?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(script))
            throw new InvalidOperationException("Script body must not be empty.");

        // Placeholder: full JS sandbox engine (e.g., Jint) implementation is E06 scope.
        // Returning empty output allows the execution to continue and tests to verify step lifecycle.
        logger.LogWarning(
            "Script execution is not yet implemented — script body will not be evaluated. " +
            "Script length: {Length} chars.", script.Length);

        return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());
    }
}
