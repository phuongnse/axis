using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetOrganizationSettings;

public sealed record GetOrganizationSettingsQuery(Guid OrganizationId) : IQuery<OrganizationSettingsDto?>;
