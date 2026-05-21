namespace Axis.WorkflowEngine.Application.Services;

/// <summary>
/// Executes a sandboxed JavaScript Script step. Implemented in Infrastructure.
/// The script may write to the <c>output</c> object; those properties are returned.
/// </summary>
public interface IScriptExecutor
{
    /// <summary>
    /// Runs the script from <paramref name="stepConfig"/> inside a sandbox with the given
    /// <paramref name="context"/> available as a read-only <c>context</c> object.
    /// </summary>
    /// <returns>All properties written to <c>output</c> inside the script.</returns>
    Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
        IReadOnlyDictionary<string, object?>? stepConfig,
        IReadOnlyDictionary<string, object?> context,
        CancellationToken ct = default);
}
