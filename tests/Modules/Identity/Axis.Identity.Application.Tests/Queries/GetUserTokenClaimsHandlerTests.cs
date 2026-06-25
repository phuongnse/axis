using Axis.Identity.Application.Queries.GetUserTokenClaims;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class GetUserTokenClaimsHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IWorkspaceMembershipRepository _membershipRepo = Substitute.For<IWorkspaceMembershipRepository>();
    private readonly IWorkspaceRepository _workspaceRepo = Substitute.For<IWorkspaceRepository>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();

    private GetUserTokenClaimsHandler CreateHandler() => new(
        _userRepo,
        _membershipRepo,
        _workspaceRepo);

    private static Workspace ActiveWorkspace(Guid ownerUserId)
    {
        Workspace workspace = Workspace.CreatePersonal(
            "Ada Lovelace",
            WorkspaceSlug.Create("ada-lovelace").Value,
            Email.Create("ada@example.com").Value,
            ownerUserId);
        workspace.ActivateAfterOwnerVerification();
        return workspace;
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFound()
    {
        Guid userId = Guid.NewGuid();
        _userRepo.GetByIdPlatformWideAsync(userId, Arg.Any<CancellationToken>()).ReturnsNull();

        Result<UserTokenClaimsDto> result = await CreateHandler().Handle(
            new GetUserTokenClaimsQuery(userId, WorkspaceId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenWorkspaceIdOmitted_UsesFirstActiveWorkspace()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@example.com").Value);
        WorkspaceMembership membership = WorkspaceMembership.Create(user.Id, WorkspaceId);
        Workspace workspace = ActiveWorkspace(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _workspaceRepo.GetByIdAsync(WorkspaceId, Arg.Any<CancellationToken>()).Returns(workspace);

        Result<UserTokenClaimsDto> result = await CreateHandler().Handle(
            new GetUserTokenClaimsQuery(user.Id, workspaceId: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.workspaceId.Should().Be(WorkspaceId);
    }

    [Fact]
    public async Task Handle_WhenWorkspaceIdMismatchesUser_ReturnsBusinessRuleFailure()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@example.com").Value);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        Result<UserTokenClaimsDto> result = await CreateHandler().Handle(
            new GetUserTokenClaimsQuery(user.Id, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }

    [Fact]
    public async Task Handle_WhenUserActive_ReturnsTokenClaims()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@example.com").Value);
        WorkspaceMembership membership = WorkspaceMembership.Create(user.Id, WorkspaceId);
        Workspace workspace = ActiveWorkspace(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _membershipRepo.GetByUserAndWorkspaceAsync(user.Id, WorkspaceId, Arg.Any<CancellationToken>()).Returns(membership);
        _workspaceRepo.GetByIdAsync(WorkspaceId, Arg.Any<CancellationToken>()).Returns(workspace);

        Result<UserTokenClaimsDto> result = await CreateHandler().Handle(
            new GetUserTokenClaimsQuery(user.Id, WorkspaceId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(user.Id);
        result.Value.Email.Should().Be("ada@example.com");
        result.Value.FullName.Should().Be("Ada Lovelace");
    }
}
