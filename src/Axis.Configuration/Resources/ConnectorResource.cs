namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>connector</c> resource: an external system a process can call. Its base URL and its secret
/// are deployment bindings, named and never held as values. The schema checks the shape of its
/// auth, its operations and their fields. Its timeout, operation and field names, paths and lookups
/// are checked by <see cref="Compilation.ApplicationCompiler"/>, and its bindings at activation.
/// </summary>
public sealed record ConnectorResource : Resource
{
    /// <summary>The <c>url</c> binding the operation paths are added to.</summary>
    public required BindingReference BaseUrl { get; init; }

    /// <summary>How calls authenticate. Null is the same as <c>none</c>.</summary>
    public ConnectorAuthDefinition? Auth { get; init; }

    /// <summary>An ISO 8601 duration. Null means the default of 30 seconds.</summary>
    public string? Timeout { get; init; }

    /// <summary>The request header that carries the idempotency key, or null when the system has none.</summary>
    public string? Idempotency { get; init; }

    public required IReadOnlyList<ConnectorOperationDefinition> Operations { get; init; }
}
