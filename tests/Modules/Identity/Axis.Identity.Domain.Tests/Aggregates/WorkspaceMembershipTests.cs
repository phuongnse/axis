using Axis.Identity.Domain.Aggregates;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class WorkspaceMembershipTests
{
    [Fact]
    public void WorkspaceMembership_WhenCreated_ProducesActiveMembership()
    {
        Guid userId = Guid.NewGuid();
        Guid workspaceId = Guid.NewGuid();

        WorkspaceMembership membership = WorkspaceMembership.Create(userId, workspaceId);

        membership.UserId.Should().Be(userId);
        membership.workspaceId.Should().Be(workspaceId);
        membership.Status.Should().Be(WorkspaceMembershipStatus.Active);
    }
}
