using Axis.BusinessObjects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.BusinessObjects.Infrastructure.Persistence.Configurations;

internal static class BusinessObjectValueConverters
{
    public static readonly ValueConverter<BusinessObjectDefinitionId, Guid> DefinitionId =
        new(id => id.Value, value => BusinessObjectDefinitionId.From(value));

    public static readonly ValueConverter<BusinessObjectFieldDefinitionId, Guid> FieldDefinitionId =
        new(id => id.Value, value => BusinessObjectFieldDefinitionId.From(value));

    public static readonly ValueConverter<BusinessObjectFieldRuleId, Guid> FieldRuleId =
        new(id => id.Value, value => BusinessObjectFieldRuleId.From(value));

    public static readonly ValueConverter<BusinessObjectChoiceOptionId, Guid> ChoiceOptionId =
        new(id => id.Value, value => BusinessObjectChoiceOptionId.From(value));

    public static readonly ValueConverter<BusinessObjectDefinitionVersionId, Guid> DefinitionVersionId =
        new(id => id.Value, value => BusinessObjectDefinitionVersionId.From(value));

    public static readonly ValueConverter<BusinessObjectDefinitionVersionFieldId, Guid> DefinitionVersionFieldId =
        new(id => id.Value, value => BusinessObjectDefinitionVersionFieldId.From(value));

    public static readonly ValueConverter<BusinessObjectDefinitionVersionChoiceOptionId, Guid>
        DefinitionVersionChoiceOptionId =
            new(id => id.Value, value => BusinessObjectDefinitionVersionChoiceOptionId.From(value));

    public static readonly ValueConverter<BusinessObjectDefinitionVersionFieldRuleId, Guid>
        DefinitionVersionFieldRuleId =
            new(id => id.Value, value => BusinessObjectDefinitionVersionFieldRuleId.From(value));

    public static readonly ValueConverter<BusinessObjectDefinitionKey, string> DefinitionKey =
        new(key => key.Value, value => BusinessObjectDefinitionKey.Create(value).Value);

    public static readonly ValueConverter<BusinessObjectFieldKey, string> FieldKey =
        new(key => key.Value, value => BusinessObjectFieldKey.Create(value).Value);

    public static readonly ValueConverter<BusinessObjectChoiceOptionKey, string> ChoiceOptionKey =
        new(key => key.Value, value => BusinessObjectChoiceOptionKey.Create(value).Value);
}
