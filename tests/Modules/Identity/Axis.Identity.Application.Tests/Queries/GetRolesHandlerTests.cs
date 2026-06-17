using Axis.Identity.Application.Queries.GetRoles;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class GetRolesHandlerTests
{
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();

    private GetRolesHandler CreateHandler() => new(_roleRepo);

    [Fact]
    public async Task GetRoles_WithAllRoleTypes_ReturnsPagedDtos()
    {
        List<Role> roles =
        [
            Role.CreateSystem("Admin", WorkspaceId, ["users:read"]),
            Role.CreateSystem("Editor", WorkspaceId, ["workflow:definition:read"]),
            Role.CreateSystem("Viewer", WorkspaceId, ["workflow:definition:read"]),
            Role.CreateSystem("End User", WorkspaceId, ["form:submit"]),
            Role.Create("Manager", "Custom role", WorkspaceId, ["workflow:definition:read"]),
        ];
        _roleRepo.GetPagedAsync(WorkspaceId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((roles, 5));

        PagedResult<RoleDto> result = await CreateHandler()
            .Handle(new GetRolesQuery(WorkspaceId, 1, 20), CancellationToken.None);

        result.Items.Should().HaveCount(5);
        result.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task GetRoles_RoleDto_ContainsCorrectFields()
    {
        Role customRole = Role.Create("Manager", "Manages workflows", WorkspaceId,
            ["workflow:definition:read", "workflow:definition:write"]);
        _roleRepo.GetPagedAsync(WorkspaceId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((new List<Role> { customRole }, 1));

        PagedResult<RoleDto> result = await CreateHandler()
            .Handle(new GetRolesQuery(WorkspaceId, 1, 20), CancellationToken.None);

        RoleDto dto = result.Items.Single();
        dto.Name.Should().Be("Manager");
        dto.Description.Should().Be("Manages workflows");
        dto.IsSystem.Should().BeFalse();
        dto.Permissions.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRoles_SystemRole_IsFlaggedCorrectly()
    {
        Role adminRole = Role.CreateSystem("Admin", WorkspaceId, ["users:read"]);
        _roleRepo.GetPagedAsync(WorkspaceId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((new List<Role> { adminRole }, 1));

        PagedResult<RoleDto> result = await CreateHandler()
            .Handle(new GetRolesQuery(WorkspaceId, 1, 20), CancellationToken.None);

        result.Items.Single().IsSystem.Should().BeTrue();
    }

    [Fact]
    public async Task GetRoles_EmptyWorkspace_ReturnsEmptyPage()
    {
        _roleRepo.GetPagedAsync(WorkspaceId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((new List<Role>(), 0));

        PagedResult<RoleDto> result = await CreateHandler()
            .Handle(new GetRolesQuery(WorkspaceId, 1, 20), CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetRoles_PageSizeExceedsCap_ClampsTo100()
    {
        _roleRepo.GetPagedAsync(WorkspaceId, 1, 100, Arg.Any<CancellationToken>())
            .Returns((new List<Role>(), 0));

        await CreateHandler().Handle(new GetRolesQuery(WorkspaceId, 1, 200), CancellationToken.None);

        await _roleRepo.Received(1)
            .GetPagedAsync(WorkspaceId, 1, 100, Arg.Any<CancellationToken>());
    }
}
