using System.Diagnostics;

namespace Axis.Processes.Work;

/// <summary>A work item a worker holds until <see cref="LeaseExpiresAt"/>, by the database clock.</summary>
/// <param name="ClaimToken">Identifies this claim. Completing the item checks that it is still current.</param>
/// <param name="ProcessInstanceId">The process instance the item runs a step of, or <see langword="null"/> for other work.</param>
/// <param name="ConnectorCallId">The connector call a <c>connector.call</c> item sends, or <see langword="null"/> for other work.</param>
/// <param name="PackageCallId">The package call a <c>package.call</c> item runs, or <see langword="null"/> for other work.</param>
/// <param name="EventId">The event occurrence an <c>event.deliver</c> item delivers, or <see langword="null"/> for other work.</param>
/// <param name="TriggerId">The schedule trigger a <c>trigger.schedule</c> or <c>trigger.fanOut</c> item runs, or <see langword="null"/> for other work.</param>
/// <param name="ReleaseId">The release whose trigger a <c>trigger.fanOut</c> item runs, or <see langword="null"/> for other work.</param>
/// <param name="OccurrenceAt">The occurrence a <c>trigger.fanOut</c> item runs, or <see langword="null"/> for other work.</param>
/// <param name="Cursor">The last record id a <c>trigger.fanOut</c> item handled, or <see langword="null"/> before its first batch and for other work.</param>
/// <param name="Lease">The length the item was claimed for.</param>
/// <param name="EntityId">The entity a <c>computed.backfill</c> item recomputes, or <see langword="null"/> for other work.</param>
/// <param name="QueuedBy">The span that queued the item, which the item's run links to, or <see langword="null"/> when the item stores none.</param>
/// <param name="TaskId">The task a <c>task.deadline</c> item expires or a <c>task.escalate</c> item escalates, or <see langword="null"/> for other work.</param>
/// <param name="Revision">The instance revision a <c>process.timer</c> item resumes at, or <see langword="null"/> for other work.</param>
/// <param name="Escalation">The zero-based index of the escalation a <c>task.escalate</c> item runs, or <see langword="null"/> for other work.</param>
/// <param name="EmailId">The outbox email a <c>notification.email</c> item sends, or <see langword="null"/> for other work.</param>
/// <param name="WebhookDeliveryId">The webhook delivery a <c>webhook.deliver</c> item sends, or <see langword="null"/> for other work.</param>
/// <param name="ClaimDelay">How long after its due time the item was claimed, by the clock of the claim.</param>
public sealed record ClaimedWorkItem(
    Guid Id,
    string TenantId,
    string Kind,
    Guid ClaimToken,
    DateTimeOffset LeaseExpiresAt,
    Guid? ProcessInstanceId,
    Guid? ConnectorCallId,
    Guid? EventId,
    Guid? PackageCallId,
    Guid? TriggerId,
    Guid? ReleaseId,
    DateTimeOffset? OccurrenceAt,
    Guid? Cursor,
    TimeSpan Lease,
    Guid? EntityId,
    ActivityContext? QueuedBy = null,
    Guid? TaskId = null,
    long? Revision = null,
    int? Escalation = null,
    Guid? EmailId = null,
    TimeSpan ClaimDelay = default,
    Guid? WebhookDeliveryId = null);
