using Axis.Identity.Application.Commands.ResetPassword;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class ResetPasswordHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IPasswordResetTokenStore _tokenStore = Substitute.For<IPasswordResetTokenStore>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private ResetPasswordHandler CreateHandler() =>
        new(_userRepo, _tokenStore, _hasher, _uow);

    private static User MakeUser()
    {
        User user = User.Create("Alice", "Smith", Email.Create("alice@acme.com").Value);
        user.SetPasswordHash("old_hash");
        user.VerifyEmail();
        return user;
    }

    [Fact]
    public async Task ResetPassword_WhenTokenIsValid_ResetsPasswordAndInvalidatesToken()
    {
        User user = MakeUser();
        _tokenStore.FindUserIdByTokenHashAsync(Arg.Any<string>()).Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _hasher.Hash("fresh account passphrase").Returns("new_hash");

        Result result = await CreateHandler().Handle(
            new ResetPasswordCommand(
                "valid-token",
                "fresh account passphrase",
                "fresh account passphrase"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be("new_hash");
        await _tokenStore.Received(1).InvalidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetPassword_WhenTokenIsExpiredOrInvalid_ReturnsBusinessRuleFailure()
    {
        _tokenStore.FindUserIdByTokenHashAsync(Arg.Any<string>()).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new ResetPasswordCommand(
                "bad-token",
                "fresh account passphrase",
                "fresh account passphrase"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("expired");
    }

    [Fact]
    public async Task ResetPassword_WhenPasswordsDoNotMatch_ReturnsBusinessRuleFailure()
    {
        Result result = await CreateHandler().Handle(
            new ResetPasswordCommand(
                "valid-token",
                "fresh account passphrase",
                "different account passphrase"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("match");
    }

    [Fact]
    public async Task ResetPassword_WhenPasswordIsWeak_ReturnsBusinessRuleFailure()
    {
        Result result = await CreateHandler().Handle(
            new ResetPasswordCommand("valid-token", "short", "short"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }
}
