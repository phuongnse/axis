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
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private GetUserPermissionsHandler CreateHandler() => new(_userRepo, _roleRepo);

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFound()
    {
        Guid userId = Guid.NewGuid();
        _userRepo.GetByIdAsync(userId, OrgId, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        Result<GetUserPermissionsResult> result = await CreateHandler()
            .Handle(new GetUserPermissionsQuery(userId, OrgId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenUserInactive_ReturnsEmptyPermissions()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@example.com").Value, OrgId);
        user.Deactivate();
        _userRepo.GetByIdAsync(user.Id, OrgId, Arg.Any<CancellationToken>())
            .Returns(user);

        Result<GetUserPermissionsResult> result = await CreateHandler()
            .Handle(new GetUserPermissionsQuery(user.Id, OrgId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Permissions.Should().BeEmpty();
        await _roleRepo.DidNotReceive().GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenUserHasRoles_ReturnsDistinctPermissions()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@example.com").Value, OrgId);
        Role editor = Role.CreateSystem("Editor", OrgId, ["workflow:definition:read", "workflow:definition:write"]);
        Role viewer = Role.CreateSystem("Viewer", OrgId, ["workflow:definition:read"]);
        user.AssignRole(editor.Id);
        user.AssignRole(viewer.Id);

        _userRepo.GetByIdAsync(user.Id, OrgId, Arg.Any<CancellationToken>())
            .Returns(user);
        _roleRepo.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), OrgId, Arg.Any<CancellationToken>())
            .Returns(new List<Role> { editor, viewer });

        Result<GetUserPermissionsResult> result = await CreateHandler()
            .Handle(new GetUserPermissionsQuery(user.Id, OrgId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Permissions.Should().BeEquivalentTo(
            ["workflow:definition:read", "workflow:definition:write"],
            opts => opts.WithoutStrictOrdering());
    }
}
