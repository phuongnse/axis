using Axis.Identity.Application.Commands.ChangePassword;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class ChangePasswordHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid TenantId = Guid.NewGuid();

    private ChangePasswordHandler CreateHandler() =>
        new(_userRepo, _hasher, _emailSender, _uow);

    private static User MakeUser()
    {
        User user = User.Create("Alice", "Smith", Email.Create("alice@acme.com").Value);
        user.SetPasswordHash("old_hash");
        user.VerifyEmail();
        return user;
    }

    [Fact]
    public async Task ChangePassword_WhenCurrentPasswordIsCorrect_ChangesPasswordAndSendsNotification()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, TenantId).Returns(user);
        _hasher.Verify("old account passphrase", "old_hash").Returns(true);
        _hasher.Hash("fresh account passphrase").Returns("new_hash");

        Result result = await CreateHandler().Handle(
            new ChangePasswordCommand(
                user.Id,
                TenantId,
                "old account passphrase",
                "fresh account passphrase",
                "fresh account passphrase"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be("new_hash");
        await _emailSender.Received(1).SendPasswordChangedNotificationAsync(
            "alice@acme.com", Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangePassword_WhenCurrentPasswordIsWrong_ReturnsBusinessRuleFailure()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, TenantId).Returns(user);
        _hasher.Verify("WrongOld", "old_hash").Returns(false);

        Result result = await CreateHandler().Handle(
            new ChangePasswordCommand(
                user.Id,
                TenantId,
                "WrongOld",
                "fresh account passphrase",
                "fresh account passphrase"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("Current password is incorrect");
    }

    [Fact]
    public async Task ChangePassword_WhenNewPasswordSameAsCurrent_ReturnsBusinessRuleFailure()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, TenantId).Returns(user);
        _hasher.Verify("old account passphrase", "old_hash").Returns(true);

        Result result = await CreateHandler().Handle(
            new ChangePasswordCommand(
                user.Id,
                TenantId,
                "old account passphrase",
                "old account passphrase",
                "old account passphrase"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("different from your current");
    }

    [Fact]
    public async Task ChangePassword_WhenConfirmationDoesNotMatch_ReturnsBusinessRuleFailure()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, TenantId).Returns(user);
        _hasher.Verify("old account passphrase", "old_hash").Returns(true);

        Result result = await CreateHandler().Handle(
            new ChangePasswordCommand(
                user.Id,
                TenantId,
                "old account passphrase",
                "fresh account passphrase",
                "different account passphrase"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("match");
    }

    [Fact]
    public async Task ChangePassword_WhenNewPasswordIsWeak_ReturnsBusinessRuleFailure()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, TenantId).Returns(user);
        _hasher.Verify("old account passphrase", "old_hash").Returns(true);

        Result result = await CreateHandler().Handle(
            new ChangePasswordCommand(user.Id, TenantId, "old account passphrase", "short", "short"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }

    [Fact]
    public async Task ChangePassword_WhenUserNotFound_ReturnsNotFound()
    {
        _userRepo.GetByIdAsync(Arg.Any<Guid>(), TenantId).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new ChangePasswordCommand(
                Guid.NewGuid(),
                TenantId,
                "old account passphrase",
                "fresh account passphrase",
                "fresh account passphrase"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ChangePassword_WhenUserBelongsToAnotherTenant_ReturnsNotFound()
    {
        User user = MakeUser();
        _userRepo.GetByIdAsync(user.Id, TenantId).Returns(user);

        Guid otherTenantId = Guid.NewGuid();
        Result result = await CreateHandler().Handle(
            new ChangePasswordCommand(
                user.Id,
                otherTenantId,
                "old account passphrase",
                "fresh account passphrase",
                "fresh account passphrase"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
