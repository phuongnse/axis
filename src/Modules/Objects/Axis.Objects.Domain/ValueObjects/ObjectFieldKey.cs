using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Domain.ValueObjects;

public sealed class ObjectFieldKey : ValueObject
{
    public string Value { get; }

    private ObjectFieldKey(string value) => Value = value;

    public static Result<ObjectFieldKey> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<ObjectFieldKey>.Failure("Field key is required.");

        string candidate = value;
        if (!DefinitionKeyFormat.IsValid(candidate))
        {
            return Result<ObjectFieldKey>.Failure(
                "Field key must be 1-63 characters, start with a lowercase letter, " +
                "and contain only lowercase letters, digits, and underscores.");
        }

        return new ObjectFieldKey(candidate);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
