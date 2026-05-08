using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Entities;

/// <summary>Defines a single field within a DataModel or DataClass. Part of the DataModel aggregate.</summary>
public sealed class FieldDefinition : Entity<Guid>
{
    public string Name { get; private set; }
    public string Label { get; private set; }
    public string? HelpText { get; private set; }
    public FieldType Type { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsSystem { get; private set; }
    public int DisplayOrder { get; internal set; }
    public FieldConfig Config { get; private set; }

    private FieldDefinition(
        Guid id,
        string name,
        string label,
        string? helpText,
        FieldType type,
        bool isRequired,
        bool isSystem,
        int displayOrder,
        FieldConfig config)
        : base(id)
    {
        Name = name;
        Label = label;
        HelpText = helpText;
        Type = type;
        IsRequired = isRequired;
        IsSystem = isSystem;
        DisplayOrder = displayOrder;
        Config = config;
    }

    internal static FieldDefinition Create(
        string name,
        string label,
        FieldType type,
        bool isRequired,
        int displayOrder,
        FieldConfig config,
        bool isSystem = false)
        => new(Guid.NewGuid(), name, label, null, type, isRequired, isSystem, displayOrder, config);

    internal static FieldDefinition Reconstitute(
        Guid id, string name, string label, string? helpText,
        FieldType type, bool isRequired, bool isSystem, int displayOrder, FieldConfig config)
        => new(id, name, label, helpText, type, isRequired, isSystem, displayOrder, config);

    public void Update(string label, string? helpText, bool isRequired, FieldConfig config)
    {
        Label = label;
        HelpText = helpText;
        IsRequired = isRequired;
        Config = config;
    }
}
