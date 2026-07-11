using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.ValueObjects;

public sealed class BusinessObjectFieldKey : ValueObject
{
    public string Value { get; }

    private BusinessObjectFieldKey(string value) => Value = value;

    public static Result<BusinessObjectFieldKey> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<BusinessObjectFieldKey>.Failure("Field key is required.");

        string candidate = value;
        if (!BusinessObjectKeyFormat.IsValid(candidate))
        {
            return Result<BusinessObjectFieldKey>.Failure(
                "Field key must be 1-63 characters, start with a lowercase letter, " +
                "and contain only lowercase letters, digits, and underscores.");
        }

        return new BusinessObjectFieldKey(candidate);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
