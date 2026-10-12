namespace Axis.Configuration.Resources;

/// <summary>
/// The auth of a connector as written. A <c>bearer</c> auth has a <see cref="Token"/>, and a
/// <c>header</c> auth has a header <see cref="Name"/> and a <see cref="Value"/>. Both name
/// <c>secret</c> bindings. The schema checks which properties each type has.
/// </summary>
public sealed record ConnectorAuthDefinition
{
    /// <summary>One of <c>none</c>, <c>bearer</c> and <c>header</c>.</summary>
    public required string Type { get; init; }

    public BindingReference? Token { get; init; }

    public string? Name { get; init; }

    public BindingReference? Value { get; init; }
}
