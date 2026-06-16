using Axis.Shared.Application.Tenancy;

namespace Axis.Shared.Infrastructure.Persistence;

/// <summary>
/// Resolves the <c>public</c> schema for <c>dotnet ef migrations</c> design-time only.
/// Runtime tenant modules use <see cref="Tenancy.FixedTenantContext"/> or HTTP-scoped tenant context.
/// </summary>
public sealed class DesignTimePublicSchemaTenantContext : ITenantContext
{
    public Guid tenantId => Guid.Empty;

    public string SchemaName => "public";
}
