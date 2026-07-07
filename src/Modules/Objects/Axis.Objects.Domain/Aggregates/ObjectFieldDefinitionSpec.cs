namespace Axis.Objects.Domain.Aggregates;

public sealed record ObjectFieldDefinitionSpec(
    string FieldKey,
    string Label,
    int Order);
