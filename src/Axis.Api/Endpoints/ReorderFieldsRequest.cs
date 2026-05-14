namespace Axis.Api.Endpoints;

public record ReorderFieldsRequest(IReadOnlyList<Guid> FieldIds);
