using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class WorkspaceMembershipTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid WorkspaceId = Guid.NewGuid();

    [Fact]
    public void WorkspaceMembership_WhenCreated_ProducesActiveMembership()
    {
        WorkspaceMembership membership = WorkspaceMembership.Create(UserId, WorkspaceId);

        membership.UserId.Should().Be(UserId);
        membership.workspaceId.Should().Be(WorkspaceId);
        membership.Status.Should().Be(WorkspaceMembershipStatus.Active);
        membership.RoleIds.Should().BeEmpty();
    }

    [Fact]
    public void AssignRole_WhenCalled_AddsRoleToMembership()
    {
        WorkspaceMembership membership = WorkspaceMembership.Create(UserId, WorkspaceId);
        Guid roleId = Guid.NewGuid();

        membership.AssignRole(roleId);

        membership.RoleIds.Should().Contain(roleId);
        membership.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<RoleAssigned>();
    }

    [Fact]
    public void AssignRole_WhenSameRoleAssignedTwice_IsIdempotent()
    {
        WorkspaceMembership membership = WorkspaceMembership.Create(UserId, WorkspaceId);
        Guid roleId = Guid.NewGuid();

        membership.AssignRole(roleId);
        membership.AssignRole(roleId);

        membership.RoleIds.Should().ContainSingle(id => id == roleId);
    }

    [Fact]
    public void RemoveRole_WhenCalled_RemovesRoleFromMembership()
    {
        WorkspaceMembership membership = WorkspaceMembership.Create(UserId, WorkspaceId);
        Guid roleId = Guid.NewGuid();
        membership.AssignRole(roleId);
        membership.ClearDomainEvents();

        membership.RemoveRole(roleId);

        membership.RoleIds.Should().NotContain(roleId);
        membership.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<RoleRemoved>();
    }
}
