using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class OrganizationMembershipTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public void OrganizationMembership_WhenCreated_ProducesActiveMembership()
    {
        OrganizationMembership membership = OrganizationMembership.Create(UserId, OrgId);

        membership.UserId.Should().Be(UserId);
        membership.OrganizationId.Should().Be(OrgId);
        membership.Status.Should().Be(OrganizationMembershipStatus.Active);
        membership.RoleIds.Should().BeEmpty();
    }

    [Fact]
    public void AssignRole_WhenCalled_AddsRoleToMembership()
    {
        OrganizationMembership membership = OrganizationMembership.Create(UserId, OrgId);
        Guid roleId = Guid.NewGuid();

        membership.AssignRole(roleId);

        membership.RoleIds.Should().Contain(roleId);
        membership.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<RoleAssigned>();
    }

    [Fact]
    public void AssignRole_WhenSameRoleAssignedTwice_IsIdempotent()
    {
        OrganizationMembership membership = OrganizationMembership.Create(UserId, OrgId);
        Guid roleId = Guid.NewGuid();

        membership.AssignRole(roleId);
        membership.AssignRole(roleId);

        membership.RoleIds.Should().ContainSingle(id => id == roleId);
    }

    [Fact]
    public void RemoveRole_WhenCalled_RemovesRoleFromMembership()
    {
        OrganizationMembership membership = OrganizationMembership.Create(UserId, OrgId);
        Guid roleId = Guid.NewGuid();
        membership.AssignRole(roleId);
        membership.ClearDomainEvents();

        membership.RemoveRole(roleId);

        membership.RoleIds.Should().NotContain(roleId);
        membership.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<RoleRemoved>();
    }
}
