using System.Globalization;
using System.Text.Json;
using Axis.FormBuilder.Domain.Entities;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ValueObjects;

namespace Axis.FormBuilder.Application.Services;

/// <summary>
/// Validates submitted form data against form field definitions.
/// </summary>
public static class FormSubmissionFieldValidator
{
    public static Dictionary<string, string[]> Validate(
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyList<FormField> fields)
    {
        Dictionary<string, List<string>> errors = new();

        foreach (FormField field in fields)
        {
            if (field.Type == FormFieldType.Section)
                continue;

            data.TryGetValue(field.Key, out object? raw);
            string? value = CoerceScalar(raw);
            bool absent = !data.ContainsKey(field.Key) || raw is null
                || (field.Type != FormFieldType.MultiSelect && string.IsNullOrWhiteSpace(value));

            if (field.IsRequired && absent)
            {
                Add(errors, field.Key, $"'{field.Label}' is required.");
                continue;
            }

            if (absent)
                continue;

            switch (field.Type)
            {
                case FormFieldType.Text when field.Config is TextFormFieldConfig text:
                    if (text.MinLength.HasValue && value!.Length < text.MinLength.Value)
                        Add(errors, field.Key, $"'{field.Label}' must be at least {text.MinLength} character(s).");
                    if (text.MaxLength.HasValue && value!.Length > text.MaxLength.Value)
                        Add(errors, field.Key, $"'{field.Label}' must not exceed {text.MaxLength} character(s).");
                    break;

                case FormFieldType.Number when field.Config is NumberFormFieldConfig number:
                    if (!decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal num))
                    {
                        Add(errors, field.Key, $"'{field.Label}' must be a valid number.");
                    }
                    else
                    {
                        if (number.Min.HasValue && num < number.Min.Value)
                            Add(errors, field.Key, $"'{field.Label}' must be at least {number.Min}.");
                        if (number.Max.HasValue && num > number.Max.Value)
                            Add(errors, field.Key, $"'{field.Label}' must not exceed {number.Max}.");
                    }
                    break;

                case FormFieldType.Dropdown when field.Config is DropdownFieldConfig dropdown:
                    if (!dropdown.Options.Any(o => o.Value == value))
                        Add(errors, field.Key, $"'{field.Label}' must be one of the allowed options.");
                    break;

                case FormFieldType.Boolean:
                    if (!bool.TryParse(value, out _))
                        Add(errors, field.Key, $"'{field.Label}' must be true or false.");
                    break;

                case FormFieldType.Date:
                    if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
                        Add(errors, field.Key, $"'{field.Label}' must be a valid date.");
                    break;

                case FormFieldType.MultiSelect when field.Config is MultiSelectFieldConfig multi:
                    if (!TryCoerceStringArray(raw, out IReadOnlyList<string> selected) || selected.Count == 0)
                    {
                        Add(errors, field.Key, $"'{field.Label}' requires at least one selection.");
                    }
                    else
                    {
                        HashSet<string> allowed = multi.Options.Select(o => o.Value).ToHashSet(StringComparer.Ordinal);
                        foreach (string item in selected)
                        {
                            if (!allowed.Contains(item))
                            {
                                Add(errors, field.Key, $"'{field.Label}' contains an invalid option.");
                                break;
                            }
                        }
                    }
                    break;
            }
        }

        return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    private static string? CoerceScalar(object? raw) =>
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

    private static bool TryCoerceStringArray(object? raw, out IReadOnlyList<string> values)
    {
        values = Array.Empty<string>();
        if (raw is null)
            return false;

        if (raw is JsonElement { ValueKind: JsonValueKind.Array } array)
        {
            List<string> list = new();
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    list.Add(item.GetString() ?? string.Empty);
                else
                    list.Add(item.GetRawText());
            }

            values = list;
            return true;
        }

        if (raw is IEnumerable<string> strings)
        {
            values = strings.ToList();
            return true;
        }

        return false;
    }

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
