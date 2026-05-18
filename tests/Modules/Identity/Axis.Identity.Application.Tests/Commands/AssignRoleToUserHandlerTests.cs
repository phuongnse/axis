using Axis.Identity.Application.Commands.AssignRoleToUser;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class AssignRoleToUserHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid AdminRoleId = Guid.NewGuid();
    private static readonly Guid EditorRoleId = Guid.NewGuid();

    private AssignRoleToUserHandler CreateHandler() => new(_userRepo, _roleRepo, _uow);

    private static User MakeUser(string email = "user@acme.com")
    {
        User u = User.Create("Test", "User", Email.Create(email).Value, OrgId);
        u.AssignRole(EditorRoleId);
        return u;
    }

    [Fact]
    public async Task AssignRoleToUser_WhenRoleIsValid_AddsRoleToUser()
    {
        User user = MakeUser();
        Role newRole = Role.Create("Manager", null, OrgId, ["workflow:definition:read"]);
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _roleRepo.GetByIdAsync(newRole.Id, OrgId).Returns(newRole);

        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, OrgId, newRole.Id, Action: RoleAction.Assign),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.RoleIds.Should().Contain(newRole.Id);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignRoleToUser_WhenActionIsRemove_RemovesRoleFromUser()
    {
        User user = MakeUser();
        Role editorRole = Role.Create("Editor", null, OrgId, ["workflow:definition:read"]);
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _roleRepo.GetByIdAsync(EditorRoleId, OrgId).Returns(editorRole);
        user.AssignRole(AdminRoleId); // give another role so we can remove editor

        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, OrgId, EditorRoleId, Action: RoleAction.Remove),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.RoleIds.Should().NotContain(EditorRoleId);
    }

    [Fact]
    public async Task AssignRoleToUser_WhenRemovingLastRole_ReturnsBusinessRuleFailure()
    {
        // User has only EditorRoleId
        User user = MakeUser();
        Role editorRole = Role.Create("Editor", null, OrgId, ["workflow:definition:read"]);
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _roleRepo.GetByIdAsync(EditorRoleId, OrgId).Returns(editorRole);

        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, OrgId, EditorRoleId, Action: RoleAction.Remove),
            CancellationToken.None);

        // US-024: a user must always have at least one role
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("at least one role");
    }

    [Fact]
    public async Task AssignRoleToUser_WhenRemovingAdminRoleFromLastAdmin_ReturnsBusinessRuleFailure()
    {
        User user = MakeUser();
        user.AssignRole(AdminRoleId);
        Role adminRole = Role.CreateSystem("Admin", OrgId, ["users:read"]);
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _roleRepo.GetByIdAsync(AdminRoleId, OrgId).Returns(adminRole);
        _userRepo.CountAdminsAsync(OrgId, AdminRoleId).Returns(1); // last admin

        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, OrgId, AdminRoleId, Action: RoleAction.Remove),
            CancellationToken.None);

        // US-024: last admin guard
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("last admin");
    }

    [Fact]
    public async Task AssignRoleToUser_WhenRoleNotFoundInOrg_ReturnsNotFound()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _roleRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, OrgId, Guid.NewGuid(), Action: RoleAction.Assign),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task AssignRoleToUser_WhenUserBelongsToAnotherOrg_ReturnsNotFound()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);

        Guid otherOrgId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, otherOrgId, EditorRoleId, Action: RoleAction.Assign),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
