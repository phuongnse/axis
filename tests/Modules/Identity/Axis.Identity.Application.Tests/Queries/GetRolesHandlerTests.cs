using Axis.Identity.Application.Queries.GetRoles;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class GetRolesHandlerTests
{
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private GetRolesHandler CreateHandler() => new(_roleRepo);

    [Fact]
    public async Task Returns_all_roles_for_org_including_system_roles()
    {
        var roles = new List<Role>
        {
            Role.CreateSystem("Admin", OrgId, ["users:read"]),
            Role.CreateSystem("Editor", OrgId, ["workflow:definition:read"]),
            Role.CreateSystem("Viewer", OrgId, ["workflow:definition:read"]),
            Role.CreateSystem("End User", OrgId, ["form:submit"]),
            Role.Create("Manager", "Custom role", OrgId, ["workflow:definition:read"]),
        };
        _roleRepo.GetAllAsync(OrgId).Returns(roles);

        var result = await CreateHandler().Handle(new GetRolesQuery(OrgId), CancellationToken.None);

        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task Each_role_dto_contains_correct_fields()
    {
        var customRole = Role.Create("Manager", "Manages workflows", OrgId,
            ["workflow:definition:read", "workflow:definition:write"]);
        _roleRepo.GetAllAsync(OrgId).Returns(new List<Role> { customRole });

        var result = await CreateHandler().Handle(new GetRolesQuery(OrgId), CancellationToken.None);

        var dto = result.Single();
        dto.Name.Should().Be("Manager");
        dto.Description.Should().Be("Manages workflows");
        dto.IsSystem.Should().BeFalse();
        dto.Permissions.Should().HaveCount(2);
    }

    [Fact]
    public async Task System_roles_are_flagged_correctly_in_dto()
    {
        var adminRole = Role.CreateSystem("Admin", OrgId, ["users:read"]);
        _roleRepo.GetAllAsync(OrgId).Returns(new List<Role> { adminRole });

        var result = await CreateHandler().Handle(new GetRolesQuery(OrgId), CancellationToken.None);

        result.Single().IsSystem.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_org_returns_empty_list()
    {
        _roleRepo.GetAllAsync(OrgId).Returns(new List<Role>());

        var result = await CreateHandler().Handle(new GetRolesQuery(OrgId), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
