namespace Axis.Shared.Application.Tenancy;

/// <summary>
/// Interface for accessing tenant context within the application layer.
/// Implemented by infrastructure and injected into handlers.
/// </summary>
public interface ITenantContext
{
    Guid TeamAccountId { get; }
    string SchemaName { get; }
}
