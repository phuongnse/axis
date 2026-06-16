using axis.identity.events;
using Axis.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.WorkflowBuilder.Infrastructure.Messaging;

/// <summary>
/// Consumes <see cref="WorkspaceVerifiedEvent"/> from Kafka and provisions the
/// WorkflowBuilder workspace schema for the verified Workspace (ADR-019, ADR-023).
/// </summary>
internal sealed class WorkspaceVerifiedHandler(
    IConfiguration configuration,
    IMessageBus messageBus,
    ILogger<WorkspaceVerifiedHandler> logger)
{
    public Task Handle(WorkspaceVerifiedEvent evt, CancellationToken cancellationToken)
        => WorkspaceModuleProvisionAttempt.RunAsync(
            evt.workspaceId(),
            attempt: 1,
            configuration,
            messageBus,
            logger,
            cancellationToken);
}
