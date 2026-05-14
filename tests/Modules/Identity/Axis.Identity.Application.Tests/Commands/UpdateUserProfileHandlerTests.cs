using Axis.Identity.Application.Commands.UpdateUserProfile;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class UpdateUserProfileHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IAvatarStorageService _avatarStorage = Substitute.For<IAvatarStorageService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();

    private UpdateUserProfileHandler CreateHandler() =>
        new(_userRepo, _avatarStorage, _uow);

    private static User MakeUser(string? avatarUrl = null)
    {
        User user = User.Create("Alice", "Smith", Email.Create("alice@example.com").Value, OrgId);
        if (avatarUrl is not null)
            user.UpdateAvatar(avatarUrl);
        return user;
    }

    [Fact]
    public async Task UpdateUserProfile_WhenNoAvatarProvided_UpdatesNameOnly()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(UserId, OrgId).Returns(user);

        UpdateUserProfileCommand command = new(UserId, OrgId, "Bob", "Jones", null, null);
        Result result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.FirstName.Should().Be("Bob");
        user.LastName.Should().Be("Jones");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _avatarStorage.DidNotReceive().UploadAvatarAsync(
            Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateUserProfile_WhenNoPreviousAvatar_UploadsNewAvatar()
    {
        User user = MakeUser(avatarUrl: null);
        _userRepo.GetByIdAsync(UserId, OrgId).Returns(user);
        _avatarStorage.UploadAvatarAsync(UserId, Arg.Any<byte[]>(), "image/png")
            .Returns("https://storage/avatar.png");

        byte[] avatarBytes = new byte[512];
        UpdateUserProfileCommand command = new(UserId, OrgId, "Alice", "Smith", avatarBytes, "image/png");
        Result result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.AvatarUrl.Should().Be("https://storage/avatar.png");
        await _avatarStorage.DidNotReceive().DeleteAvatarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateUserProfile_WhenAvatarExists_ReplacesOldAndDeletesIt()
    {
        User user = MakeUser(avatarUrl: "https://storage/old-avatar.png");
        _userRepo.GetByIdAsync(UserId, OrgId).Returns(user);
        _avatarStorage.UploadAvatarAsync(UserId, Arg.Any<byte[]>(), "image/jpeg")
            .Returns("https://storage/new-avatar.jpg");

        byte[] avatarBytes = new byte[512];
        UpdateUserProfileCommand command = new(UserId, OrgId, "Alice", "Smith", avatarBytes, "image/jpeg");
        Result result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _avatarStorage.Received(1).DeleteAvatarAsync(
            "https://storage/old-avatar.png", Arg.Any<CancellationToken>());
        user.AvatarUrl.Should().Be("https://storage/new-avatar.jpg");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateUserProfile_WhenUserNotFound_ReturnsNotFound()
    {
        _userRepo.GetByIdAsync(UserId, OrgId).ReturnsNull();

        UpdateUserProfileCommand command = new(UserId, OrgId, "Bob", "Jones", null, null);
        Result result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task UpdateUserProfile_WhenFullNameTooShort_ReturnsBusinessRuleFailure()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(UserId, OrgId).Returns(user);

        UpdateUserProfileCommand command = new(UserId, OrgId, "A", "", null, null);
        Result result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("Full name");
    }

    [Fact]
    public async Task UpdateUserProfile_WhenFullNameTooLong_ReturnsBusinessRuleFailure()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(UserId, OrgId).Returns(user);

        string longName = new('A', 101);
        UpdateUserProfileCommand command = new(UserId, OrgId, longName, "", null, null);
        Result result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("Full name");
    }

    [Fact]
    public async Task UpdateUserProfile_WhenAvatarContentTypeIsInvalid_ReturnsBusinessRuleFailure()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(UserId, OrgId).Returns(user);

        UpdateUserProfileCommand command = new(
            UserId, OrgId, "Alice", "Smith", new byte[100], "image/gif");
        Result result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("PNG or JPG");
    }

    [Fact]
    public async Task UpdateUserProfile_WhenAvatarExceedsMaxSize_ReturnsBusinessRuleFailure()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(UserId, OrgId).Returns(user);

        byte[] oversized = new byte[1_048_577]; // 1 MB + 1 byte
        UpdateUserProfileCommand command = new(
            UserId, OrgId, "Alice", "Smith", oversized, "image/png");
        Result result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("1 MB");
    }
}
