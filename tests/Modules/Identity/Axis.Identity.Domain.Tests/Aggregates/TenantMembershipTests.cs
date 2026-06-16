using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class TenantMembershipTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void TenantMembership_WhenCreated_ProducesActiveMembership()
    {
        TenantMembership membership = TenantMembership.Create(UserId, TenantId);

        membership.UserId.Should().Be(UserId);
        membership.tenantId.Should().Be(TenantId);
        membership.Status.Should().Be(TenantMembershipStatus.Active);
        membership.RoleIds.Should().BeEmpty();
    }

    [Fact]
    public void AssignRole_WhenCalled_AddsRoleToMembership()
    {
        TenantMembership membership = TenantMembership.Create(UserId, TenantId);
        Guid roleId = Guid.NewGuid();

        membership.AssignRole(roleId);

        membership.RoleIds.Should().Contain(roleId);
        membership.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<RoleAssigned>();
    }

    [Fact]
    public void AssignRole_WhenSameRoleAssignedTwice_IsIdempotent()
    {
        TenantMembership membership = TenantMembership.Create(UserId, TenantId);
        Guid roleId = Guid.NewGuid();

        membership.AssignRole(roleId);
        membership.AssignRole(roleId);

        membership.RoleIds.Should().ContainSingle(id => id == roleId);
    }

    [Fact]
    public void RemoveRole_WhenCalled_RemovesRoleFromMembership()
    {
        TenantMembership membership = TenantMembership.Create(UserId, TenantId);
        Guid roleId = Guid.NewGuid();
        membership.AssignRole(roleId);
        membership.ClearDomainEvents();

        membership.RemoveRole(roleId);

        membership.RoleIds.Should().NotContain(roleId);
        membership.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<RoleRemoved>();
    }
}
