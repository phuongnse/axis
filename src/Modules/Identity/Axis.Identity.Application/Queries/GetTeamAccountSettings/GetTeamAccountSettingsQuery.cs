using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetTeamAccountSettings;

public sealed record GetTeamAccountSettingsQuery(Guid TeamAccountId) : IQuery<TeamAccountSettingsDto?>;
