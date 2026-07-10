using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Rules.Contracts;
using ContractRuleScope = Axis.Rules.Contracts.RuleScope;
using ContractRuleValueType = Axis.Rules.Contracts.RuleValueType;

namespace Axis.BusinessObjects.Application;

public sealed class BusinessObjectRuleContextSchemaProvider : IRuleContextSchemaProvider
{
    private static readonly IReadOnlyList<RuleContextSchemaDto> Schemas =
    [
        FieldSchema("business_objects.field.text", "Text field value", ContractRuleValueType.Text),
        FieldSchema("business_objects.field.integer", "Integer field value", ContractRuleValueType.Integer),
        FieldSchema("business_objects.field.decimal", "Decimal field value", ContractRuleValueType.Decimal),
        FieldSchema("business_objects.field.date", "Date field value", ContractRuleValueType.Date),
        FieldSchema("business_objects.field.datetime", "Date and time field value", ContractRuleValueType.DateTime),
        FieldSchema("business_objects.field.boolean", "Boolean field value", ContractRuleValueType.Boolean),
        FieldSchema("business_objects.field.choice.single", "Single-choice field value", ContractRuleValueType.Text),
        FieldSchema(
            "business_objects.field.choice.multiple",
            "Multiple-choice field value",
            ContractRuleValueType.Text,
            allowMultiple: true),
    ];

    public Task<IReadOnlyList<RuleContextSchemaDto>> ListSchemasAsync(
        Guid workspaceId,
        ContractRuleScope? scope = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<RuleContextSchemaDto> result = scope is null or ContractRuleScope.Field
            ? Schemas
            : [];
        return Task.FromResult(result);
    }

    public Task<RuleContextSchemaDto?> FindSchemaAsync(
        Guid workspaceId,
        string contextKey,
        int version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RuleContextSchemaDto? schema = Schemas.SingleOrDefault(candidate =>
            candidate.ContextKey.Equals(contextKey, StringComparison.Ordinal) &&
            candidate.Version == version);
        return Task.FromResult(schema);
    }

    public static string ContextKeyFor(
        BusinessObjectFieldType fieldType,
        BusinessObjectChoiceSelectionMode? choiceSelectionMode = null) =>
        fieldType switch
        {
            BusinessObjectFieldType.Text => "business_objects.field.text",
            BusinessObjectFieldType.Integer => "business_objects.field.integer",
            BusinessObjectFieldType.Decimal => "business_objects.field.decimal",
            BusinessObjectFieldType.Date => "business_objects.field.date",
            BusinessObjectFieldType.DateTime => "business_objects.field.datetime",
            BusinessObjectFieldType.Boolean => "business_objects.field.boolean",
            BusinessObjectFieldType.Choice when choiceSelectionMode == BusinessObjectChoiceSelectionMode.Single =>
                "business_objects.field.choice.single",
            BusinessObjectFieldType.Choice when choiceSelectionMode == BusinessObjectChoiceSelectionMode.Multiple =>
                "business_objects.field.choice.multiple",
            _ => throw new InvalidOperationException("Field type configuration does not have a rule context schema."),
        };

    private static RuleContextSchemaDto FieldSchema(
        string key,
        string displayName,
        ContractRuleValueType valueType,
        bool allowMultiple = false) =>
        new(
            key,
            Version: 1,
            ContractRuleScope.Field,
            displayName,
            [new RuleContextFieldDto("field.value", "Field value", valueType, allowMultiple)],
            TargetTypeKey: key.Contains(".choice.", StringComparison.Ordinal)
                ? BusinessObjectFieldType.Choice.ToString()
                : key.Split('.')[^1] switch
                {
                    "datetime" => BusinessObjectFieldType.DateTime.ToString(),
                    string suffix => char.ToUpperInvariant(suffix[0]) + suffix[1..],
                },
            Configuration: key.EndsWith(".choice.single", StringComparison.Ordinal)
                ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["selection_mode"] = [BusinessObjectChoiceSelectionMode.Single.ToString()],
                }
                : key.EndsWith(".choice.multiple", StringComparison.Ordinal)
                    ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                    {
                        ["selection_mode"] = [BusinessObjectChoiceSelectionMode.Multiple.ToString()],
                    }
                    : new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
}
