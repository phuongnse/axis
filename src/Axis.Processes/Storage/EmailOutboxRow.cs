namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.email_outbox</c>: one rendered email for one address, which a <c>notify</c> step or a task
/// escalation with the <c>email</c> channel staged. It queues one <c>notification.email</c> work item for the row in the
/// same transaction. Times use the database clock.
/// </summary>
public sealed class EmailOutboxRow
{
    /// <summary>The row's id, a version 7 UUID. The row's work item names it as its <c>email_id</c>.</summary>
    public required Guid Id { get; init; }

    public required Guid ApplicationId { get; init; }

    /// <summary>The release the notifying instance is pinned to. It gave the texts.</summary>
    public required Guid ReleaseId { get; init; }

    /// <summary>The id of the notification the step or escalation stored for its in-app recipients. All the emails of one step run or escalation share it.</summary>
    public required Guid NotificationId { get; init; }

    /// <summary>The name of the <c>notify</c> step, or of the task step whose escalation notified.</summary>
    public required string Step { get; init; }

    /// <summary>The address the email goes to.</summary>
    public required string Address { get; init; }

    /// <summary>The locale of the texts the email was rendered in, or null when the release has no texts.</summary>
    public string? Locale { get; init; }

    public required string Subject { get; init; }

    public required string Body { get; init; }

    /// <summary>The email's <c>Message-ID</c> header, unique among the rows.</summary>
    public required string MessageId { get; init; }

    /// <summary>The raw evaluated placeholder values of the step run, as a JSON object keyed by the placeholders' names.</summary>
    public string Values { get; init; } = "{}";

    /// <summary>The state of the email: <c>pending</c> or <c>sent</c>.</summary>
    public string State { get; init; } = "pending";

    /// <summary>The start of the staging step's transaction.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the email was sent, or null while it is pending.</summary>
    public DateTimeOffset? SentAt { get; init; }
}
