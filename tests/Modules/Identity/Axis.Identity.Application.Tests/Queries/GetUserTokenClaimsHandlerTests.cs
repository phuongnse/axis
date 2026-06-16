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
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();

    private GetUserTokenClaimsHandler CreateHandler() => new(
        _userRepo,
        _membershipRepo,
        _workspaceRepo,
        _roleRepo);

    private static Workspace ActiveWorkspace(Guid workspaceId)
    {
        Workspace workspace = Workspace.Create(
            "Ada workspace",
            WorkspaceSlug.Create($"workspace-{workspaceId:N}"[..20]).Value,
            Email.Create("owner@example.com").Value,
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
    public async Task Handle_WhenUserInactive_ReturnsNotFound()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@example.com").Value);
        user.Deactivate();
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        Result<UserTokenClaimsDto> result = await CreateHandler().Handle(
            new GetUserTokenClaimsQuery(user.Id, WorkspaceId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenworkspaceIdOmitted_UsesUserWorkspace()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@example.com").Value);
        Role role = Role.CreateSystem("Editor", WorkspaceId, ["workflow:definition:read"]);
        WorkspaceMembership membership = WorkspaceMembership.Create(user.Id, WorkspaceId);
        membership.AssignRole(role.Id);
        Workspace workspace = ActiveWorkspace(WorkspaceId);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _workspaceRepo.GetByIdAsync(WorkspaceId, Arg.Any<CancellationToken>()).Returns(workspace);
        _roleRepo.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(new List<Role> { role });

        Result<UserTokenClaimsDto> result = await CreateHandler().Handle(
            new GetUserTokenClaimsQuery(user.Id, workspaceId: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.workspaceId.Should().Be(WorkspaceId);
        result.Value.Permissions.Should().Contain("workflow:definition:read");
    }

    [Fact]
    public async Task Handle_WhenworkspaceIdMismatchesUser_ReturnsBusinessRuleFailure()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@example.com").Value);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        Guid otherWorkspaceId = Guid.NewGuid();
        Result<UserTokenClaimsDto> result = await CreateHandler().Handle(
            new GetUserTokenClaimsQuery(user.Id, otherWorkspaceId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        await _roleRepo.DidNotReceive().GetByIdsAsync(
            Arg.Any<IEnumerable<Guid>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenUserActive_ReturnsTokenClaims()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@example.com").Value);
        Role editor = Role.CreateSystem("Editor", WorkspaceId, ["workflow:definition:read", "workflow:definition:write"]);
        WorkspaceMembership membership = WorkspaceMembership.Create(user.Id, WorkspaceId);
        membership.AssignRole(editor.Id);
        Workspace workspace = ActiveWorkspace(WorkspaceId);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _membershipRepo.GetByUserAndWorkspaceAsync(user.Id, WorkspaceId, Arg.Any<CancellationToken>()).Returns(membership);
        _workspaceRepo.GetByIdAsync(WorkspaceId, Arg.Any<CancellationToken>()).Returns(workspace);
        _roleRepo.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(new List<Role> { editor });

        Result<UserTokenClaimsDto> result = await CreateHandler().Handle(
            new GetUserTokenClaimsQuery(user.Id, WorkspaceId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(user.Id);
        result.Value.Email.Should().Be("ada@example.com");
        result.Value.FullName.Should().Be("Ada Lovelace");
        result.Value.Permissions.Should().BeEquivalentTo(
            ["workflow:definition:read", "workflow:definition:write"],
            opts => opts.WithoutStrictOrdering());
    }
}
