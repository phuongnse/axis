using Axis.Objects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.Objects.Infrastructure.Persistence.Configurations;

internal static class ObjectValueConverters
{
    public static readonly ValueConverter<ObjectDefinitionId, Guid> DefinitionId =
        new(id => id.Value, value => ObjectDefinitionId.From(value));

    public static readonly ValueConverter<ObjectFieldDefinitionId, Guid> FieldDefinitionId =
        new(id => id.Value, value => ObjectFieldDefinitionId.From(value));

    public static readonly ValueConverter<ObjectFieldVariantId, Guid> FieldVariantId =
        new(id => id.Value, value => ObjectFieldVariantId.From(value));

    public static readonly ValueConverter<ObjectDefinitionVersionId, Guid> DefinitionVersionId =
        new(id => id.Value, value => ObjectDefinitionVersionId.From(value));

    public static readonly ValueConverter<ObjectDefinitionKey, string> DefinitionKey =
        new(key => key.Value, value => ObjectDefinitionKey.Create(value).Value);

    public static readonly ValueConverter<ObjectFieldKey, string> FieldKey =
        new(key => key.Value, value => ObjectFieldKey.Create(value).Value);
}
