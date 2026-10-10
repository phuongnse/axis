namespace Axis.Configuration.Resources;

/// <summary>
/// The assignee of a task step as written: a <c>text</c> expression over the subject record that
/// gives a user id, or a role name. Whether exactly one is set is checked by
/// <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record TaskAssigneeDefinition
{
    public string? User { get; init; }

    public string? Role { get; init; }
}
