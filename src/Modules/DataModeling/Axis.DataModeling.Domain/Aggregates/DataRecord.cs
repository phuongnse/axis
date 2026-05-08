using Axis.DataModeling.Domain.Events;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Aggregates;

/// <summary>A single record of data stored against a DataModel. Data is stored as a key-value map (JSONB).</summary>
public sealed class DataRecord : AggregateRoot<Guid>
{
    private Dictionary<string, object?> _data;

    public Guid ModelId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyDictionary<string, object?> Data => _data;

    private DataRecord() : base(default) { _data = []; } // EF Core materialisation

    private DataRecord(Guid id, Guid modelId, Guid organizationId,
        Dictionary<string, object?> data, DateTime createdAt)
        : base(id)
    {
        ModelId = modelId;
        OrganizationId = organizationId;
        _data = data;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public static DataRecord Create(Guid modelId, Guid organizationId, IReadOnlyDictionary<string, object?> data)
    {
        var record = new DataRecord(Guid.NewGuid(), modelId, organizationId,
            new Dictionary<string, object?>(data), DateTime.UtcNow);

        record.RaiseDomainEvent(new DataRecordCreated(record.Id, modelId, organizationId));
        return record;
    }

    /// <summary>Replaces the record data and bumps UpdatedAt.</summary>
    public void Update(IReadOnlyDictionary<string, object?> data)
    {
        if (IsDeleted)
            throw new InvalidOperationException("Cannot update a deleted record.");

        _data = new Dictionary<string, object?>(data);
        UpdatedAt = DateTime.UtcNow;
    }

    public void Delete()
    {
        if (IsDeleted)
            throw new InvalidOperationException("Record is already deleted.");

        IsDeleted = true;
        RaiseDomainEvent(new DataRecordDeleted(Id, ModelId, OrganizationId));
    }
}
