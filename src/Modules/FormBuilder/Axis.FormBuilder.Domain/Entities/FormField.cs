using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Domain.Entities;

/// <summary>A single field or section within a form definition.</summary>
public sealed class FormField : Entity<Guid>
{
    public string Key { get; private set; }
    public string Label { get; private set; }
    public string? HelpText { get; private set; }
    public FormFieldType Type { get; private set; }
    public bool IsRequired { get; private set; }
    public int DisplayOrder { get; internal set; }
    public FormFieldConfig? Config { get; private set; }

    private FormField() : base(default) { Key = null!; Label = null!; } // EF Core materialisation

    private FormField(Guid id, string key, string label, string? helpText,
        FormFieldType type, bool isRequired, int displayOrder, FormFieldConfig? config)
        : base(id)
    {
        Key = key;
        Label = label;
        HelpText = helpText;
        Type = type;
        IsRequired = isRequired;
        DisplayOrder = displayOrder;
        Config = config;
    }

    internal static FormField Create(string key, string label, FormFieldType type,
        bool isRequired, int displayOrder, FormFieldConfig? config)
        => new(Guid.NewGuid(), key, label, null, type, isRequired, displayOrder, config);

    internal static FormField Reconstitute(Guid id, string key, string label, string? helpText,
        FormFieldType type, bool isRequired, int displayOrder, FormFieldConfig? config)
        => new(id, key, label, helpText, type, isRequired, displayOrder, config);
}
