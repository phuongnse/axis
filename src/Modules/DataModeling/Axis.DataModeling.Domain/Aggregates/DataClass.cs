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
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string CreatedBy { get; private set; }

    public IReadOnlyList<FieldDefinition> Fields => _fields.AsReadOnly();

    private DataClass(Guid id, string name, string? description, Guid organizationId,
        string createdBy, DateTimeOffset createdAt)
        : base(id)
    {
        Name = name;
        Description = description;
        OrganizationId = organizationId;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public static DataClass Create(string name, string? description, Guid organizationId, string createdBy)
    {
        ValidateName(name);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DataClass dc = new(Guid.NewGuid(), name.Trim(), description?.Trim(), organizationId, createdBy, now);
        dc.RaiseDomainEvent(new DataClassCreated(dc.Id, organizationId, dc.Name));
        return dc;
    }

    public void Update(string name, string? description)
    {
        if (DeletedAt.HasValue)
            throw new InvalidOperationException("Cannot modify a deleted data class.");
        ValidateName(name);
        Name = name.Trim();
        Description = description?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateField(Guid fieldId, string label, string? helpText, bool isRequired, FieldConfig config)
    {
        if (DeletedAt.HasValue)
            throw new InvalidOperationException("Cannot modify a deleted data class.");
        FieldDefinition field = _fields.SingleOrDefault(f => f.Id == fieldId)
            ?? throw new InvalidOperationException("Field not found.");
        field.Update(label, helpText, isRequired, config);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public FieldDefinition AddField(string name, string label, FieldType type, bool required, FieldConfig config)
    {
        if (DeletedAt.HasValue)
            throw new InvalidOperationException("Cannot modify a deleted data class.");

        if (DisallowedTypes.Contains(type))
            throw new InvalidOperationException(
                $"Field type '{type}' is not allowed in a data class. Relation, DataClass, and File types are excluded.");

        if (_fields.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A field named '{name}' already exists in this data class.");

        int order = _fields.Count;
        FieldDefinition field = FieldDefinition.Create(name.ToLowerInvariant(), label, type, required, order, config);
        _fields.Add(field);
        UpdatedAt = DateTimeOffset.UtcNow;
        return field;
    }

    public void RemoveField(Guid fieldId)
    {
        FieldDefinition field = _fields.SingleOrDefault(f => f.Id == fieldId)
            ?? throw new InvalidOperationException("Field not found.");
        _fields.Remove(field);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Delete()
    {
        if (DeletedAt.HasValue)
            throw new InvalidOperationException("Data class is already deleted.");

        DeletedAt = DateTimeOffset.UtcNow;
        RaiseDomainEvent(new DataClassDeleted(Id, OrganizationId));
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2 || name.Trim().Length > 100)
            throw new ArgumentException("Data class name must be 2–100 characters.");
    }
}
