namespace Axis.Api.Endpoints;

/// <summary>Request body for the bulk-delete records endpoint.</summary>
internal sealed record BulkDeleteRequest(IReadOnlyList<Guid> Ids);
