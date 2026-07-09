using Axis.Objects.Domain.Aggregates;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Application;

internal static class ObjectDefinitionMapper
{
    public static ObjectDefinitionDetailDto ToDetailDto(ObjectDefinition definition)
    {
        ObjectDefinitionVersion? latestVersion = definition.Versions
            .OrderByDescending(version => version.VersionNumber)
            .FirstOrDefault();

        return new ObjectDefinitionDetailDto(
            definition.Id.Value,
            definition.WorkspaceId,
            definition.Name,
            definition.Key.Value,
            definition.Status,
            definition.Revision,
            definition.LatestPublishedVersionNumber,
            definition.CreatedAt,
            definition.UpdatedAt,
            definition.Fields.OrderBy(field => field.Order).Select(ToFieldDto).ToList(),
            latestVersion is null ? null : ToVersionDto(latestVersion));
    }

    public static ObjectDefinitionListItemDto ToListItemDto(ObjectDefinition definition) =>
        new(
            definition.Id.Value,
            definition.Name,
            definition.Key.Value,
            definition.Status,
            definition.Revision,
            definition.LatestPublishedVersionNumber,
            definition.UpdatedAt);

    public static ObjectDefinitionVersionDto ToVersionDto(ObjectDefinitionVersion version) =>
        new(
            version.Id.Value,
            version.VersionNumber,
            version.PublishedByUserId,
            version.PublishedAt,
            version.Fields.OrderBy(field => field.Order).Select(ToVersionFieldDto).ToList());

    public static Result<IReadOnlyList<ObjectFieldDefinitionSpec>> ToDomainSpecs(
        IReadOnlyList<ObjectFieldDefinitionInput> fields,
        IFieldRuleApplicationValidator fieldRuleValidator)
    {
        List<ObjectFieldDefinitionSpec> specs = [];
        for (int index = 0; index < fields.Count; index++)
        {
            ObjectFieldDefinitionInput field = fields[index];
            Result<IReadOnlyList<ObjectFieldRuleSpec>> rules = ToDomainRuleSpecs(field, fieldRuleValidator);
            if (rules.IsFailure)
                return Result.Failure<IReadOnlyList<ObjectFieldDefinitionSpec>>(rules.Error);

            specs.Add(new ObjectFieldDefinitionSpec(
                field.FieldKey,
                field.Label,
                index,
                field.FieldType,
                rules.Value));
        }

        return specs;
    }

    private static ObjectFieldDefinitionDto ToFieldDto(ObjectFieldDefinition field) =>
        new(
            field.Id.Value,
            field.Key.Value,
            field.Label,
            field.Order,
            field.FieldType,
            field.Rules.OrderBy(rule => rule.Order).Select(ToRuleDto).ToList());

    private static ObjectFieldDefinitionDto ToVersionFieldDto(ObjectDefinitionVersionField field) =>
        new(
            field.Id.Value,
            field.Key.Value,
            field.Label,
            field.Order,
            field.FieldType,
            field.Rules.OrderBy(rule => rule.Order).Select(ToRuleDto).ToList());

    private static Result<IReadOnlyList<ObjectFieldRuleSpec>> ToDomainRuleSpecs(
        ObjectFieldDefinitionInput field,
        IFieldRuleApplicationValidator fieldRuleValidator)
    {
        if (field.Rules is null || field.Rules.Count == 0)
            return Array.Empty<ObjectFieldRuleSpec>();

        List<ObjectFieldRuleSpec> rules = [];
        foreach (ObjectFieldRuleInput rule in field.Rules)
        {
            IReadOnlyDictionary<string, IReadOnlyList<string>> parameters =
                rule.Parameters ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            FieldRuleApplicationValidationResult validation =
                fieldRuleValidator.ValidateFieldRuleApplication(
                    rule.DefinitionKey,
                    field.FieldType.ToString(),
                    parameters);

            if (!validation.IsValid)
                return Result.Failure<IReadOnlyList<ObjectFieldRuleSpec>>(validation.Error!);

            rules.Add(new ObjectFieldRuleSpec(rule.DefinitionKey, parameters));
        }

        return rules;
    }

    private static ObjectFieldRuleDto ToRuleDto(ObjectFieldRule rule) =>
        new(
            rule.DefinitionKey,
            ToParameterDto(rule.Parameters));

    private static ObjectFieldRuleDto ToRuleDto(ObjectDefinitionVersionFieldRule rule) =>
        new(
            rule.DefinitionKey,
            ToParameterDto(rule.Parameters));

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ToParameterDto(
        IReadOnlyDictionary<string, string[]> parameters) =>
        parameters.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.Ordinal);
}
