namespace Axis.Processes.Work;

/// <summary>A work item a worker holds until <see cref="LeaseExpiresAt"/>, by the database clock.</summary>
/// <param name="ClaimToken">Identifies this claim. Completing the item checks that it is still current.</param>
/// <param name="ProcessInstanceId">The process instance the item runs a step of, or <see langword="null"/> for other work.</param>
/// <param name="ConnectorCallId">The connector call a <c>connector.call</c> item sends, or <see langword="null"/> for other work.</param>
/// <param name="EventId">The event occurrence an <c>event.deliver</c> item delivers, or <see langword="null"/> for other work.</param>
/// <param name="Lease">The length the item was claimed for.</param>
public sealed record ClaimedWorkItem(
    Guid Id,
    string TenantId,
    string Kind,
    Guid ClaimToken,
    DateTimeOffset LeaseExpiresAt,
    Guid? ProcessInstanceId,
    Guid? ConnectorCallId,
    Guid? EventId,
    TimeSpan Lease);
