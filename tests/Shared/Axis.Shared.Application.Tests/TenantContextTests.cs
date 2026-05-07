using Axis.Shared.Application.Tenancy;
using FluentAssertions;

namespace Axis.Shared.Application.Tests;

public class TenantContextTests
{
    [Fact]
    public void SchemaName_is_derived_from_org_slug()
    {
        var context = new TenantContext(Guid.NewGuid(), "acme-corp");

        context.SchemaName.Should().Be("tenant_acme-corp");
    }

    [Fact]
    public void TenantContext_exposes_org_id_and_slug()
    {
        var orgId = Guid.NewGuid();
        var context = new TenantContext(orgId, "my-org");

        context.OrganizationId.Should().Be(orgId);
        context.OrganizationSlug.Should().Be("my-org");
    }
}
