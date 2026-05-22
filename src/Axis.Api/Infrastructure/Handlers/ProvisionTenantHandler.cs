using Axis.Shared.Application.Tenancy;

namespace Axis.Api.Infrastructure.Handlers;

/// <summary>US-003: provisions tenant schema asynchronously after email verification is saved.</summary>
public sealed class ProvisionTenantHandler(
    ITenantSchemaProvisioner provisioner,
    ILogger<ProvisionTenantHandler> logger)
{
    public async Task Handle(ProvisionTenantMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Processing ProvisionTenantMessage for organization {OrganizationId}",
            message.OrganizationId);

        await provisioner.ProvisionAsync(message.OrganizationId, cancellationToken);

        logger.LogInformation(
            "Tenant schema provisioned for organization {OrganizationId}",
            message.OrganizationId);
    }
}
