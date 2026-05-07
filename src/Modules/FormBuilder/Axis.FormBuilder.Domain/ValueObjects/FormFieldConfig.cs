namespace Axis.FormBuilder.Domain.ValueObjects;

public abstract record FormFieldConfig;

public sealed record TextFormFieldConfig(
    int? MinLength = null,
    int? MaxLength = null,
    string? Placeholder = null,
    string? DefaultValue = null) : FormFieldConfig;

public sealed record NumberFormFieldConfig(
    decimal? Min = null,
    decimal? Max = null,
    int? DecimalPlaces = null) : FormFieldConfig;

public sealed record DateFormFieldConfig(bool IncludeTime = false) : FormFieldConfig;

public sealed record DropdownFieldConfig(
    IReadOnlyList<DropdownOption> Options,
    string? DefaultValue = null) : FormFieldConfig;

public sealed record MultiSelectFieldConfig(
    IReadOnlyList<DropdownOption> Options) : FormFieldConfig;

public sealed record RelationPickerFieldConfig(Guid TargetModelId, bool AllowMultiple = false) : FormFieldConfig;

public sealed record FileUploadFieldConfig(
    IReadOnlyList<string>? AllowedExtensions = null,
    float? MaxSizeMb = null) : FormFieldConfig;

public sealed record SectionFieldConfig(string? SectionDescription = null) : FormFieldConfig;

public sealed record DropdownOption(string Value, string Label);
