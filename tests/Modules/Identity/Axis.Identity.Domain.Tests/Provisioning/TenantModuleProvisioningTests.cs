using Axis.Identity.Domain.Provisioning;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Provisioning;

public sealed class TenantModuleProvisioningTests
{
    [Fact]
    public void CreatePending_WhenModuleEmpty_Throws()
    {
        Action act = () => TenantModuleProvisioning.CreatePending(Guid.NewGuid(), "  ");

        act.Should().Throw<ArgumentException>().WithParameterName("module");
    }

    [Fact]
    public void RecordFailure_WhenErrorEmpty_Throws()
    {
        TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(Guid.NewGuid(), "datamodeling");

        Action act = () => row.RecordFailure(" ", attemptCount: 1);

        act.Should().Throw<ArgumentException>().WithParameterName("error");
    }

    [Fact]
    public void RecordFailure_WhenAttemptCountZero_Throws()
    {
        TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(Guid.NewGuid(), "datamodeling");

        Action act = () => row.RecordFailure("timeout", attemptCount: 0);

        act.Should().Throw<ArgumentException>().WithParameterName("attemptCount");
    }
}
