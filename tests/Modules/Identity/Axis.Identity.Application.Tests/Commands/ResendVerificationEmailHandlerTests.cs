using Axis.Identity.Application.Commands.ResendVerificationEmail;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class ResendVerificationEmailHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IEmailVerificationTokenStore _tokenStore = Substitute.For<IEmailVerificationTokenStore>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IResendVerificationRateLimiter _rateLimiter = Substitute.For<IResendVerificationRateLimiter>();

    private ResendVerificationEmailHandler CreateHandler() =>
        new(_userRepo, _tokenStore, _emailSender, _rateLimiter);

    private static User MakeUnverifiedUser(string email = "alice@acme.com")
    {
        User user = User.Create("Alice Smith", Email.Create(email).Value);
        user.SetPasswordHash("hashed");
        return user;
    }

    [Fact]
    public async Task ResendVerificationEmail_WhenUserExists_ResendsVerificationEmail()
    {
        User user = MakeUnverifiedUser();
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);
        _rateLimiter.TryRecordResendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await CreateHandler().Handle(
            new ResendVerificationEmailCommand("alice@acme.com"),
            CancellationToken.None);

        await _tokenStore.Received(1).InvalidateAllForUserAsync(user.Id, Arg.Any<CancellationToken>());
        await _tokenStore.Received(1).CreateAsync(
            user.Id, Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _emailSender.Received(1).SendVerificationEmailAsync(
            "alice@acme.com", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendVerificationEmail_WhenEmailNotFound_DoesNothingWithoutErrorLeakage()
    {
        _rateLimiter.TryRecordResendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).ReturnsNull();

        Func<Task> act = async () => await CreateHandler().Handle(
            new ResendVerificationEmailCommand("unknown@acme.com"),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _rateLimiter.Received(1).TryRecordResendAsync(
            "unknown@acme.com", Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendVerificationEmail_WhenAlreadyVerified_DoesNothing()
    {
        User user = MakeUnverifiedUser();
        user.VerifyEmail();
        _rateLimiter.TryRecordResendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);

        await CreateHandler().Handle(
            new ResendVerificationEmailCommand("alice@acme.com"),
            CancellationToken.None);

        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _rateLimiter.Received(1).TryRecordResendAsync(
            "alice@acme.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendVerificationEmail_WhenRateLimited_ReturnsRateLimitedWithoutLookupOrEmail()
    {
        _rateLimiter.TryRecordResendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(
                ErrorCodes.RateLimited,
                "Please wait before requesting another email."));

        Result result = await CreateHandler().Handle(
            new ResendVerificationEmailCommand("alice@acme.com"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.RateLimited);
        await _userRepo.DidNotReceive().FindByEmailGloballyAsync(
            Arg.Any<Email>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
