using Axis.Identity.Application.Commands.ChangePassword;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class ChangePasswordHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private ChangePasswordHandler CreateHandler() =>
        new(_userRepo, _hasher, _emailSender, _uow);

    private static User MakeUser()
    {
        var user = User.Create("Alice", "Smith", Email.Create("alice@acme.com").Value, OrgId);
        user.SetPasswordHash("old_hash");
        user.VerifyEmail();
        return user;
    }

    [Fact]
    public async Task Happy_path_changes_password_and_sends_notification()
    {
        var user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _hasher.Verify("OldPass1", "old_hash").Returns(true);
        _hasher.Hash("NewPass1").Returns("new_hash");

        await CreateHandler().Handle(
            new ChangePasswordCommand(user.Id, OrgId, "OldPass1", "NewPass1", "NewPass1"),
            CancellationToken.None);

        user.PasswordHash.Should().Be("new_hash");
        await _emailSender.Received(1).SendPasswordChangedNotificationAsync(
            "alice@acme.com", Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Wrong_current_password_throws_validation_exception()
    {
        var user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _hasher.Verify("WrongOld", "old_hash").Returns(false);

        var act = async () => await CreateHandler().Handle(
            new ChangePasswordCommand(user.Id, OrgId, "WrongOld", "NewPass1", "NewPass1"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Current password is incorrect*");
    }

    [Fact]
    public async Task New_password_same_as_current_throws_validation_exception()
    {
        var user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _hasher.Verify("OldPass1", "old_hash").Returns(true);

        var act = async () => await CreateHandler().Handle(
            new ChangePasswordCommand(user.Id, OrgId, "OldPass1", "OldPass1", "OldPass1"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*different from your current*");
    }

    [Fact]
    public async Task Password_confirmation_mismatch_throws_validation_exception()
    {
        var user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _hasher.Verify("OldPass1", "old_hash").Returns(true);

        var act = async () => await CreateHandler().Handle(
            new ChangePasswordCommand(user.Id, OrgId, "OldPass1", "NewPass1", "Different1"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*match*");
    }

    [Fact]
    public async Task Weak_new_password_throws_validation_exception()
    {
        var user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, OrgId).Returns(user);
        _hasher.Verify("OldPass1", "old_hash").Returns(true);

        var act = async () => await CreateHandler().Handle(
            new ChangePasswordCommand(user.Id, OrgId, "OldPass1", "short", "short"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task User_not_found_throws_validation_exception()
    {
        _userRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        var act = async () => await CreateHandler().Handle(
            new ChangePasswordCommand(Guid.NewGuid(), OrgId, "OldPass1", "NewPass1", "NewPass1"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*not found*");
    }
}
