namespace Axis.WorkflowEngine.Application.Services;

/// <summary>
/// Executes an HTTP Request step. Implemented in Infrastructure.
/// Returns the response as a context-ready dictionary on success,
/// or throws on non-2xx (when configured to fail) / network failure.
/// </summary>
public interface IHttpStepExecutor
{
    /// <summary>
    /// Executes an outbound HTTP call using <paramref name="stepConfig"/> interpolated
    /// with values from <paramref name="context"/>.
    /// </summary>
    /// <returns>
    /// Output dictionary: <c>{ status_code, body, headers }</c>,
    /// namespaced under the configured output variable name.
    /// </returns>
    Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
        IReadOnlyDictionary<string, object?>? stepConfig,
        IReadOnlyDictionary<string, object?> context,
        CancellationToken ct = default);
}
