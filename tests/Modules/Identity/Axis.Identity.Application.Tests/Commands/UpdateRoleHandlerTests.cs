using Axis.Identity.Application.Commands.UpdateRole;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class UpdateRoleHandlerTests
{
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();

    private UpdateRoleHandler CreateHandler() => new(_roleRepo, _uow);

    private static UpdateRoleCommand ValidCommand() => new(
        RoleId, OrgId,
        Name: "Senior Manager",
        Description: "Updated",
        Permissions: ["workflow:definition:read", "workflow:definition:write"]);

    [Fact]
    public async Task UpdateRole_WhenRequestIsValid_UpdatesRole()
    {
        Role role = Role.Create("Manager", null, OrgId, ["workflow:definition:read"]);
        _roleRepo.GetByIdAsync(RoleId, OrgId).Returns(role);
        _roleRepo.NameExistsAsync("Senior Manager", OrgId, role.Id).Returns(false);

        Result result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        role.Name.Should().Be("Senior Manager");
        role.Description.Should().Be("Updated");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateRole_WhenSystemRole_ReturnsBusinessRuleFailure()
    {
        Role systemRole = Role.CreateSystem("Admin", OrgId, ["users:read"]);
        _roleRepo.GetByIdAsync(RoleId, OrgId).Returns(systemRole);

        Result result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        // US-023: system roles cannot be edited
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("system role");
    }

    [Fact]
    public async Task UpdateRole_WhenNameIsDuplicate_ReturnsConflict()
    {
        Role role = Role.Create("Manager", null, OrgId, ["workflow:definition:read"]);
        _roleRepo.GetByIdAsync(RoleId, OrgId).Returns(role);
        _roleRepo.NameExistsAsync("Senior Manager", OrgId, role.Id).Returns(true);

        Result result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task UpdateRole_WhenRoleNotFound_ReturnsNotFound()
    {
        _roleRepo.GetByIdAsync(RoleId, OrgId).ReturnsNull();

        Result result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task UpdateRole_WhenRoleBelongsToAnotherOrg_ReturnsNotFound()
    {
        Role role = Role.Create("Manager", null, OrgId, ["workflow:definition:read"]);
        _roleRepo.GetByIdAsync(RoleId, OrgId).Returns(role);

        Guid otherOrgId = Guid.NewGuid();
        UpdateRoleCommand command = ValidCommand() with { OrganizationId = otherOrgId };
        Result result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
