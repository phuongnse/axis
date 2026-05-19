namespace Axis.Api.Endpoints;

public record ReorderFormFieldsRequest(IReadOnlyList<Guid> FieldIds);
