using Axis.Identity.Application.Commands.RegisterTeamAccount;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Legal;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class RegisterTeamAccountHandlerTests
{
    private readonly ITeamAccountRepository _teamAccountRepo = Substitute.For<ITeamAccountRepository>();
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IRegistrationIdempotencyRepository _idempotencyRepo =
        Substitute.For<IRegistrationIdempotencyRepository>();
    private readonly ITeamAccountRegistrationTokenStore _teamAccountTokenStore =
        Substitute.For<ITeamAccountRegistrationTokenStore>();
    private readonly ITeamAccountSlugGenerator _slugGenerator = Substitute.For<ITeamAccountSlugGenerator>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private RegisterTeamAccountHandler CreateHandler() =>
        new(
            _teamAccountRepo,
            _planRepo,
            _roleRepo,
            _idempotencyRepo,
            _teamAccountTokenStore,
            _slugGenerator,
            _emailSender,
            _uow);

    private static RegisterTeamAccountCommand ValidCommand() => new(
        TeamAccountName: "Acme Corp",
        TeamContactEmail: "admin@acme.com",
        AcceptedTermsVersion: WellKnownLegalDocuments.TermsVersion,
        AcceptedPrivacyVersion: WellKnownLegalDocuments.PrivacyVersion);

    private void SetupDefaultPlan()
    {
        SubscriptionPlan freePlan = SubscriptionPlan.Create(
            WellKnownSubscriptionPlans.FreeId,
            "Free",
            "free",
            0,
            3,
            1_000,
            3,
            500,
            true,
            true);
        _planRepo.GetByIdAsync(WellKnownSubscriptionPlans.FreeId, Arg.Any<CancellationToken>())
            .Returns(freePlan);
        _planRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(freePlan);
    }

    private void SetupDefaultSlug()
    {
        _slugGenerator.GenerateUniqueSlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TeamAccountSlug.Create("acme-corp").Value!);
    }

    [Fact]
    public async Task RegisterTeamAccount_WhenCommandIsValid_CreatesPendingTeamAccountAndSystemRoles()
    {
        SetupDefaultPlan();
        SetupDefaultSlug();
        _idempotencyRepo.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await _teamAccountRepo.Received(1).AddAsync(
            Arg.Is<TeamAccount>(o =>
                o.Status == TeamAccountStatus.PendingVerification
                && o.OwnerEmail.Value == "admin@acme.com"
                && o.AcceptedTermsVersion == WellKnownLegalDocuments.TermsVersion
                && o.AcceptedPrivacyVersion == WellKnownLegalDocuments.PrivacyVersion),
            Arg.Any<CancellationToken>());
        await _roleRepo.Received(4).AddAsync(Arg.Any<Role>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterTeamAccount_WhenCommandIsValid_SendsVerificationEmail()
    {
        SetupDefaultPlan();
        SetupDefaultSlug();
        _idempotencyRepo.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await _teamAccountTokenStore.Received(1).CreateVerificationAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _emailSender.Received(1).SendVerificationEmailAsync(
            "admin@acme.com", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterTeamAccount_WhenEmailIsInvalid_ReturnsFailureWithoutCreatingAnything()
    {
        RegisterTeamAccountCommand command = ValidCommand() with
        {
            TeamContactEmail = "not-an-email",
        };

        Shared.Domain.Primitives.Result result =
            await CreateHandler().Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _teamAccountRepo.DidNotReceive().AddAsync(Arg.Any<TeamAccount>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterTeamAccount_WhenHandled_DelegatesSlugGenerationToGenerator()
    {
        SetupDefaultPlan();
        SetupDefaultSlug();
        _idempotencyRepo.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        // Uniqueness/collision retry lives in TeamAccountSlugGenerator (covered by its own
        // unit tests); the handler only delegates to it.
        await _slugGenerator.Received(1).GenerateUniqueSlugAsync("Acme Corp", Arg.Any<CancellationToken>());
        await _teamAccountRepo.Received(1).AddAsync(Arg.Any<TeamAccount>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterTeamAccount_WhenIdempotencyKeyAlreadyCompleted_SkipsRegistration()
    {
        _idempotencyRepo.AcquireAsync("idem-1", Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.AlreadyCompleted);

        RegisterTeamAccountCommand command = ValidCommand() with { IdempotencyKey = "idem-1" };
        await CreateHandler().Handle(command, CancellationToken.None);

        await _teamAccountRepo.DidNotReceive().AddAsync(Arg.Any<TeamAccount>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterTeamAccount_WhenSaveFails_MarksIdempotencyFailedSoRetryCanProceed()
    {
        SetupDefaultPlan();
        SetupDefaultSlug();
        _idempotencyRepo.AcquireAsync("idem-retry", Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<int>(new InvalidOperationException("db down")));

        RegisterTeamAccountCommand command = ValidCommand() with { IdempotencyKey = "idem-retry" };

        Func<Task> act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _idempotencyRepo.Received(1).MarkFailedAsync("idem-retry", Arg.Any<CancellationToken>());
        await _idempotencyRepo.DidNotReceive().MarkCompletedAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
