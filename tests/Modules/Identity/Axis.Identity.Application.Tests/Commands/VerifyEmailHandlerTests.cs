using Axis.Identity.Application.Commands.VerifyEmail;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class VerifyEmailHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private VerifyEmailHandler CreateHandler() => new(_userRepo, _uow);

    private static User MakeUnverifiedUser()
    {
        var user = User.Create("Alice", "Smith", Email.Create("alice@acme.com").Value, OrgId);
        user.SetPasswordHash("hashed");
        return user;
    }

    [Fact]
    public async Task Happy_path_verifies_email()
    {
        var user = MakeUnverifiedUser();
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);

        await CreateHandler().Handle(
            new VerifyEmailCommand(user.Id.ToString()),
            CancellationToken.None);

        user.IsEmailVerified.Should().BeTrue();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalid_token_format_throws_validation_exception()
    {
        var act = async () => await CreateHandler().Handle(
            new VerifyEmailCommand("not-a-guid"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Invalid verification link*");
    }

    [Fact]
    public async Task Token_not_found_throws_validation_exception()
    {
        _userRepo.GetByIdPlatformWideAsync(Arg.Any<Guid>()).ReturnsNull();

        var act = async () => await CreateHandler().Handle(
            new VerifyEmailCommand(Guid.NewGuid().ToString()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Invalid verification link*");
    }

    [Fact]
    public async Task Already_verified_throws_validation_exception()
    {
        var user = MakeUnverifiedUser();
        user.VerifyEmail();
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);

        var act = async () => await CreateHandler().Handle(
            new VerifyEmailCommand(user.Id.ToString()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*already been used*");
    }
}

public class ResendVerificationEmailHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private ResendVerificationEmailHandler CreateHandler() => new(_userRepo, _emailSender);

    private static User MakeUnverifiedUser(string email = "alice@acme.com")
    {
        var user = User.Create("Alice", "Smith", Email.Create(email).Value, OrgId);
        user.SetPasswordHash("hashed");
        return user;
    }

    [Fact]
    public async Task Happy_path_resends_verification_email()
    {
        var user = MakeUnverifiedUser();
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);

        await CreateHandler().Handle(
            new ResendVerificationEmailCommand("alice@acme.com"),
            CancellationToken.None);

        await _emailSender.Received(1).SendVerificationEmailAsync(
            "alice@acme.com", user.Id.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Email_not_found_does_nothing_no_error_leakage()
    {
        // Per US-002: same behavior regardless of whether email exists
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).ReturnsNull();

        var act = async () => await CreateHandler().Handle(
            new ResendVerificationEmailCommand("unknown@acme.com"),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Already_verified_does_nothing()
    {
        var user = MakeUnverifiedUser();
        user.VerifyEmail();
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);

        await CreateHandler().Handle(
            new ResendVerificationEmailCommand("alice@acme.com"),
            CancellationToken.None);

        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
