namespace Axis.Data.Records;

/// <summary>
/// Who answers for a record write and which task decision led to it. Every record write takes one,
/// filled by its caller as the table in the accountable user section of
/// docs/reference/module-contracts.md gives it. The deciding task, described in the next section
/// of that doc, is the task whose completion the write follows.
/// </summary>
/// <param name="AccountableUser">The user or service account that answers for the write, or <c>null</c> when no person does.</param>
/// <param name="DecisionTask">The id of the task whose completion led to the write, or <c>null</c> when no task decision did.</param>
public sealed record RecordWriteContext(string? AccountableUser, Guid? DecisionTask)
{
    /// <summary>A write no person answers for, such as seed data.</summary>
    public static RecordWriteContext None { get; } = new(null, null);
}
