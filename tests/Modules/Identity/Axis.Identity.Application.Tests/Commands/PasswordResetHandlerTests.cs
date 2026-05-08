using Axis.Identity.Application.Commands.RequestPasswordReset;
using Axis.Identity.Application.Commands.ResetPassword;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class RequestPasswordResetHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IPasswordResetTokenStore _tokenStore = Substitute.For<IPasswordResetTokenStore>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private RequestPasswordResetHandler CreateHandler() =>
        new(_userRepo, _tokenStore, _emailSender);

    private static User MakeUser()
    {
        var user = User.Create("Alice", "Smith", Email.Create("alice@acme.com").Value, OrgId);
        user.SetPasswordHash("hashed");
        user.VerifyEmail();
        return user;
    }

    [Fact]
    public async Task Happy_path_creates_token_and_sends_email()
    {
        var user = MakeUser();
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);

        await CreateHandler().Handle(
            new RequestPasswordResetCommand("alice@acme.com"),
            CancellationToken.None);

        await _tokenStore.Received(1).InvalidateAllForUserAsync(user.Id, Arg.Any<CancellationToken>());
        await _tokenStore.Received(1).CreateAsync(
            user.Id, Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _emailSender.Received(1).SendPasswordResetEmailAsync(
            "alice@acme.com", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_email_does_nothing_no_error_leakage()
    {
        // Per US-027: same message regardless of whether email exists
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).ReturnsNull();

        var act = async () => await CreateHandler().Handle(
            new RequestPasswordResetCommand("unknown@acme.com"),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _emailSender.DidNotReceive().SendPasswordResetEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task New_request_invalidates_previous_token()
    {
        var user = MakeUser();
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);

        await CreateHandler().Handle(
            new RequestPasswordResetCommand("alice@acme.com"),
            CancellationToken.None);

        // Prior tokens must be invalidated before creating the new one
        await _tokenStore.Received(1).InvalidateAllForUserAsync(user.Id, Arg.Any<CancellationToken>());
    }
}

public class ResetPasswordHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IPasswordResetTokenStore _tokenStore = Substitute.For<IPasswordResetTokenStore>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private ResetPasswordHandler CreateHandler() =>
        new(_userRepo, _tokenStore, _hasher, _uow);

    private static User MakeUser()
    {
        var user = User.Create("Alice", "Smith", Email.Create("alice@acme.com").Value, OrgId);
        user.SetPasswordHash("old_hash");
        user.VerifyEmail();
        return user;
    }

    [Fact]
    public async Task Happy_path_resets_password_and_invalidates_token()
    {
        var user = MakeUser();
        _tokenStore.FindUserIdByTokenHashAsync(Arg.Any<string>()).Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _hasher.Hash("NewPass1").Returns("new_hash");

        await CreateHandler().Handle(
            new ResetPasswordCommand("valid-token", "NewPass1", "NewPass1"),
            CancellationToken.None);

        user.PasswordHash.Should().Be("new_hash");
        await _tokenStore.Received(1).InvalidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Expired_or_invalid_token_throws_validation_exception()
    {
        _tokenStore.FindUserIdByTokenHashAsync(Arg.Any<string>()).ReturnsNull();

        var act = async () => await CreateHandler().Handle(
            new ResetPasswordCommand("bad-token", "NewPass1", "NewPass1"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public async Task Password_mismatch_throws_validation_exception()
    {
        _tokenStore.FindUserIdByTokenHashAsync(Arg.Any<string>()).Returns(UserId);

        var act = async () => await CreateHandler().Handle(
            new ResetPasswordCommand("valid-token", "NewPass1", "Different1"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*match*");
    }

    [Fact]
    public async Task Weak_password_throws_validation_exception()
    {
        _tokenStore.FindUserIdByTokenHashAsync(Arg.Any<string>()).Returns(UserId);

        var act = async () => await CreateHandler().Handle(
            new ResetPasswordCommand("valid-token", "short", "short"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
