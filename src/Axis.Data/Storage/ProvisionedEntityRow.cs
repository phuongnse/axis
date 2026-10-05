namespace Axis.Data.Storage;

/// <summary>A row of <c>axis.provisioned_entities</c>: an entity table provisioned for an application.</summary>
public sealed class ProvisionedEntityRow
{
    public required Guid EntityId { get; init; }

    /// <summary>The <c>id</c> of the application manifest the entity was provisioned for.</summary>
    public required Guid ApplicationId { get; init; }

    /// <summary>The physical table name in the <c>entities</c> schema.</summary>
    public required string TableName { get; init; }
}
