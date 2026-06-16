using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class TeamAccountMembershipTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TeamAccountId = Guid.NewGuid();

    [Fact]
    public void TeamAccountMembership_WhenCreated_ProducesActiveMembership()
    {
        TeamAccountMembership membership = TeamAccountMembership.Create(UserId, TeamAccountId);

        membership.UserId.Should().Be(UserId);
        membership.TeamAccountId.Should().Be(TeamAccountId);
        membership.Status.Should().Be(TeamAccountMembershipStatus.Active);
        membership.RoleIds.Should().BeEmpty();
    }

    [Fact]
    public void AssignRole_WhenCalled_AddsRoleToMembership()
    {
        TeamAccountMembership membership = TeamAccountMembership.Create(UserId, TeamAccountId);
        Guid roleId = Guid.NewGuid();

        membership.AssignRole(roleId);

        membership.RoleIds.Should().Contain(roleId);
        membership.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<RoleAssigned>();
    }

    [Fact]
    public void AssignRole_WhenSameRoleAssignedTwice_IsIdempotent()
    {
        TeamAccountMembership membership = TeamAccountMembership.Create(UserId, TeamAccountId);
        Guid roleId = Guid.NewGuid();

        membership.AssignRole(roleId);
        membership.AssignRole(roleId);

        membership.RoleIds.Should().ContainSingle(id => id == roleId);
    }

    [Fact]
    public void RemoveRole_WhenCalled_RemovesRoleFromMembership()
    {
        TeamAccountMembership membership = TeamAccountMembership.Create(UserId, TeamAccountId);
        Guid roleId = Guid.NewGuid();
        membership.AssignRole(roleId);
        membership.ClearDomainEvents();

        membership.RemoveRole(roleId);

        membership.RoleIds.Should().NotContain(roleId);
        membership.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<RoleRemoved>();
    }
}
