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
    IUserRepository userRepo,
    IOrganizationExecutionCanceller executionCanceller,
    IUnitOfWork uow,
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

        await executionCanceller.CancelAllForOrganizationAsync(job.OrganizationId, cancellationToken);

        await DropModuleSchemasAsync(job.OrganizationId, cancellationToken);

        IReadOnlyList<Domain.Aggregates.User> users =
            await userRepo.GetAllByOrganizationAsync(job.OrganizationId, cancellationToken);
        foreach (Domain.Aggregates.User user in users)
        {
            if (user.Status == Domain.Aggregates.UserStatus.Active)
                user.Deactivate();
        }

        organization.MarkDeleted();
        await uow.SaveChangesAsync(cancellationToken);
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
