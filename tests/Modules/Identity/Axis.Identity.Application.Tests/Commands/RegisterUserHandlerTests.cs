using Axis.Identity.Application.Commands.RegisterUser;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Legal;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public class RegisterUserHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IRegistrationIdempotencyRepository _idempotencyRepo =
        Substitute.For<IRegistrationIdempotencyRepository>();
    private readonly IEmailVerificationTokenStore _verificationTokenStore =
        Substitute.For<IEmailVerificationTokenStore>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private RegisterUserHandler CreateHandler() =>
        new(
            _userRepo,
            _idempotencyRepo,
            _verificationTokenStore,
            _hasher,
            _emailSender,
            _uow);

    private static RegisterUserCommand ValidCommand() => new(
        FirstName: "Alice",
        LastName: "Smith",
        Email: "alice@example.com",
        Password: "SecurePass1",
        PasswordConfirmation: "SecurePass1",
        AcceptedTermsVersion: WellKnownLegalDocuments.TermsVersion,
        AcceptedPrivacyVersion: WellKnownLegalDocuments.PrivacyVersion,
        IdempotencyKey: "idem-1");

    [Fact]
    public async Task RegisterUser_WhenCommandIsValid_CreatesUserAndSendsVerificationEmail()
    {
        _userRepo.EmailExistsPlatformWideAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _idempotencyRepo.AcquireAsync("idem-1", Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);
        _hasher.Hash("SecurePass1").Returns("hashed_password");

        Result result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _hasher.Received(1).Hash("SecurePass1");
        await _userRepo.Received(1).AddAsync(
            Arg.Is<User>(u =>
                u.Email.Value == "alice@example.com"
                && u.PasswordHash == "hashed_password"
                && u.AcceptedTermsVersion == WellKnownLegalDocuments.TermsVersion
                && u.AcceptedPrivacyVersion == WellKnownLegalDocuments.PrivacyVersion),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _verificationTokenStore.Received(1).CreateAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _emailSender.Received(1).SendVerificationEmailAsync(
            "alice@example.com", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _idempotencyRepo.Received(1).MarkCompletedAsync("idem-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterUser_WhenEmailAlreadyExists_ReturnsConflictWithoutCreatingUser()
    {
        _idempotencyRepo.AcquireAsync("idem-1", Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);
        _userRepo.EmailExistsPlatformWideAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(true);

        Result result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        await _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterUser_WhenIdempotencyKeyAlreadyCompleted_SkipsRegistration()
    {
        _idempotencyRepo.AcquireAsync("idem-1", Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.AlreadyCompleted);

        Result result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterUser_WhenSaveFails_MarksIdempotencyFailedSoRetryCanProceed()
    {
        _userRepo.EmailExistsPlatformWideAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _idempotencyRepo.AcquireAsync("idem-1", Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);
        _hasher.Hash(Arg.Any<string>()).Returns("hashed_password");
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<int>(new InvalidOperationException("db down")));

        Func<Task> act = async () => await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _idempotencyRepo.Received(1).MarkFailedAsync("idem-1", Arg.Any<CancellationToken>());
        await _idempotencyRepo.DidNotReceive().MarkCompletedAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
