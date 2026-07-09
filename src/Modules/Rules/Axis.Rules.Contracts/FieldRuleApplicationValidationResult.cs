namespace Axis.Rules.Contracts;

public sealed record FieldRuleApplicationValidationResult(bool IsValid, string? Error)
{
    public static FieldRuleApplicationValidationResult Valid() => new(true, null);

    public static FieldRuleApplicationValidationResult Invalid(string error) => new(false, error);
}
