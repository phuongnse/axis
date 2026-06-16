using axis.identity.events;
using Axis.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.DataModeling.Infrastructure.Messaging;

/// <summary>
/// Consumes <see cref="OrganizationVerifiedEvent"/> from Kafka and provisions the
/// DataModeling tenant schema for the verified organization (ADR-019, ADR-023).
/// </summary>
internal sealed class OrganizationVerifiedHandler(
    IConfiguration configuration,
    IMessageBus messageBus,
    ILogger<OrganizationVerifiedHandler> logger)
{
    public Task Handle(OrganizationVerifiedEvent evt, CancellationToken cancellationToken)
        => TenantModuleProvisionAttempt.RunAsync(
            evt.OrganizationId(),
            attempt: 1,
            configuration,
            messageBus,
            logger,
            cancellationToken);
}
