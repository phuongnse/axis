using Axis.Identity.Application.Commands.VerifyEmail;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class ResendVerificationEmailHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private ResendVerificationEmailHandler CreateHandler() => new(_userRepo, _emailSender);

    private static User MakeUnverifiedUser(string email = "alice@acme.com")
    {
        User user = User.Create("Alice", "Smith", Email.Create(email).Value, OrgId);
        user.SetPasswordHash("hashed");
        return user;
    }

    [Fact]
    public async Task ResendVerificationEmail_WhenUserExists_ResendsVerificationEmail()
    {
        User user = MakeUnverifiedUser();
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);

        await CreateHandler().Handle(
            new ResendVerificationEmailCommand("alice@acme.com"),
            CancellationToken.None);

        await _emailSender.Received(1).SendVerificationEmailAsync(
            "alice@acme.com", user.Id.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendVerificationEmail_WhenEmailNotFound_DoesNothingWithoutErrorLeakage()
    {
        // Per US-002: same behavior regardless of whether email exists
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).ReturnsNull();

        Func<Task> act = async () => await CreateHandler().Handle(
            new ResendVerificationEmailCommand("unknown@acme.com"),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendVerificationEmail_WhenAlreadyVerified_DoesNothing()
    {
        User user = MakeUnverifiedUser();
        user.VerifyEmail();
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);

        await CreateHandler().Handle(
            new ResendVerificationEmailCommand("alice@acme.com"),
            CancellationToken.None);

        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
