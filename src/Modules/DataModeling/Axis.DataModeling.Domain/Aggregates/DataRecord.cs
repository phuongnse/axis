using Axis.DataModeling.Domain.Events;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Aggregates;

/// <summary>A single record of data stored against a DataModel. Data is stored as a key-value map (JSONB).</summary>
public sealed class DataRecord : AggregateRoot<Guid>
{
    private Dictionary<string, object?> _data;

    public Guid ModelId { get; private set; }
    public Guid workspaceId { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string CreatedBy { get; private set; }

    public IReadOnlyDictionary<string, object?> Data => _data;

    private DataRecord() : base(default) { _data = []; CreatedBy = string.Empty; } // EF Core materialisation

    private DataRecord(Guid id, Guid modelId, Guid workspaceId,
        Dictionary<string, object?> data, string createdBy, DateTimeOffset createdAt)
        : base(id)
    {
        ModelId = modelId;
        this.workspaceId = workspaceId;
        _data = data;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public static DataRecord Create(Guid modelId, Guid workspaceId,
        IReadOnlyDictionary<string, object?> data, string createdBy)
    {
        DataRecord record = new(Guid.NewGuid(), modelId, workspaceId,
            new Dictionary<string, object?>(data), createdBy, DateTimeOffset.UtcNow);

        record.RaiseDomainEvent(new DataRecordCreated(record.Id, modelId, workspaceId));
        return record;
    }

    /// <summary>Replaces the record data and bumps UpdatedAt.</summary>
    public void Update(IReadOnlyDictionary<string, object?> data)
    {
        if (DeletedAt.HasValue)
            throw new InvalidOperationException("Cannot update a deleted record.");

        _data = new Dictionary<string, object?>(data);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Delete()
    {
        if (DeletedAt.HasValue)
            throw new InvalidOperationException("Record is already deleted.");

        DeletedAt = DateTimeOffset.UtcNow;
        RaiseDomainEvent(new DataRecordDeleted(Id, ModelId, workspaceId));
    }
}
