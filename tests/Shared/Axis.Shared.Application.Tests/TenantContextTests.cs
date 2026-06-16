using Axis.Shared.Application.Tenancy;
using FluentAssertions;

namespace Axis.Shared.Application.Tests;

public class TenantContextTests
{
    [Fact]
    public void TenantContext_WhenCreatedWithTeamAccountId_DerivesSchemaNameFromTeamAccountId()
    {
        Guid teamAccountId = Guid.NewGuid();
        TenantContext context = new TenantContext(teamAccountId);

        context.SchemaName.Should().Be($"tenant_{teamAccountId:N}");
    }

    [Fact]
    public void TenantContext_WhenCreatedWithTeamAccountId_ExposesTeamAccountId()
    {
        Guid teamAccountId = Guid.NewGuid();
        TenantContext context = new TenantContext(teamAccountId);

        context.TeamAccountId.Should().Be(teamAccountId);
    }
}
