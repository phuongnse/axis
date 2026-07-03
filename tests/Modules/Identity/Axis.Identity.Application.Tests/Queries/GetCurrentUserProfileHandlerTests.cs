using Axis.Identity.Application.Queries.GetCurrentUserProfile;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class GetCurrentUserProfileHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IWorkspaceRepository _workspaceRepo = Substitute.For<IWorkspaceRepository>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();

    private GetCurrentUserProfileHandler CreateHandler() => new(
        _userRepo,
        _workspaceRepo);

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNull()
    {
        Guid userId = Guid.NewGuid();
        _userRepo.GetByIdPlatformWideAsync(userId, Arg.Any<CancellationToken>()).ReturnsNull();

        CurrentUserProfileDto? dto = await CreateHandler().Handle(
            new GetCurrentUserProfileQuery(userId, WorkspaceId),
            CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenUserExists_ReturnsProfile()
    {
        User user = User.Create("Ada Lovelace", Email.Create("ada@acme.com").Value);
        user.SetLanguagePreference(UserLanguage.Create("vi").Value);
        Workspace workspace = Workspace.CreatePersonal(
            "Ada Lovelace",
            WorkspaceSlug.Create("ada-lovelace").Value,
            user.Email,
            user.Id);
        workspace.ActivateAfterOwnerVerification();

        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _workspaceRepo.GetPersonalByOwnerUserIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(workspace);
        CurrentUserProfileDto? dto = await CreateHandler().Handle(
            new GetCurrentUserProfileQuery(user.Id, workspace.Id),
            CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Id.Should().Be(user.Id);
        dto.Email.Should().Be("ada@acme.com");
        dto.FullName.Should().Be("Ada Lovelace");
        dto.IsActive.Should().BeTrue();
        dto.Language.Should().Be("vi");
        dto.WorkspaceId.Should().Be(workspace.Id);
        dto.Workspaces.Should().ContainSingle();
        dto.Workspaces[0].Type.Should().Be("Personal");
        dto.Workspaces[0].IsCurrent.Should().BeTrue();
    }
}
