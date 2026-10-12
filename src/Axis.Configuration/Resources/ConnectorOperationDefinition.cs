namespace Axis.Configuration.Resources;

/// <summary>
/// A named operation of a connector as written: its HTTP method, its path template, its input and
/// output fields and an optional lookup, which names another operation that finds an earlier call
/// by its idempotency key.
/// </summary>
public sealed record ConnectorOperationDefinition
{
    public required string Name { get; init; }

    /// <summary>One of <c>GET</c>, <c>POST</c>, <c>PUT</c>, <c>PATCH</c> and <c>DELETE</c>.</summary>
    public required string Method { get; init; }

    /// <summary>The path template added to the base URL. <c>{idempotencyKey}</c> is its only placeholder.</summary>
    public required string Path { get; init; }

    /// <summary>The fields sent as a JSON body.</summary>
    public IReadOnlyList<ConnectorFieldDefinition> Input { get; init; } = [];

    /// <summary>The fields read from the JSON response.</summary>
    public IReadOnlyList<ConnectorFieldDefinition> Output { get; init; } = [];

    public string? Lookup { get; init; }
}
