using Axis.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.WorkflowEngine.Infrastructure.Messaging;

internal sealed class RetryTenantModuleProvisionHandler(
    IConfiguration configuration,
    IMessageBus messageBus,
    ILogger<RetryTenantModuleProvisionHandler> logger)
{
    public Task Handle(RetryTenantModuleProvisionMessage message, CancellationToken cancellationToken)
    {
        if (!string.Equals(message.Module, TenantModuleNames.WorkflowEngine, StringComparison.Ordinal))
            return Task.CompletedTask;

        return TenantModuleProvisionAttempt.RunAsync(
            message.TeamAccountId,
            message.Attempt,
            configuration,
            messageBus,
            logger,
            cancellationToken);
    }
}
