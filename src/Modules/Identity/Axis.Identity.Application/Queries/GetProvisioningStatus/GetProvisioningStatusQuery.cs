using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetProvisioningStatus;

public sealed record GetProvisioningStatusQuery(string Token) : IQuery<ProvisioningStatusDto?>;
