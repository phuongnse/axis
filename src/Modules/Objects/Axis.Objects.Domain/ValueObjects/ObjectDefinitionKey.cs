using System.Globalization;
using System.Text;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Domain.ValueObjects;

public sealed class ObjectDefinitionKey : ValueObject
{
    public string Value { get; }

    private ObjectDefinitionKey(string value) => Value = value;

    public static Result<ObjectDefinitionKey> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<ObjectDefinitionKey>.Failure("Object key is required.");

        string candidate = value;
        if (!DefinitionKeyFormat.IsValid(candidate))
        {
            return Result<ObjectDefinitionKey>.Failure(
                "Object key must be 1-63 characters, start with a lowercase letter, " +
                "and contain only lowercase letters, digits, and underscores.");
        }

        return new ObjectDefinitionKey(candidate);
    }

    public static Result<ObjectDefinitionKey> CreateFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<ObjectDefinitionKey>.Failure("Object definition name is required.");

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

        if (key.Length > DefinitionKeyFormat.MaxLength)
            key = key[..DefinitionKeyFormat.MaxLength].TrimEnd('_');

        return key.Length == 0 ? "object" : key;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
