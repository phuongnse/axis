using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.Organizations;
using Axis.Shared.Infrastructure.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Axis.Identity.Infrastructure.Messaging;

internal sealed class OrganizationHardDeleteHandler(
    IOrganizationRepository orgRepo,
    IOrganizationExecutionCanceller executionCanceller,
    IOrganizationFormTaskCanceller formTaskCanceller,
    IOrganizationIdentityPurger identityPurger,
    IConfiguration configuration,
    ILogger<OrganizationHardDeleteHandler> logger)
{
    public async Task Handle(OrganizationHardDeleteJob job, CancellationToken cancellationToken)
    {
        Organization? organization = await orgRepo.GetByIdAsync(job.OrganizationId, cancellationToken);
        if (organization is null)
            return;

        if (organization.Status != OrganizationStatus.DeletionScheduled)
            return;

        if (organization.ScheduledHardDeleteAt is DateTime scheduled && scheduled > DateTime.UtcNow)
            return;

        string? logoUrl = organization.LogoUrl;

        await executionCanceller.CancelAllForOrganizationAsync(job.OrganizationId, cancellationToken);
        await formTaskCanceller.CancelPendingForOrganizationAsync(job.OrganizationId, cancellationToken);
        await DropModuleSchemasAsync(job.OrganizationId, cancellationToken);
        await identityPurger.PurgeAsync(job.OrganizationId, logoUrl, cancellationToken);
    }

    private async Task DropModuleSchemasAsync(Guid organizationId, CancellationToken cancellationToken)
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
                organizationId,
                logger,
                module,
                cancellationToken);
        }
    }
}
