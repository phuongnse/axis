using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class RoleGrantPermissionsTests
{
    [Fact]
    public void GrantMissingPermissions_WhenAdmin_AddsOnlyMissingPermissions()
    {
        Role role = Role.CreateSystem("Admin", Guid.NewGuid(), ["users:read"]);

        bool changed = role.GrantMissingPermissions(["users:read", "tenant:settings:read"]);

        changed.Should().BeTrue();
        role.Permissions.Should().Contain("tenant:settings:read");
        role.Permissions.Should().HaveCount(2);
    }

    [Fact]
    public void GrantMissingPermissions_WhenPermissionsNull_Throws()
    {
        Role role = Role.CreateSystem("Admin", Guid.NewGuid(), ["users:read"]);

        Action act = () => role.GrantMissingPermissions(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GrantMissingPermissions_WhenNotAdmin_Throws()
    {
        Role role = Role.CreateSystem("Viewer", Guid.NewGuid(), ["page:read"]);

        Action act = () => role.GrantMissingPermissions(["tenant:settings:read"]);

        act.Should().Throw<InvalidOperationException>();
    }
}
