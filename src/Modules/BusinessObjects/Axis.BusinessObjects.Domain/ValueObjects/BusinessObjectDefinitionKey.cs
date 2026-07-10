using System.Globalization;
using System.Text;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.ValueObjects;

public sealed class BusinessObjectDefinitionKey : ValueObject
{
    public string Value { get; }

    private BusinessObjectDefinitionKey(string value) => Value = value;

    public static Result<BusinessObjectDefinitionKey> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<BusinessObjectDefinitionKey>.Failure("Object key is required.");

        string candidate = value;
        if (!BusinessObjectKeyFormat.IsValid(candidate))
        {
            return Result<BusinessObjectDefinitionKey>.Failure(
                "Object key must be 1-63 characters, start with a lowercase letter, " +
                "and contain only lowercase letters, digits, and underscores.");
        }

        return new BusinessObjectDefinitionKey(candidate);
    }

    public static Result<BusinessObjectDefinitionKey> CreateFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<BusinessObjectDefinitionKey>.Failure("Object definition name is required.");

        return Create(DeriveKey(name));
    }

    private static string DeriveKey(string name)
    {
        StringBuilder builder = new();
        foreach (char rawCharacter in name.Trim().Normalize(NormalizationForm.FormD))
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(rawCharacter);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            char character = char.ToLowerInvariant(rawCharacter);
            if (character == '\u0111')
                character = 'd';

            if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
            {
                builder.Append(character);
                continue;
            }

            if (builder.Length > 0 && builder[^1] != '_')
                builder.Append('_');
        }

        string key = builder.ToString().Trim('_');
        if (key.Length == 0)
            key = "object";

        if (key[0] < 'a' || key[0] > 'z')
            key = $"object_{key}";

        if (key.Length > BusinessObjectKeyFormat.MaxLength)
            key = key[..BusinessObjectKeyFormat.MaxLength].TrimEnd('_');

        return key.Length == 0 ? "object" : key;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
