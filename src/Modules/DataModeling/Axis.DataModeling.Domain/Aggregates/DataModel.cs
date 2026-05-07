using System.Text.RegularExpressions;
using Axis.DataModeling.Domain.Entities;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.Events;
using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Aggregates;

public sealed class DataModel : AggregateRoot<Guid>
{
    private static readonly string[] ReservedFieldNames = ["id", "created_at", "updated_at"];

    // Name: 2–100 chars, letters/digits/spaces/hyphens only
    private static readonly Regex NameRegex = new(@"^[A-Za-z0-9 \-]{2,100}$", RegexOptions.Compiled);

    // Field name: 1–64 chars, alphanumeric+underscore, must start with a letter
    private static readonly Regex FieldNameRegex = new(@"^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);

    private readonly List<FieldDefinition> _fields = [];

    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string? Icon { get; private set; }
    public string? Color { get; private set; }
    public Guid OrganizationId { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyList<FieldDefinition> Fields => _fields.AsReadOnly();

    private DataModel(Guid id, string name, string? description, string? icon, string? color,
        Guid organizationId, DateTime createdAt)
        : base(id)
    {
        Name = name;
        Description = description;
        Icon = icon;
        Color = color;
        OrganizationId = organizationId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public static DataModel Create(string name, string? description, string? icon, string? color, Guid organizationId)
    {
        ValidateName(name);

        var now = DateTime.UtcNow;
        var model = new DataModel(Guid.NewGuid(), name.Trim(), description?.Trim(), icon, color, organizationId, now);

        // Auto-generate system fields (US-030)
        model._fields.Add(FieldDefinition.Create("id", "ID", FieldType.Text, true, 0, new TextFieldConfig(), isSystem: true));
        model._fields.Add(FieldDefinition.Create("created_at", "Created At", FieldType.Date, true, 1, new DateFieldConfig(IncludeTime: true), isSystem: true));
        model._fields.Add(FieldDefinition.Create("updated_at", "Updated At", FieldType.Date, true, 2, new DateFieldConfig(IncludeTime: true), isSystem: true));

        model.RaiseDomainEvent(new ModelCreated(model.Id, organizationId, model.Name));
        return model;
    }

    public void Update(string name, string? description, string? icon, string? color)
    {
        EnsureNotDeleted();
        ValidateName(name);

        Name = name.Trim();
        Description = description?.Trim();
        Icon = icon;
        Color = color;
        UpdatedAt = DateTime.UtcNow;
    }

    public FieldDefinition AddField(string name, string label, FieldType type, bool required, FieldConfig config)
    {
        EnsureNotDeleted();
        ValidateFieldName(name);
        ValidateFieldConfig(type, config);

        var order = _fields.Where(f => !f.IsSystem).Count();
        var field = FieldDefinition.Create(name.ToLowerInvariant(), label, type, required, order, config);
        _fields.Add(field);
        UpdatedAt = DateTime.UtcNow;
        return field;
    }

    public void RemoveField(Guid fieldId)
    {
        var field = _fields.SingleOrDefault(f => f.Id == fieldId)
            ?? throw new InvalidOperationException("Field not found.");

        if (field.IsSystem)
            throw new InvalidOperationException("Cannot remove a system field.");

        _fields.Remove(field);
        UpdatedAt = DateTime.UtcNow;
        RecalculateOrder();
    }

    public void ReorderFields(IReadOnlyList<Guid> orderedFieldIds)
    {
        var customFields = _fields.Where(f => !f.IsSystem).ToList();
        if (orderedFieldIds.Count != customFields.Count ||
            !orderedFieldIds.All(id => customFields.Any(f => f.Id == id)))
            throw new ArgumentException("The provided field IDs must match all custom fields exactly.");

        for (int i = 0; i < orderedFieldIds.Count; i++)
        {
            var field = customFields.Single(f => f.Id == orderedFieldIds[i]);
            field.DisplayOrder = i;
        }

        UpdatedAt = DateTime.UtcNow;
    }

    public void Delete()
    {
        if (IsDeleted)
            throw new InvalidOperationException("Model is already deleted.");

        IsDeleted = true;
        RaiseDomainEvent(new ModelDeleted(Id, OrganizationId));
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
            throw new InvalidOperationException("Cannot modify a deleted model.");
    }

    private void ValidateFieldName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !FieldNameRegex.IsMatch(name))
            throw new ArgumentException(
                $"Field name '{name}' is invalid. Must be 1–64 characters, start with a letter, and contain only alphanumeric characters and underscores.");

        if (ReservedFieldNames.Contains(name.ToLowerInvariant()))
            throw new InvalidOperationException($"'{name}' is a reserved field name and cannot be used.");

        if (_fields.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A field named '{name}' already exists in this model.");
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !NameRegex.IsMatch(name.Trim()))
            throw new ArgumentException(
                "Model name must be 2–100 characters and contain only letters, numbers, spaces, and hyphens.");
    }

    private static void ValidateFieldConfig(FieldType type, FieldConfig config)
    {
        if (type == FieldType.Enum && config is EnumFieldConfig enumConfig)
        {
            if (enumConfig.Options.Count < 2)
                throw new ArgumentException("Enum field requires at least 2 options.");
        }
    }

    private void RecalculateOrder()
    {
        var customFields = _fields.Where(f => !f.IsSystem).OrderBy(f => f.DisplayOrder).ToList();
        for (int i = 0; i < customFields.Count; i++)
            customFields[i].DisplayOrder = i;
    }
}
