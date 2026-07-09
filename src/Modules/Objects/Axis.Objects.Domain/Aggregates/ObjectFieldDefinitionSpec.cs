namespace Axis.Objects.Domain.Aggregates;

public sealed record ObjectFieldDefinitionSpec(
    string FieldKey,
    string Label,
    int Order,
    ObjectFieldType FieldType = ObjectFieldType.Text,
    IReadOnlyList<ObjectFieldVariantSpec>? Variants = null);
