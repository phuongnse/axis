using Axis.Shared.Application.Tenancy;
using FluentAssertions;

namespace Axis.Shared.Application.Tests;

public class TenantContextTests
{
    [Fact]
    public void TenantContext_WhenCreatedWithOrgId_DerivesSchemaNameFromOrgId()
    {
        var orgId = Guid.NewGuid();
        var context = new TenantContext(orgId);

        context.SchemaName.Should().Be($"tenant_{orgId:N}");
    }

    [Fact]
    public void TenantContext_WhenCreatedWithOrgId_ExposesOrganizationId()
    {
        var orgId = Guid.NewGuid();
        var context = new TenantContext(orgId);

        context.OrganizationId.Should().Be(orgId);
    }
}
