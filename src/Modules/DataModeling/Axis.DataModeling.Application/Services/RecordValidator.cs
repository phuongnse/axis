using System.Text.RegularExpressions;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.Entities;
using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using FluentValidation.Results;

namespace Axis.DataModeling.Application.Services;

public sealed class RecordValidator : IRecordValidator
{
    public Task<List<ValidationFailure>> ValidateAsync(DataModel model, IReadOnlyDictionary<string, object?> data, CancellationToken ct = default)
    {
        List<ValidationFailure> errors = [];

        foreach (var field in model.Fields)
        {
            if (field.IsSystem) continue;

            bool hasValue = data.TryGetValue(field.Name, out var val) && val is not null;

            if (val is string str && string.IsNullOrWhiteSpace(str))
            {
                hasValue = false;
            }

            if (field.IsRequired && !hasValue)
            {
                errors.Add(new ValidationFailure(field.Name, $"Field '{field.Label}' is required."));
                continue;
            }

            if (!hasValue) continue;

            ValidateFieldType(field, val!, errors);
        }

        return Task.FromResult(errors);
    }

    private void ValidateFieldType(FieldDefinition field, object value, List<ValidationFailure> errors)
    {
        switch (field.Type)
        {
            case FieldType.Text:
                if (value is not string stringValue)
                {
                    errors.Add(new ValidationFailure(field.Name, $"Field '{field.Label}' must be a string."));
                    return;
                }
                if (field.Config is TextFieldConfig textConfig)
                {
                    if (textConfig.MinLength.HasValue && stringValue.Length < textConfig.MinLength.Value)
                        errors.Add(new ValidationFailure(field.Name, $"Field '{field.Label}' must be at least {textConfig.MinLength.Value} characters."));
                    if (textConfig.MaxLength.HasValue && stringValue.Length > textConfig.MaxLength.Value)
                        errors.Add(new ValidationFailure(field.Name, $"Field '{field.Label}' must be at most {textConfig.MaxLength.Value} characters."));
                }
                break;

            case FieldType.Number:
                if (!decimal.TryParse(value.ToString(), out decimal numValue))
                {
                    errors.Add(new ValidationFailure(field.Name, $"Field '{field.Label}' must be a number."));
                    return;
                }
                if (field.Config is NumberFieldConfig numConfig)
                {
                    if (numConfig.Min.HasValue && numValue < numConfig.Min.Value)
                        errors.Add(new ValidationFailure(field.Name, $"Field '{field.Label}' must be greater than or equal to {numConfig.Min.Value}."));
                    if (numConfig.Max.HasValue && numValue > numConfig.Max.Value)
                        errors.Add(new ValidationFailure(field.Name, $"Field '{field.Label}' must be less than or equal to {numConfig.Max.Value}."));
                }
                break;

            case FieldType.Date:
                if (!DateTime.TryParse(value.ToString(), out DateTime dateValue))
                {
                    errors.Add(new ValidationFailure(field.Name, $"Field '{field.Label}' must be a valid date."));
                    return;
                }
                if (field.Config is DateFieldConfig dateConfig)
                {
                    if (dateConfig.MinDate.HasValue && dateValue < dateConfig.MinDate.Value)
                        errors.Add(new ValidationFailure(field.Name, $"Field '{field.Label}' must be on or after {dateConfig.MinDate.Value:yyyy-MM-dd}."));
                    if (dateConfig.MaxDate.HasValue && dateValue > dateConfig.MaxDate.Value)
                        errors.Add(new ValidationFailure(field.Name, $"Field '{field.Label}' must be on or before {dateConfig.MaxDate.Value:yyyy-MM-dd}."));
                }
                break;

            case FieldType.Enum:
                if (field.Config is EnumFieldConfig enumConfig)
                {
                    if (enumConfig.AllowMultiple)
                    {
                        if (value is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                             var arr = jsonElement.EnumerateArray().Select(x => x.GetString()).ToList();
                             foreach(var item in arr)
                             {
                                 if (!enumConfig.Options.Any(o => o.Value == item))
                                 {
                                     errors.Add(new ValidationFailure(field.Name, $"Value '{item}' is not a valid option for '{field.Label}'."));
                                 }
                             }
                        }
                        else
                        {
                            errors.Add(new ValidationFailure(field.Name, $"Field '{field.Label}' must be an array of valid options."));
                        }
                    }
                    else
                    {
                        string? strVal = value.ToString();
                        if (!enumConfig.Options.Any(o => o.Value == strVal))
                            errors.Add(new ValidationFailure(field.Name, $"Value '{strVal}' is not a valid option for '{field.Label}'."));
                    }
                }
                break;

            case FieldType.Boolean:
                if (value is not bool && !bool.TryParse(value.ToString(), out _))
                {
                    errors.Add(new ValidationFailure(field.Name, $"Field '{field.Label}' must be a boolean."));
                }
                break;
        }
    }
}
