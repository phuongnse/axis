namespace Axis.Configuration.Model;

/// <summary>
/// A compiled connector: an external system a process can call. Its base URL and its secret are
/// deployment bindings, held only by name, and activation checks that the tenant has them with the
/// right kind.
/// </summary>
public sealed record ConnectorModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The connector's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    /// <summary>The name of the <c>url</c> binding the operation paths are added to.</summary>
    public required string BaseUrlBinding { get; init; }

    /// <summary>How calls authenticate, or null when they send no credentials.</summary>
    public ConnectorAuthModel? Auth { get; init; }

    /// <summary>How long one call may take: at most 2 minutes, 30 seconds when not written.</summary>
    public required TimeSpan Timeout { get; init; }

    /// <summary>The request header that carries the idempotency key, or null when the system has none.</summary>
    public string? Idempotency { get; init; }

    /// <summary>The operations, in declaration order.</summary>
    public required IReadOnlyList<ConnectorOperationModel> Operations { get; init; }
}

/// <summary>
/// The auth of a compiled connector. <see cref="Binding"/> names the <c>secret</c> binding, which
/// <see cref="BindingPath"/> points to in the connector file. A <c>header</c> auth sends it in the
/// header <see cref="HeaderName"/>, and a <c>bearer</c> auth as <c>Authorization: Bearer</c>. An
/// <c>oauth2</c> auth gets a token with the client credentials grant and sends it as a bearer token.
/// Its <see cref="Binding"/> is the client secret, and <see cref="OAuth2"/> holds the rest.
/// </summary>
public sealed record ConnectorAuthModel(string Type, string? HeaderName, string Binding, string BindingPath)
{
    /// <summary>The token request of an <c>oauth2</c> auth, or null for another type.</summary>
    public ConnectorOAuth2Model? OAuth2 { get; init; }
}

/// <summary>
/// The token request of an <c>oauth2</c> connector auth: the names of the <c>url</c> binding of the
/// token endpoint and of the <c>secret</c> binding of the client id, and the <c>scope</c> and
/// <c>audience</c> to ask for, when written.
/// </summary>
public sealed record ConnectorOAuth2Model(string TokenUrlBinding, string ClientIdBinding, string? Scope, string? Audience);

/// <summary>
/// A compiled connector operation. <see cref="Path"/> holds no placeholder but
/// <c>{idempotencyKey}</c>. <see cref="Lookup"/> is the declared name of another operation of the
/// connector: a <c>GET</c> whose path holds <c>{idempotencyKey}</c>, with no inputs and the same
/// outputs.
/// </summary>
public sealed record ConnectorOperationModel(
    string Name,
    string Method,
    string Path,
    IReadOnlyList<ConnectorFieldModel> Input,
    IReadOnlyList<ConnectorFieldModel> Output,
    string? Lookup);

/// <summary>An input or output field of a compiled connector operation, with a scalar field type.</summary>
public sealed record ConnectorFieldModel(string Name, FieldType Type, bool Required);
