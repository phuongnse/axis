namespace Axis.Objects.Domain.ValueObjects;

public readonly record struct ObjectFieldVariantId(Guid Value)
{
    public static ObjectFieldVariantId New() => new(Guid.NewGuid());
    public static ObjectFieldVariantId From(Guid value) => new(value);
}
