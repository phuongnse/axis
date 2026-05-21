using Axis.Shared.Application.Tenancy;

namespace Axis.Api.Tests.Helpers;

/// <summary>
/// Integration tests use a shared public schema — tenant provisioning is a no-op.
/// </summary>
internal sealed class NoOpTenantSchemaProvisioner : ITenantSchemaProvisioner
{
    public Task ProvisionAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
