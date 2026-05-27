using Axis.Shared.Application.Tenancy;

namespace Axis.Testing;

/// <summary>Runs module migrations against the module public schema (no tenant).</summary>
public sealed class PublicSchemaTenantContext : ITenantContext
{
    public Guid OrganizationId => Guid.Empty;
    public string SchemaName => "public";
}
