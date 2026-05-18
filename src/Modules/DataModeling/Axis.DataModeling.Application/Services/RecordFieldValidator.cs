using System.Globalization;
using System.Text.Json;
using Axis.DataModeling.Domain.Entities;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;

namespace Axis.DataModeling.Application.Services;

/// <summary>
/// Validates a record's data dictionary against a model's field definitions.
/// Returns a dictionary of field-name → error messages (empty = valid).
/// </summary>
public static class RecordFieldValidator
{
    public static Dictionary<string, string[]> Validate(
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyList<FieldDefinition> fields)
    {
        Dictionary<string, List<string>> errors = new();

        foreach (FieldDefinition field in fields)
        {
            if (field.IsSystem)
                continue;

            data.TryGetValue(field.Name, out object? raw);
            string? value = Coerce(raw);
            bool absent = !data.ContainsKey(field.Name) || raw is null || string.IsNullOrWhiteSpace(value);

            if (field.IsRequired && absent)
            {
                Add(errors, field.Name, $"'{field.Label}' is required.");
                continue;
            }

            if (absent)
                continue;

            switch (field.Config)
            {
                case TextFieldConfig text:
                    if (text.MinLength.HasValue && value!.Length < text.MinLength.Value)
                        Add(errors, field.Name, $"'{field.Label}' must be at least {text.MinLength} character(s).");
                    if (text.MaxLength.HasValue && value!.Length > text.MaxLength.Value)
                        Add(errors, field.Name, $"'{field.Label}' must not exceed {text.MaxLength} character(s).");
                    break;

                case NumberFieldConfig number:
                    if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal num))
                    {
                        if (number.Min.HasValue && num < number.Min.Value)
                            Add(errors, field.Name, $"'{field.Label}' must be at least {number.Min}.");
                        if (number.Max.HasValue && num > number.Max.Value)
                            Add(errors, field.Name, $"'{field.Label}' must not exceed {number.Max}.");
                    }
                    break;

                case EnumFieldConfig enumCfg:
                    if (!enumCfg.Options.Any(o => o.Value == value))
                        Add(errors, field.Name, $"'{field.Label}' must be one of: {string.Join(", ", enumCfg.Options.Select(o => o.Value))}.");
                    break;
            }
        }

        return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    private static string? Coerce(object? raw) =>
        raw switch
        {
            null => null,
            string s => s,
            JsonElement el when el.ValueKind == JsonValueKind.String => el.GetString(),
            JsonElement el when el.ValueKind == JsonValueKind.Number => el.GetRawText(),
            JsonElement el when el.ValueKind == JsonValueKind.True => "true",
            JsonElement el when el.ValueKind == JsonValueKind.False => "false",
            JsonElement el when el.ValueKind == JsonValueKind.Null => null,
            _ => Convert.ToString(raw, CultureInfo.InvariantCulture)
        };

    private static void Add(Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out List<string>? list))
        {
            list = [];
            errors[field] = list;
        }
        list.Add(message);
    }
}
