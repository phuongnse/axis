using Axis.Shared.Application.Tenancy;
using FluentAssertions;

namespace Axis.Shared.Application.Tests;

public class TenantContextTests
{
    [Fact]
    public void TenantContext_WhenCreatedWithTenantId_DerivesSchemaNameFromTenantId()
    {
        Guid TenantId = Guid.NewGuid();
        TenantContext context = new TenantContext(TenantId);

        context.SchemaName.Should().Be($"tenant_{TenantId:N}");
    }

    [Fact]
    public void TenantContext_WhenCreatedWithTenantId_ExposestenantId()
    {
        Guid TenantId = Guid.NewGuid();
        TenantContext context = new TenantContext(TenantId);

        context.tenantId.Should().Be(TenantId);
    }
}
