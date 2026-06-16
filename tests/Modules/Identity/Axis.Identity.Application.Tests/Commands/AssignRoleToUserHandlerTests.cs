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
    private readonly IWorkspaceMembershipRepository _membershipRepo = Substitute.For<IWorkspaceMembershipRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid AdminRoleId = Guid.NewGuid();
    private static readonly Guid EditorRoleId = Guid.NewGuid();

    private AssignRoleToUserHandler CreateHandler() => new(_userRepo, _membershipRepo, _roleRepo, _uow);

    private static (User User, WorkspaceMembership Membership) MakeUserWithMembership(string email = "user@acme.com")
    {
        User u = User.Create("Test", "User", Email.Create(email).Value);
        WorkspaceMembership membership = WorkspaceMembership.Create(u.Id, WorkspaceId);
        membership.AssignRole(EditorRoleId);
        return (u, membership);
    }

    [Fact]
    public async Task AssignRoleToUser_WhenRoleIsValid_AddsRoleToUser()
    {
        (User user, WorkspaceMembership membership) = MakeUserWithMembership();
        Role newRole = Role.Create("Manager", null, WorkspaceId, ["workflow:definition:read"]);
        _userRepo.GetByIdAsync(user.Id, WorkspaceId).Returns(user);
        _membershipRepo.GetByUserAndWorkspaceAsync(user.Id, WorkspaceId).Returns(membership);
        _roleRepo.GetByIdAsync(newRole.Id, WorkspaceId).Returns(newRole);

        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, WorkspaceId, newRole.Id, Action: RoleAction.Assign),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        membership.RoleIds.Should().Contain(newRole.Id);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignRoleToUser_WhenActionIsRemove_RemovesRoleFromUser()
    {
        (User user, WorkspaceMembership membership) = MakeUserWithMembership();
        Role editorRole = Role.Create("Editor", null, WorkspaceId, ["workflow:definition:read"]);
        _userRepo.GetByIdAsync(user.Id, WorkspaceId).Returns(user);
        _membershipRepo.GetByUserAndWorkspaceAsync(user.Id, WorkspaceId).Returns(membership);
        _roleRepo.GetByIdAsync(EditorRoleId, WorkspaceId).Returns(editorRole);
        membership.AssignRole(AdminRoleId); // give another role so we can remove editor

        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, WorkspaceId, EditorRoleId, Action: RoleAction.Remove),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        membership.RoleIds.Should().NotContain(EditorRoleId);
    }

    [Fact]
    public async Task AssignRoleToUser_WhenRemovingLastRole_ReturnsBusinessRuleFailure()
    {
        // User has only EditorRoleId
        (User user, WorkspaceMembership membership) = MakeUserWithMembership();
        Role editorRole = Role.Create("Editor", null, WorkspaceId, ["workflow:definition:read"]);
        _userRepo.GetByIdAsync(user.Id, WorkspaceId).Returns(user);
        _membershipRepo.GetByUserAndWorkspaceAsync(user.Id, WorkspaceId).Returns(membership);
        _roleRepo.GetByIdAsync(EditorRoleId, WorkspaceId).Returns(editorRole);

        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, WorkspaceId, EditorRoleId, Action: RoleAction.Remove),
            CancellationToken.None);

        // a user must always have at least one role
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("at least one role");
    }

    [Fact]
    public async Task AssignRoleToUser_WhenRemovingAdminRoleFromLastAdmin_ReturnsBusinessRuleFailure()
    {
        (User user, WorkspaceMembership membership) = MakeUserWithMembership();
        membership.AssignRole(AdminRoleId);
        Role adminRole = Role.CreateSystem("Admin", WorkspaceId, ["users:read"]);
        _userRepo.GetByIdAsync(user.Id, WorkspaceId).Returns(user);
        _membershipRepo.GetByUserAndWorkspaceAsync(user.Id, WorkspaceId).Returns(membership);
        _roleRepo.GetByIdAsync(AdminRoleId, WorkspaceId).Returns(adminRole);
        _membershipRepo.CountAdminsAsync(WorkspaceId, AdminRoleId).Returns(1); // last admin

        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, WorkspaceId, AdminRoleId, Action: RoleAction.Remove),
            CancellationToken.None);

        // last admin guard
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("last admin");
    }

    [Fact]
    public async Task AssignRoleToUser_WhenRoleNotFoundInWorkspace_ReturnsNotFound()
    {
        (User user, WorkspaceMembership membership) = MakeUserWithMembership();
        _userRepo.GetByIdAsync(user.Id, WorkspaceId).Returns(user);
        _membershipRepo.GetByUserAndWorkspaceAsync(user.Id, WorkspaceId).Returns(membership);
        _roleRepo.GetByIdAsync(Arg.Any<Guid>(), WorkspaceId).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, WorkspaceId, Guid.NewGuid(), Action: RoleAction.Assign),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task AssignRoleToUser_WhenUserBelongsToAnotherWorkspace_ReturnsNotFound()
    {
        (User user, _) = MakeUserWithMembership();
        _userRepo.GetByIdAsync(user.Id, WorkspaceId).Returns(user);

        Guid otherWorkspaceId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, otherWorkspaceId, EditorRoleId, Action: RoleAction.Assign),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
