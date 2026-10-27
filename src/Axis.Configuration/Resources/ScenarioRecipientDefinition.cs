namespace Axis.Configuration.Resources;

/// <summary>
/// The recipient an <c>expectNotification</c> step asserts as written: a scenario user, a role or an
/// email address. The JSON Schema checks that exactly one is set.
/// </summary>
public sealed record ScenarioRecipientDefinition
{
    public string? User { get; init; }

    public string? Role { get; init; }

    public string? Email { get; init; }
}
