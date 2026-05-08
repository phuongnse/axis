using Axis.Identity.Application.Commands.UpdateUserProfile;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using FluentValidation;
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
        var user = User.Create("Alice", "Smith", Email.Create("alice@example.com").Value, OrgId);
        if (avatarUrl is not null)
            user.UpdateAvatar(avatarUrl);
        return user;
    }

    [Fact]
    public async Task Happy_path_updates_name_without_avatar()
    {
        var user = MakeUser();
        _userRepo.GetByIdAsync(UserId, OrgId).Returns(user);

        var command = new UpdateUserProfileCommand(UserId, OrgId, "Bob", "Jones", null, null);
        await CreateHandler().Handle(command, CancellationToken.None);

        user.FirstName.Should().Be("Bob");
        user.LastName.Should().Be("Jones");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _avatarStorage.DidNotReceive().UploadAvatarAsync(
            Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Happy_path_uploads_new_avatar_when_no_previous()
    {
        var user = MakeUser(avatarUrl: null);
        _userRepo.GetByIdAsync(UserId, OrgId).Returns(user);
        _avatarStorage.UploadAvatarAsync(UserId, Arg.Any<byte[]>(), "image/png")
            .Returns("https://storage/avatar.png");

        var avatarBytes = new byte[512];
        var command = new UpdateUserProfileCommand(UserId, OrgId, "Alice", "Smith", avatarBytes, "image/png");
        await CreateHandler().Handle(command, CancellationToken.None);

        user.AvatarUrl.Should().Be("https://storage/avatar.png");
        await _avatarStorage.DidNotReceive().DeleteAvatarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Happy_path_replaces_old_avatar_and_deletes_it()
    {
        var user = MakeUser(avatarUrl: "https://storage/old-avatar.png");
        _userRepo.GetByIdAsync(UserId, OrgId).Returns(user);
        _avatarStorage.UploadAvatarAsync(UserId, Arg.Any<byte[]>(), "image/jpeg")
            .Returns("https://storage/new-avatar.jpg");

        var avatarBytes = new byte[512];
        var command = new UpdateUserProfileCommand(UserId, OrgId, "Alice", "Smith", avatarBytes, "image/jpeg");
        await CreateHandler().Handle(command, CancellationToken.None);

        await _avatarStorage.Received(1).DeleteAvatarAsync(
            "https://storage/old-avatar.png", Arg.Any<CancellationToken>());
        user.AvatarUrl.Should().Be("https://storage/new-avatar.jpg");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task User_not_found_throws_validation_exception()
    {
        _userRepo.GetByIdAsync(UserId, OrgId).ReturnsNull();

        var command = new UpdateUserProfileCommand(UserId, OrgId, "Bob", "Jones", null, null);
        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task Full_name_too_short_throws_validation_exception()
    {
        var user = MakeUser();
        _userRepo.GetByIdAsync(UserId, OrgId).Returns(user);

        // "A " = 2 chars but only 1 meaningful char — "A B" is 3 chars, "A " won't matter
        // Try single char first name, empty last name -> FullName = "A " -> trimmed "A" = 1 char
        var command = new UpdateUserProfileCommand(UserId, OrgId, "A", "", null, null);
        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*full name*");
    }

    [Fact]
    public async Task Full_name_too_long_throws_validation_exception()
    {
        var user = MakeUser();
        _userRepo.GetByIdAsync(UserId, OrgId).Returns(user);

        var longName = new string('A', 101);
        var command = new UpdateUserProfileCommand(UserId, OrgId, longName, "", null, null);
        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*full name*");
    }

    [Fact]
    public async Task Invalid_avatar_content_type_throws_validation_exception()
    {
        var user = MakeUser();
        _userRepo.GetByIdAsync(UserId, OrgId).Returns(user);

        var command = new UpdateUserProfileCommand(
            UserId, OrgId, "Alice", "Smith", new byte[100], "image/gif");
        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*PNG or JPG*");
    }

    [Fact]
    public async Task Avatar_exceeds_max_size_throws_validation_exception()
    {
        var user = MakeUser();
        _userRepo.GetByIdAsync(UserId, OrgId).Returns(user);

        var oversized = new byte[1_048_577]; // 1 MB + 1 byte
        var command = new UpdateUserProfileCommand(
            UserId, OrgId, "Alice", "Smith", oversized, "image/png");
        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*1 MB*");
    }
}
