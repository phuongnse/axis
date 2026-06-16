using Axis.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.WorkflowEngine.Infrastructure.Messaging;

internal sealed class RetryWorkspaceModuleProvisionHandler(
    IConfiguration configuration,
    IMessageBus messageBus,
    ILogger<RetryWorkspaceModuleProvisionHandler> logger)
{
    public Task Handle(RetryWorkspaceModuleProvisionMessage message, CancellationToken cancellationToken)
    {
        if (!string.Equals(message.Module, WorkspaceModuleNames.WorkflowEngine, StringComparison.Ordinal))
            return Task.CompletedTask;

        return WorkspaceModuleProvisionAttempt.RunAsync(
            message.workspaceId,
            message.Attempt,
            configuration,
            messageBus,
            logger,
            cancellationToken);
    }
}
