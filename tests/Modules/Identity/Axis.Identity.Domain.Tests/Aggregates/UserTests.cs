using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class UserTests
{
    private static Email ValidEmail => Email.Create("alice@acme.com").Value;
    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public void User_WhenCreated_ProducesValidUser()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);

        user.FirstName.Should().Be("Alice");
        user.LastName.Should().Be("Smith");
        user.Email.Should().Be(ValidEmail);
        user.OrganizationId.Should().Be(OrgId);
        user.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public void User_WhenCreated_RaisesUserRegisteredEvent()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);

        user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserRegistered>();
    }

    [Fact]
    public void User_WhenCreated_UserRegisteredEventContainsCorrectData()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);

        var evt = user.DomainEvents.OfType<UserRegistered>().Single();
        evt.UserId.Should().Be(user.Id);
        evt.OrganizationId.Should().Be(OrgId);
        evt.Email.Should().Be("alice@acme.com");
    }

    [Fact]
    public void User_WhenDeactivated_ChangesStatusToInactive()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);
        user.ClearDomainEvents();

        user.Deactivate();

        user.Status.Should().Be(UserStatus.Inactive);
    }

    [Fact]
    public void User_WhenAlreadyInactive_DeactivateThrows()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);
        user.Deactivate();

        var act = () => user.Deactivate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already inactive*");
    }

    [Fact]
    public void User_WhenRoleAssigned_AddsToRolesCollection()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);
        var roleId = Guid.NewGuid();

        user.AssignRole(roleId);

        user.RoleIds.Should().Contain(roleId);
    }

    [Fact]
    public void User_WhenSameRoleAssignedTwice_IsIdempotent()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);
        var roleId = Guid.NewGuid();

        user.AssignRole(roleId);
        user.AssignRole(roleId);

        user.RoleIds.Should().ContainSingle(id => id == roleId);
    }

    [Fact]
    public void User_WhenRoleRemoved_RemovesFromRolesCollection()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);
        var roleId = Guid.NewGuid();
        user.AssignRole(roleId);

        user.RemoveRole(roleId);

        user.RoleIds.Should().NotContain(roleId);
    }

    [Fact]
    public void User_WhenAccessed_FullNameCombinesFirstAndLastName()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);

        user.FullName.Should().Be("Alice Smith");
    }
}
