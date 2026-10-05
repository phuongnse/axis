namespace Axis.Data.Schema;

/// <summary>What earlier releases provisioned: the entity tables and the enum values of their fields.</summary>
public sealed record ProvisioningRecords(IReadOnlyList<ProvisionedEntity> Entities, IReadOnlyList<ProvisionedEnumValue> EnumValues)
{
    public static ProvisioningRecords Empty { get; } = new([], []);
}

/// <summary>An entity table provisioned for an application.</summary>
public sealed record ProvisionedEntity(Guid EntityId, Guid ApplicationId, string TableName);

/// <summary>A value of an enum field, recorded when the field's column was provisioned or extended.</summary>
public sealed record ProvisionedEnumValue(Guid EntityId, string FieldName, string Value);
