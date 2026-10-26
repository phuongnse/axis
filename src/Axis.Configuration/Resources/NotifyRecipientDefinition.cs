namespace Axis.Configuration.Resources;

/// <summary>
/// A recipient of a <c>notify</c> step as written: a <c>text</c> expression that gives a user id, a
/// role name, or a <c>text</c> expression that gives an email address. Whether exactly one is set is
/// checked by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record NotifyRecipientDefinition
{
    public string? User { get; init; }

    public string? Role { get; init; }

    public string? Email { get; init; }
}
