using Axis.Identity.Domain.Aggregates;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public sealed class WorkspaceContextTransitionRetentionTests
{
    [Fact]
    public void CanPurge_WhenRequirementsAreSatisfied_ReturnsTrue()
    {
        DateTime now = DateTime.UtcNow;
        WorkspaceContextTransition transition = WorkspaceContextTransition.Begin(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "source", "target",
            now, now.AddMinutes(5), now.AddHours(1));

        transition.Compensate(transition.Revision, now.AddMinutes(1));
        transition.MarkAuditProjectionConfirmed(transition.Revision, now.AddMinutes(2));
        transition.CanPurge(now.AddHours(2)).Should().BeFalse();
        transition.MarkRedisCleanupCompleted(transition.Revision, now.AddMinutes(3));

        transition.CanPurge(now.AddMinutes(30)).Should().BeFalse();
        transition.CanPurge(now.AddHours(2)).Should().BeTrue();
    }
}
