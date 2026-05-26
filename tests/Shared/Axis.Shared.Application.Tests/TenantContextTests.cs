using Axis.Shared.Application.Tenancy;
using FluentAssertions;

namespace Axis.Shared.Application.Tests;

public class TenantContextTests
{
    [Fact]
    public void TenantContext_WhenCreatedWithOrgId_DerivesSchemaNameFromOrgId()
    {
        Guid orgId = Guid.NewGuid();
        TenantContext context = new TenantContext(orgId);

        context.SchemaName.Should().Be($"tenant_{orgId:N}");
    }

    [Fact]
    public void TenantContext_WhenCreatedWithOrgId_ExposesOrganizationId()
    {
        Guid orgId = Guid.NewGuid();
        TenantContext context = new TenantContext(orgId);

        context.OrganizationId.Should().Be(orgId);
    }
}
