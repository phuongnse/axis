namespace Axis.Data.Storage;

/// <summary>A row of <c>axis.provisioned_enum_values</c>: a value recorded for an enum field.</summary>
public sealed class ProvisionedEnumValueRow
{
    public required Guid EntityId { get; init; }

    public required string FieldName { get; init; }

    public required string Value { get; init; }
}
