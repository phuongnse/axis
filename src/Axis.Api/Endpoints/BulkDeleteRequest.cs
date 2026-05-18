namespace Axis.Api.Endpoints;

/// <summary>
/// Request body for the bulk-delete records endpoint.
/// Ids is nullable to guard against JSON deserializing a missing/null property —
/// the endpoint coalesces null to an empty list so the handler's validation produces the correct error.
/// </summary>
internal sealed record BulkDeleteRequest(IReadOnlyList<Guid>? Ids);
