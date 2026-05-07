namespace Axis.DataModeling.Domain.ValueObjects;

/// <summary>Base type for all field configuration value objects.</summary>
public abstract record FieldConfig;

public sealed record TextFieldConfig(
    int? MinLength = null,
    int? MaxLength = null,
    bool Multiline = false,
    string? DefaultValue = null) : FieldConfig;

public sealed record NumberFieldConfig(
    decimal? Min = null,
    decimal? Max = null,
    int? DecimalPlaces = null,
    decimal? DefaultValue = null) : FieldConfig;

public sealed record BooleanFieldConfig(bool DefaultValue = false) : FieldConfig;

public sealed record DateFieldConfig(
    bool IncludeTime = false,
    DateTime? MinDate = null,
    DateTime? MaxDate = null,
    string? DefaultValue = null) : FieldConfig;

public sealed record EnumFieldConfig(
    IReadOnlyList<EnumOption> Options,
    bool AllowMultiple = false,
    string? DefaultValue = null) : FieldConfig;

public sealed record RelationFieldConfig(
    Guid TargetModelId,
    bool AllowMultiple = false,
    string? DisplayField = null) : FieldConfig;

public sealed record DataClassFieldConfig(Guid DataClassId) : FieldConfig;

public sealed record FileFieldConfig(
    IReadOnlyList<string>? AllowedExtensions = null,
    float? MaxSizeMb = null,
    int? MaxFiles = null) : FieldConfig;

public sealed record JsonFieldConfig() : FieldConfig;

public sealed record EnumOption(string Value, string Label);
