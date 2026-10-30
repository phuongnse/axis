namespace Axis.Configuration.Resources;

/// <summary>
/// The auth of a connector as written. A <c>bearer</c> auth has a <see cref="Token"/>, and a
/// <c>header</c> auth has a header <see cref="Name"/> and a <see cref="Value"/>. Both name
/// <c>secret</c> bindings. An <c>oauth2</c> auth has a <see cref="TokenUrl"/> (a <c>url</c> binding),
/// a <see cref="ClientId"/> and a <see cref="ClientSecret"/> (<c>secret</c> bindings), and may have a
/// <see cref="Scope"/> and an <see cref="Audience"/>. The schema checks which properties each type has.
/// </summary>
public sealed record ConnectorAuthDefinition
{
    /// <summary>One of <c>none</c>, <c>bearer</c>, <c>header</c> and <c>oauth2</c>.</summary>
    public required string Type { get; init; }

    public BindingReference? Token { get; init; }

    public string? Name { get; init; }

    public BindingReference? Value { get; init; }

    public BindingReference? TokenUrl { get; init; }

    public BindingReference? ClientId { get; init; }

    public BindingReference? ClientSecret { get; init; }

    public string? Scope { get; init; }

    public string? Audience { get; init; }
}
