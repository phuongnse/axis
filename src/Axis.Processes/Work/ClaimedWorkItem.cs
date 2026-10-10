namespace Axis.Processes.Work;

/// <summary>A work item a worker holds until <see cref="LeaseExpiresAt"/>, by the database clock.</summary>
/// <param name="ClaimToken">Identifies this claim. Completing the item checks that it is still current.</param>
public sealed record ClaimedWorkItem(Guid Id, string TenantId, string Kind, Guid ClaimToken, DateTimeOffset LeaseExpiresAt);
