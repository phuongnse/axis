using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class RoleTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public void Role_WhenCreated_ProducesCustomRoleWithPermissions()
    {
        string[] permissions = new[] { "workflow:definition:read", "workflow:definition:write" };
        Role role = Role.Create("Manager", "Can manage workflows", OrgId, permissions);

        role.Name.Should().Be("Manager");
        role.Description.Should().Be("Can manage workflows");
        role.OrganizationId.Should().Be(OrgId);
        role.IsSystem.Should().BeFalse();
        role.Permissions.Should().BeEquivalentTo(permissions);
    }

    [Fact]
    public void Role_WhenCreated_RaisesRoleCreatedEvent()
    {
        Role role = Role.Create("Manager", null, OrgId, ["workflow:definition:read"]);

        role.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<RoleCreated>();
    }

    [Fact]
    public void Role_WhenCreatedWithNoPermissions_Throws()
    {
        Func<Role> act = () => Role.Create("Empty", null, OrgId, []);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least one permission*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Role_WhenCreatedWithEmptyName_Throws(string name)
    {
        Func<Role> act = () => Role.Create(name, null, OrgId, ["workflow:definition:read"]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Role_WhenUpdated_ChangesNameDescriptionAndPermissions()
    {
        Role role = Role.Create("Manager", null, OrgId, ["workflow:definition:read"]);
        role.ClearDomainEvents();

        role.Update("Senior Manager", "Updated", ["workflow:definition:read", "workflow:definition:write"]);

        role.Name.Should().Be("Senior Manager");
        role.Description.Should().Be("Updated");
        role.Permissions.Should().HaveCount(2);
        role.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<RoleUpdated>();
    }

    [Fact]
    public void Role_WhenSystemRoleUpdated_Throws()
    {
        Role role = Role.CreateSystem("Admin", OrgId, ["users:read", "users:invite"]);

        Action act = () => role.Update("Hacked", null, ["users:read"]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*system role*");
    }

    [Fact]
    public void Role_WhenCreatedAsSystem_IsMarkedAsSystemRole()
    {
        Role role = Role.CreateSystem("Viewer", OrgId, ["workflow:definition:read"]);

        role.IsSystem.Should().BeTrue();
    }

    [Fact]
    public void Role_WhenDuplicatePermissionsProvided_DeduplicatesPermissions()
    {
        Role role = Role.Create("Manager", null, OrgId,
                    ["workflow:definition:read", "workflow:definition:read"]);

        role.Permissions.Should().ContainSingle("workflow:definition:read");
    }
}
