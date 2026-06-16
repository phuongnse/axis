using axis.identity.events;
using Axis.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.FormBuilder.Infrastructure.Messaging;

/// <summary>
/// Consumes <see cref="TeamAccountVerifiedEvent"/> from Kafka and provisions the
/// FormBuilder tenant schema for the verified team account (ADR-019, ADR-023).
/// </summary>
internal sealed class TeamAccountVerifiedHandler(
    IConfiguration configuration,
    IMessageBus messageBus,
    ILogger<TeamAccountVerifiedHandler> logger)
{
    public Task Handle(TeamAccountVerifiedEvent evt, CancellationToken cancellationToken)
        => TenantModuleProvisionAttempt.RunAsync(
            evt.TeamAccountId(),
            attempt: 1,
            configuration,
            messageBus,
            logger,
            cancellationToken);
}
