using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.Tenants;
using Axis.Shared.Infrastructure.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Axis.Identity.Infrastructure.Messaging;

// WORKAROUND: see docs/WORKAROUNDS.md#Tenant-hard-delete-modulith-cancellers
internal sealed class TenantHardDeleteHandler(
    ITenantRepository tenantRepo,
    ITenantExecutionCanceller executionCanceller,
    ITenantFormTaskCanceller formTaskCanceller,
    ItenantIdentityPurger identityPurger,
    IConfiguration configuration,
    ILogger<TenantHardDeleteHandler> logger)
{
    public async Task Handle(TenantHardDeleteJob job, CancellationToken cancellationToken)
    {
        Tenant? Tenant = await tenantRepo.GetByIdAsync(job.tenantId, cancellationToken);
        if (Tenant is null)
            return;

        if (Tenant.Status != TenantStatus.DeletionScheduled)
            return;

        if (Tenant.ScheduledHardDeleteAt is DateTime scheduled && scheduled > DateTime.UtcNow)
            return;

        string? logoUrl = Tenant.LogoUrl;

        await executionCanceller.CancelAllForTenantAsync(job.tenantId, cancellationToken);
        await formTaskCanceller.CancelPendingForTenantAsync(job.tenantId, cancellationToken);
        await DropModuleSchemasAsync(job.tenantId, cancellationToken);
        await identityPurger.PurgeAsync(job.tenantId, logoUrl, cancellationToken);
    }

    private async Task DropModuleSchemasAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        (string ConnectionString, string Module)[] modules =
        [
            (configuration.GetConnectionString("Identity")
                ?? throw new InvalidOperationException("ConnectionStrings:Identity is required"),
                "Identity"),
            (configuration.GetConnectionString("DataModeling")
                ?? throw new InvalidOperationException("ConnectionStrings:DataModeling is required"),
                "DataModeling"),
            (configuration.GetConnectionString("FormBuilder")
                ?? throw new InvalidOperationException("ConnectionStrings:FormBuilder is required"),
                "FormBuilder"),
            (configuration.GetConnectionString("WorkflowBuilder")
                ?? throw new InvalidOperationException("ConnectionStrings:WorkflowBuilder is required"),
                "WorkflowBuilder"),
            (configuration.GetConnectionString("WorkflowEngine")
                ?? throw new InvalidOperationException("ConnectionStrings:WorkflowEngine is required"),
                "WorkflowEngine"),
        ];

        foreach ((string connectionString, string module) in modules)
        {
            await TenantSchemaDropper.DropAsync(
                connectionString,
                tenantId,
                logger,
                module,
                cancellationToken);
        }
    }
}
