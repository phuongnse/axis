using Axis.Rules.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.Rules.Infrastructure.Persistence;

internal static class RuleValueConverters
{
    public static readonly ValueConverter<RuleDefinitionId, Guid> DefinitionId =
        new(id => id.Value, value => RuleDefinitionId.From(value));

    public static readonly ValueConverter<RuleDefinitionVersionId, Guid> DefinitionVersionId =
        new(id => id.Value, value => RuleDefinitionVersionId.From(value));

    public static readonly ValueConverter<RuleBindingId, Guid> BindingId =
        new(id => id.Value, value => RuleBindingId.From(value));

    public static readonly ValueConverter<RuleDefinitionKey, string> DefinitionKey =
        new(key => key.Value, value => RuleDefinitionKey.Create(value).Value);

}
