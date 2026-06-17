using Axis.Identity.Application.Commands.DeactivateUser;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class DeactivateUserHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IWorkspaceMembershipRepository _membershipRepo = Substitute.For<IWorkspaceMembershipRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly ISessionStore _sessionStore = Substitute.For<ISessionStore>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid RequesterId = Guid.NewGuid();
    private readonly Role _adminRole = Role.CreateSystem("Admin", WorkspaceId, ["users:deactivate"]);

    public DeactivateUserHandlerTests()
    {
        _roleRepo.GetByNameAsync("Admin", WorkspaceId, Arg.Any<CancellationToken>()).Returns(_adminRole);
    }

    private DeactivateUserHandler CreateHandler() =>
        new(_userRepo, _membershipRepo, _roleRepo, _sessionStore, _uow);

    private static User MakeUser(string email = "user@acme.com") =>
        User.Create("Test", "User", Email.Create(email).Value);

    private static WorkspaceMembership MakeMembership(Guid userId, params Guid[] roleIds)
    {
        WorkspaceMembership membership = WorkspaceMembership.Create(userId, WorkspaceId);
        foreach (Guid roleId in roleIds)
            membership.AssignRole(roleId);
        return membership;
    }

    [Fact]
    public async Task DeactivateUser_WhenRequestIsValid_DeactivatesUserAndRevokesSessions()
    {
        User target = MakeUser();
        WorkspaceMembership membership = MakeMembership(target.Id, _adminRole.Id);
        _userRepo.GetByIdAsync(target.Id, WorkspaceId).Returns(target);
        _membershipRepo.GetByUserAndWorkspaceAsync(target.Id, WorkspaceId).Returns(membership);
        _membershipRepo.CountAdminsAsync(WorkspaceId, _adminRole.Id).Returns(2);

        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(target.Id, WorkspaceId, RequesterId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        membership.Status.Should().Be(WorkspaceMembershipStatus.Inactive);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _sessionStore.Received(1).RevokeAllAsync(target.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateUser_WhenSelfDeactivation_ReturnsBusinessRuleFailure()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, WorkspaceId).Returns(user);

        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(user.Id, WorkspaceId, user.Id),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("cannot deactivate yourself");
    }

    [Fact]
    public async Task DeactivateUser_WhenLastAdmin_ReturnsBusinessRuleFailure()
    {
        User target = MakeUser();
        WorkspaceMembership membership = MakeMembership(target.Id, _adminRole.Id);
        _userRepo.GetByIdAsync(target.Id, WorkspaceId).Returns(target);
        _membershipRepo.GetByUserAndWorkspaceAsync(target.Id, WorkspaceId).Returns(membership);
        _membershipRepo.CountAdminsAsync(WorkspaceId, _adminRole.Id).Returns(1);

        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(target.Id, WorkspaceId, RequesterId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("last admin");
    }

    [Fact]
    public async Task DeactivateUser_WhenUserNotFound_ReturnsNotFound()
    {
        _userRepo.GetByIdAsync(Arg.Any<Guid>(), WorkspaceId).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(Guid.NewGuid(), WorkspaceId, RequesterId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task DeactivateUser_WhenAdminRoleMissing_ReturnsNotFound()
    {
        User target = MakeUser();
        WorkspaceMembership membership = MakeMembership(target.Id);
        _userRepo.GetByIdAsync(target.Id, WorkspaceId).Returns(target);
        _membershipRepo.GetByUserAndWorkspaceAsync(target.Id, WorkspaceId).Returns(membership);
        _roleRepo.GetByNameAsync("Admin", WorkspaceId, Arg.Any<CancellationToken>()).Returns((Role?)null);

        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(target.Id, WorkspaceId, RequesterId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("Admin role not found");
    }

    [Fact]
    public async Task DeactivateUser_WhenUserBelongsToAnotherWorkspace_ReturnsNotFound()
    {
        User target = MakeUser();
        _userRepo.GetByIdAsync(target.Id, WorkspaceId).Returns(target);

        Guid otherWorkspaceId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new DeactivateUserCommand(target.Id, otherWorkspaceId, RequesterId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
