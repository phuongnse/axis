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
    public void Create_produces_valid_user()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);

        user.FirstName.Should().Be("Alice");
        user.LastName.Should().Be("Smith");
        user.Email.Should().Be(ValidEmail);
        user.OrganizationId.Should().Be(OrgId);
        user.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public void Create_raises_UserRegistered_event()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);

        user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserRegistered>();
    }

    [Fact]
    public void UserRegistered_event_contains_correct_data()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);

        var evt = user.DomainEvents.OfType<UserRegistered>().Single();
        evt.UserId.Should().Be(user.Id);
        evt.OrganizationId.Should().Be(OrgId);
        evt.Email.Should().Be("alice@acme.com");
    }

    [Fact]
    public void Deactivate_changes_status_to_inactive()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);
        user.ClearDomainEvents();

        user.Deactivate();

        user.Status.Should().Be(UserStatus.Inactive);
    }

    [Fact]
    public void Deactivating_already_inactive_user_throws()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);
        user.Deactivate();

        var act = () => user.Deactivate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already inactive*");
    }

    [Fact]
    public void Assign_role_adds_to_roles_collection()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);
        var roleId = Guid.NewGuid();

        user.AssignRole(roleId);

        user.RoleIds.Should().Contain(roleId);
    }

    [Fact]
    public void Assign_same_role_twice_is_idempotent()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);
        var roleId = Guid.NewGuid();

        user.AssignRole(roleId);
        user.AssignRole(roleId);

        user.RoleIds.Should().ContainSingle(id => id == roleId);
    }

    [Fact]
    public void Remove_role_removes_from_roles_collection()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);
        var roleId = Guid.NewGuid();
        user.AssignRole(roleId);

        user.RemoveRole(roleId);

        user.RoleIds.Should().NotContain(roleId);
    }

    [Fact]
    public void FullName_combines_first_and_last_name()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);

        user.FullName.Should().Be("Alice Smith");
    }
}
