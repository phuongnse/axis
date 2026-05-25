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

    private static readonly Guid OrgId = Guid.NewGuid();

    private GetCurrentUserProfileHandler CreateHandler() => new(_userRepo);

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNull()
    {
        Guid userId = Guid.NewGuid();
        _userRepo.GetByIdPlatformWideAsync(userId, Arg.Any<CancellationToken>()).ReturnsNull();

        CurrentUserProfileDto? dto = await CreateHandler().Handle(
            new GetCurrentUserProfileQuery(userId, OrgId, ["workflow:definition:read"]),
            CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenUserExists_ReturnsProfileWithJwtPermissions()
    {
        User user = User.Create("Ada", "Lovelace", Email.Create("ada@acme.com").Value, OrgId);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        IReadOnlyList<string> jwtPermissions = ["workflow:definition:read", "users:read"];

        CurrentUserProfileDto? dto = await CreateHandler().Handle(
            new GetCurrentUserProfileQuery(user.Id, OrgId, jwtPermissions),
            CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Id.Should().Be(user.Id);
        dto.Email.Should().Be("ada@acme.com");
        dto.FullName.Should().Be("Ada Lovelace");
        dto.IsActive.Should().BeTrue();
        dto.OrgId.Should().Be(OrgId);
        dto.Permissions.Should().BeEquivalentTo(jwtPermissions);
    }
}
