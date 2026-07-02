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
    private readonly IWorkspaceRepository _workspaceRepo = Substitute.For<IWorkspaceRepository>();
    private readonly IRegistrationIdempotencyRepository _idempotencyRepo =
        Substitute.For<IRegistrationIdempotencyRepository>();
    private readonly IEmailVerificationTokenStore _verificationTokenStore =
        Substitute.For<IEmailVerificationTokenStore>();
    private readonly IWorkspaceSlugGenerator _slugGenerator = Substitute.For<IWorkspaceSlugGenerator>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public RegisterUserHandlerTests()
    {
        _slugGenerator.GenerateUniqueSlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(WorkspaceSlug.Create("alice-smith").Value);
    }

    private RegisterUserHandler CreateHandler() =>
        new(
            _userRepo,
            _workspaceRepo,
            _idempotencyRepo,
            _verificationTokenStore,
            _slugGenerator,
            _hasher,
            _emailSender,
            _uow);

    private static RegisterUserCommand ValidCommand() => new(
        FullName: "Alice Smith",
        Email: "alice@example.com",
        Password: "maple river sunrise",
        PasswordConfirmation: "maple river sunrise",
        AcceptedTermsVersion: WellKnownLegalDocuments.TermsVersion,
        AcceptedPrivacyVersion: WellKnownLegalDocuments.PrivacyVersion,
        IdempotencyKey: "idem-1");

    [Fact]
    public async Task RegisterUser_WhenCommandIsValid_CreatesUserPersonalWorkspaceAndVerificationEmail()
    {
        _userRepo.EmailExistsPlatformWideAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _idempotencyRepo.AcquireAsync("idem-1", Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);
        _hasher.Hash("maple river sunrise").Returns("hashed_password");

        Result result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _userRepo.Received(1).AddAsync(
            Arg.Is<User>(u =>
                u.Email.Value == "alice@example.com"
                && u.FullName == "Alice Smith"
                && u.PasswordHash == "hashed_password"
                && u.AcceptedTermsVersion == WellKnownLegalDocuments.TermsVersion
                && u.AcceptedPrivacyVersion == WellKnownLegalDocuments.PrivacyVersion),
            Arg.Any<CancellationToken>());
        await _workspaceRepo.Received(1).AddAsync(
            Arg.Is<Workspace>(w =>
                w.Type == WorkspaceType.Personal
                && w.OwnerUserId.HasValue
                && w.OwnerEmail.Value == "alice@example.com"
                && w.Name == "Alice Smith"
                && w.Status == WorkspaceStatus.PendingVerification),
            Arg.Any<CancellationToken>());
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
    public async Task RegisterUser_WhenIdempotencyKeyInProgress_SkipsRegistration()
    {
        _idempotencyRepo.AcquireAsync("idem-1", Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.InProgress);

        Result result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _workspaceRepo.DidNotReceive().AddAsync(Arg.Any<Workspace>(), Arg.Any<CancellationToken>());
        await _verificationTokenStore.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _idempotencyRepo.DidNotReceive().MarkCompletedAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
