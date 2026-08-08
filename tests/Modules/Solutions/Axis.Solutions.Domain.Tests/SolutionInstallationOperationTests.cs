using Axis.Solutions.Domain;

namespace Axis.Solutions.Domain.Tests;

public sealed class SolutionInstallationOperationTests
{
    [Fact]
    public void Operation_ExpiredLease_ReclaimsAndFences()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-07T00:00:00Z");
        SolutionInstallationOperation operation = SolutionInstallationOperation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            SolutionSubjectKind.Human,
            "test-correlation",
            Guid.NewGuid(),
            "install-1",
            new string('a', 64),
            [new SolutionComponentPlan("authorization.policy.v1", "reference", new string('b', 64), [])],
            now);

        long firstEpoch = operation.AcquireLease(now, TimeSpan.FromMinutes(1));
        SolutionInstallationStep step = operation.ClaimNext(firstEpoch, now.AddSeconds(1));
        long secondEpoch = operation.AcquireLease(now.AddMinutes(2), TimeSpan.FromMinutes(1));

        Assert.Equal(InstallationStepStatus.Pending, step.Status);
        Assert.Equal(firstEpoch, step.ReclaimedEpoch);
        Assert.Throws<InvalidOperationException>(() => operation.Confirm(step.Id, firstEpoch, now.AddMinutes(2)));

        SolutionInstallationStep reclaimed = operation.ClaimNext(secondEpoch, now.AddMinutes(2));
        operation.Confirm(reclaimed.Id, secondEpoch, now.AddMinutes(2).AddSeconds(1));
        Assert.Equal(InstallationOperationStatus.Succeeded, operation.Status);
    }

    [Fact]
    public void Operation_WhenOneOfMultipleStepsConfirms_ReleasesLeaseForNextStep()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-07T00:00:00Z");
        SolutionInstallationOperation operation = SolutionInstallationOperation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            SolutionSubjectKind.Human,
            "test-correlation",
            Guid.NewGuid(),
            "install-multiple",
            new string('a', 64),
            [
                new SolutionComponentPlan("authorization.policy.v1", "policy", new string('b', 64), []),
                new SolutionComponentPlan("rule.binding.v1", "binding", new string('c', 64), []),
            ],
            now);

        long firstEpoch = operation.AcquireLease(now, TimeSpan.FromMinutes(1));
        SolutionInstallationStep first = operation.ClaimNext(firstEpoch, now.AddSeconds(1));
        operation.Confirm(first.Id, firstEpoch, now.AddSeconds(2));

        Assert.Equal(InstallationOperationStatus.Pending, operation.Status);
        Assert.Null(operation.LeaseExpiresAt);
        long secondEpoch = operation.AcquireLease(now.AddSeconds(2), TimeSpan.FromMinutes(1));
        Assert.Equal(firstEpoch + 1, secondEpoch);
        Assert.Equal("binding", operation.ClaimNext(secondEpoch, now.AddSeconds(3)).Key);
    }

    [Fact]
    public void Operation_WhenTrustFailsBeforeNextMutation_BlocksOnlyUnconfirmedWork()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-07T00:00:00Z");
        SolutionInstallationOperation operation = SolutionInstallationOperation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            SolutionSubjectKind.Human,
            "test-correlation",
            Guid.NewGuid(),
            "install-revoked",
            new string('a', 64),
            [
                new SolutionComponentPlan("authorization.policy.v1", "policy", new string('b', 64), []),
                new SolutionComponentPlan("rule.binding.v1", "binding", new string('c', 64), []),
            ],
            now);
        long epoch = operation.AcquireLease(now, TimeSpan.FromMinutes(1));
        SolutionInstallationStep first = operation.ClaimNext(epoch, now.AddSeconds(1));
        operation.Confirm(first.Id, epoch, now.AddSeconds(2));

        operation.BlockBeforeNextMutation("solutions.package.publisher_untrusted", now.AddSeconds(3));
        int revision = operation.Revision;
        operation.BlockBeforeNextMutation("solutions.package.publisher_untrusted", now.AddSeconds(4));

        Assert.Equal(InstallationOperationStatus.Blocked, operation.Status);
        Assert.Equal("solutions.package.publisher_untrusted", operation.ProblemCode);
        Assert.Equal(revision, operation.Revision);
        Assert.Equal(
            [InstallationStepStatus.Confirmed, InstallationStepStatus.Failed],
            operation.Steps.Select(value => value.Status));
    }

    [Fact]
    public void Installation_RevokedPublisher_BecomesNoncompliant()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-07T00:00:00Z");
        SolutionInstallation installation = SolutionInstallation.Create(Guid.NewGuid(), Guid.NewGuid(), now);
        installation.MarkInstalled(now.AddSeconds(1));
        installation.MarkNoncompliant(now.AddSeconds(2));
        int revision = installation.Revision;
        installation.MarkNoncompliant(now.AddSeconds(3));

        Assert.Equal(ProvisioningStatus.Installed, installation.ProvisioningStatus);
        Assert.Equal(ComplianceStatus.Noncompliant, installation.ComplianceStatus);
        Assert.Equal(revision, installation.Revision);
    }
}
