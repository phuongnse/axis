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
    private readonly ITeamAccountMembershipRepository _membershipRepo = Substitute.For<ITeamAccountMembershipRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private static readonly Guid AdminRoleId = Guid.NewGuid();
    private static readonly Guid EditorRoleId = Guid.NewGuid();

    private AssignRoleToUserHandler CreateHandler() => new(_userRepo, _membershipRepo, _roleRepo, _uow);

    private static (User User, TeamAccountMembership Membership) MakeUserWithMembership(string email = "user@acme.com")
    {
        User u = User.Create("Test", "User", Email.Create(email).Value);
        TeamAccountMembership membership = TeamAccountMembership.Create(u.Id, TeamAccountId);
        membership.AssignRole(EditorRoleId);
        return (u, membership);
    }

    [Fact]
    public async Task AssignRoleToUser_WhenRoleIsValid_AddsRoleToUser()
    {
        (User user, TeamAccountMembership membership) = MakeUserWithMembership();
        Role newRole = Role.Create("Manager", null, TeamAccountId, ["workflow:definition:read"]);
        _userRepo.GetByIdAsync(user.Id, TeamAccountId).Returns(user);
        _membershipRepo.GetByUserAndTeamAccountAsync(user.Id, TeamAccountId).Returns(membership);
        _roleRepo.GetByIdAsync(newRole.Id, TeamAccountId).Returns(newRole);

        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, TeamAccountId, newRole.Id, Action: RoleAction.Assign),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        membership.RoleIds.Should().Contain(newRole.Id);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignRoleToUser_WhenActionIsRemove_RemovesRoleFromUser()
    {
        (User user, TeamAccountMembership membership) = MakeUserWithMembership();
        Role editorRole = Role.Create("Editor", null, TeamAccountId, ["workflow:definition:read"]);
        _userRepo.GetByIdAsync(user.Id, TeamAccountId).Returns(user);
        _membershipRepo.GetByUserAndTeamAccountAsync(user.Id, TeamAccountId).Returns(membership);
        _roleRepo.GetByIdAsync(EditorRoleId, TeamAccountId).Returns(editorRole);
        membership.AssignRole(AdminRoleId); // give another role so we can remove editor

        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, TeamAccountId, EditorRoleId, Action: RoleAction.Remove),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        membership.RoleIds.Should().NotContain(EditorRoleId);
    }

    [Fact]
    public async Task AssignRoleToUser_WhenRemovingLastRole_ReturnsBusinessRuleFailure()
    {
        // User has only EditorRoleId
        (User user, TeamAccountMembership membership) = MakeUserWithMembership();
        Role editorRole = Role.Create("Editor", null, TeamAccountId, ["workflow:definition:read"]);
        _userRepo.GetByIdAsync(user.Id, TeamAccountId).Returns(user);
        _membershipRepo.GetByUserAndTeamAccountAsync(user.Id, TeamAccountId).Returns(membership);
        _roleRepo.GetByIdAsync(EditorRoleId, TeamAccountId).Returns(editorRole);

        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, TeamAccountId, EditorRoleId, Action: RoleAction.Remove),
            CancellationToken.None);

        // a user must always have at least one role
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("at least one role");
    }

    [Fact]
    public async Task AssignRoleToUser_WhenRemovingAdminRoleFromLastAdmin_ReturnsBusinessRuleFailure()
    {
        (User user, TeamAccountMembership membership) = MakeUserWithMembership();
        membership.AssignRole(AdminRoleId);
        Role adminRole = Role.CreateSystem("Admin", TeamAccountId, ["users:read"]);
        _userRepo.GetByIdAsync(user.Id, TeamAccountId).Returns(user);
        _membershipRepo.GetByUserAndTeamAccountAsync(user.Id, TeamAccountId).Returns(membership);
        _roleRepo.GetByIdAsync(AdminRoleId, TeamAccountId).Returns(adminRole);
        _membershipRepo.CountAdminsAsync(TeamAccountId, AdminRoleId).Returns(1); // last admin

        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, TeamAccountId, AdminRoleId, Action: RoleAction.Remove),
            CancellationToken.None);

        // last admin guard
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("last admin");
    }

    [Fact]
    public async Task AssignRoleToUser_WhenRoleNotFoundInTeamAccount_ReturnsNotFound()
    {
        (User user, TeamAccountMembership membership) = MakeUserWithMembership();
        _userRepo.GetByIdAsync(user.Id, TeamAccountId).Returns(user);
        _membershipRepo.GetByUserAndTeamAccountAsync(user.Id, TeamAccountId).Returns(membership);
        _roleRepo.GetByIdAsync(Arg.Any<Guid>(), TeamAccountId).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, TeamAccountId, Guid.NewGuid(), Action: RoleAction.Assign),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task AssignRoleToUser_WhenUserBelongsToAnotherTeamAccount_ReturnsNotFound()
    {
        (User user, _) = MakeUserWithMembership();
        _userRepo.GetByIdAsync(user.Id, TeamAccountId).Returns(user);

        Guid otherTeamAccountId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new AssignRoleToUserCommand(user.Id, otherTeamAccountId, EditorRoleId, Action: RoleAction.Assign),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
