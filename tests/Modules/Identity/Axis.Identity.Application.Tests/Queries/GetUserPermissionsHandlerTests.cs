using Axis.Identity.Application.Queries.GetUserPermissions;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class GetUserPermissionsHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IWorkspaceMembershipRepository _membershipRepo = Substitute.For<IWorkspaceMembershipRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();

    private GetUserPermissionsHandler CreateHandler() => new(_userRepo, _membershipRepo, _roleRepo);

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFound()
    {
        Guid userId = Guid.NewGuid();
        _userRepo.GetByIdAsync(userId, WorkspaceId, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        Result<GetUserPermissionsResult> result = await CreateHandler()
            .Handle(new GetUserPermissionsQuery(userId, WorkspaceId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenUserInactive_ReturnsEmptyPermissions()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@example.com").Value);
        user.Deactivate();
        _userRepo.GetByIdAsync(user.Id, WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(user);

        Result<GetUserPermissionsResult> result = await CreateHandler()
            .Handle(new GetUserPermissionsQuery(user.Id, WorkspaceId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Permissions.Should().BeEmpty();
        await _roleRepo.DidNotReceive().GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenUserHasRoles_ReturnsDistinctPermissions()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@example.com").Value);
        Role editor = Role.CreateSystem("Editor", WorkspaceId, ["workflow:definition:read", "workflow:definition:write"]);
        Role viewer = Role.CreateSystem("Viewer", WorkspaceId, ["workflow:definition:read"]);
        WorkspaceMembership membership = WorkspaceMembership.Create(user.Id, WorkspaceId);
        membership.AssignRole(editor.Id);
        membership.AssignRole(viewer.Id);

        _userRepo.GetByIdAsync(user.Id, WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(user);
        _membershipRepo.GetByUserAndWorkspaceAsync(user.Id, WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(membership);
        _roleRepo.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(new List<Role> { editor, viewer });

        Result<GetUserPermissionsResult> result = await CreateHandler()
            .Handle(new GetUserPermissionsQuery(user.Id, WorkspaceId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Permissions.Should().BeEquivalentTo(
            ["workflow:definition:read", "workflow:definition:write"],
            opts => opts.WithoutStrictOrdering());
    }
}
