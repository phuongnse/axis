using Axis.BusinessObjects.Domain.ValueObjects;

namespace Axis.BusinessObjects.Application;

internal static class BusinessObjectRecordRuleBindingContract
{
    public const string TargetType = "business-object-field";
    public const string UseCaseOrTrigger = "field-validation";

    public static string TargetId(BusinessObjectDefinitionKey objectKey, string fieldKey) =>
        $"{objectKey.Value}.{fieldKey.Trim()}";
}
