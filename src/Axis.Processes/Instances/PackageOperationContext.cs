using Axis.Extensions;

namespace Axis.Processes.Instances;

/// <summary>
/// What the worker gives a package operation. Its <see cref="OperationId"/> is the call's operation
/// identity. Capability limits are not built yet, so every request for a binding throws.
/// </summary>
internal sealed class PackageOperationContext(Guid operationId) : IOperationContext
{
    public Guid OperationId { get; } = operationId;

    public HttpClient GetHttpClient(string urlBinding) =>
        throw new InvalidOperationException("Capabilities are not available yet.");

    public string GetSecret(string secretBinding) =>
        throw new InvalidOperationException("Capabilities are not available yet.");
}
