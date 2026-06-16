using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetTenantSettings;

public sealed record GetTenantSettingsQuery(Guid tenantId) : IQuery<TenantSettingsDto?>;
