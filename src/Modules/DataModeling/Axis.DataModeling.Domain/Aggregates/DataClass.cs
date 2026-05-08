using Axis.DataModeling.Domain.Entities;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.Events;
using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Aggregates;

/// <summary>
/// Reusable nested object type composed of multiple fields.
/// DataClass fields cannot use Relation, DataClass, or File types (US-037).
/// </summary>
public sealed class DataClass : AggregateRoot<Guid>
{
    private static readonly FieldType[] DisallowedTypes = [FieldType.Relation, FieldType.DataClass, FieldType.File];

    private readonly List<FieldDefinition> _fields = [];

    public string Name { get; private set; }
    public string? Description { get; private set; }
    public Guid OrganizationId { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public IReadOnlyList<FieldDefinition> Fields => _fields.AsReadOnly();

    private DataClass(Guid id, string name, string? description, Guid organizationId, DateTime createdAt)
        : base(id)
    {
        Name = name;
        Description = description;
        OrganizationId = organizationId;
        CreatedAt = createdAt;
    }

    public static DataClass Create(string name, string? description, Guid organizationId)
    {
        ValidateName(name);

        var now = DateTime.UtcNow;
        var dc = new DataClass(Guid.NewGuid(), name.Trim(), description?.Trim(), organizationId, now);
        dc.RaiseDomainEvent(new DataClassCreated(dc.Id, organizationId, dc.Name));
        return dc;
    }

    public void Update(string name, string? description)
    {
        if (IsDeleted)
            throw new InvalidOperationException("Cannot modify a deleted data class.");
        ValidateName(name);
        Name = name.Trim();
        Description = description?.Trim();
    }

    public void UpdateField(Guid fieldId, string label, string? helpText, bool isRequired, FieldConfig config)
    {
        if (IsDeleted)
            throw new InvalidOperationException("Cannot modify a deleted data class.");
        var field = _fields.SingleOrDefault(f => f.Id == fieldId)
            ?? throw new InvalidOperationException("Field not found.");
        field.Update(label, helpText, isRequired, config);
    }

    public FieldDefinition AddField(string name, string label, FieldType type, bool required, FieldConfig config)
    {
        if (IsDeleted)
            throw new InvalidOperationException("Cannot modify a deleted data class.");

        if (DisallowedTypes.Contains(type))
            throw new InvalidOperationException(
                $"Field type '{type}' is not allowed in a data class. Relation, DataClass, and File types are excluded.");

        if (_fields.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A field named '{name}' already exists in this data class.");

        var order = _fields.Count;
        var field = FieldDefinition.Create(name.ToLowerInvariant(), label, type, required, order, config);
        _fields.Add(field);
        return field;
    }

    public void RemoveField(Guid fieldId)
    {
        var field = _fields.SingleOrDefault(f => f.Id == fieldId)
            ?? throw new InvalidOperationException("Field not found.");
        _fields.Remove(field);
    }

    public void Delete()
    {
        if (IsDeleted)
            throw new InvalidOperationException("Data class is already deleted.");

        IsDeleted = true;
        RaiseDomainEvent(new DataClassDeleted(Id, OrganizationId));
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2 || name.Trim().Length > 100)
            throw new ArgumentException("Data class name must be 2–100 characters.");
    }
}
