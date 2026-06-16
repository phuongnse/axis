using axis.identity.events;
using Axis.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.WorkflowEngine.Infrastructure.Messaging;

/// <summary>
/// Consumes <see cref="TenantVerifiedEvent"/> from Kafka and provisions the
/// WorkflowEngine tenant schema for the verified Tenant (ADR-019, ADR-023).
/// </summary>
internal sealed class TenantVerifiedHandler(
    IConfiguration configuration,
    IMessageBus messageBus,
    ILogger<TenantVerifiedHandler> logger)
{
    public Task Handle(TenantVerifiedEvent evt, CancellationToken cancellationToken)
        => TenantModuleProvisionAttempt.RunAsync(
            evt.tenantId(),
            attempt: 1,
            configuration,
            messageBus,
            logger,
            cancellationToken);
}
