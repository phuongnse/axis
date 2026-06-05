using Axis.Identity.Application.Commands.RegisterOrganization;
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

public class RegisterOrganizationHandlerTests
{
    private readonly IOrganizationRepository _orgRepo = Substitute.For<IOrganizationRepository>();
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IRegistrationIdempotencyRepository _idempotencyRepo =
        Substitute.For<IRegistrationIdempotencyRepository>();
    private readonly IOrganizationRegistrationTokenStore _organizationTokenStore =
        Substitute.For<IOrganizationRegistrationTokenStore>();
    private readonly IOrganizationSlugGenerator _slugGenerator = Substitute.For<IOrganizationSlugGenerator>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private RegisterOrganizationHandler CreateHandler() =>
        new(
            _orgRepo,
            _planRepo,
            _roleRepo,
            _idempotencyRepo,
            _organizationTokenStore,
            _slugGenerator,
            _emailSender,
            _uow);

    private static RegisterOrganizationCommand ValidCommand() => new(
        OrgName: "Acme Corp",
        OrganizationContactEmail: "admin@acme.com",
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
            .Returns(OrganizationSlug.Create("acme-corp").Value!);
    }

    [Fact]
    public async Task RegisterOrganization_WhenCommandIsValid_CreatesPendingOrgAndSystemRoles()
    {
        SetupDefaultPlan();
        SetupDefaultSlug();
        _idempotencyRepo.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await _orgRepo.Received(1).AddAsync(
            Arg.Is<Organization>(o =>
                o.Status == OrganizationStatus.PendingVerification
                && o.OwnerEmail.Value == "admin@acme.com"
                && o.AcceptedTermsVersion == WellKnownLegalDocuments.TermsVersion
                && o.AcceptedPrivacyVersion == WellKnownLegalDocuments.PrivacyVersion),
            Arg.Any<CancellationToken>());
        await _roleRepo.Received(4).AddAsync(Arg.Any<Role>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterOrganization_WhenCommandIsValid_SendsVerificationEmail()
    {
        SetupDefaultPlan();
        SetupDefaultSlug();
        _idempotencyRepo.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await _organizationTokenStore.Received(1).CreateVerificationAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _emailSender.Received(1).SendVerificationEmailAsync(
            "admin@acme.com", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterOrganization_WhenEmailIsInvalid_ReturnsFailureWithoutCreatingAnything()
    {
        RegisterOrganizationCommand command = ValidCommand() with
        {
            OrganizationContactEmail = "not-an-email",
        };

        Shared.Domain.Primitives.Result result =
            await CreateHandler().Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _orgRepo.DidNotReceive().AddAsync(Arg.Any<Organization>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterOrganization_WhenHandled_DelegatesSlugGenerationToGenerator()
    {
        SetupDefaultPlan();
        SetupDefaultSlug();
        _idempotencyRepo.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        // Uniqueness/collision retry lives in OrganizationSlugGenerator (covered by its own
        // unit tests); the handler only delegates to it.
        await _slugGenerator.Received(1).GenerateUniqueSlugAsync("Acme Corp", Arg.Any<CancellationToken>());
        await _orgRepo.Received(1).AddAsync(Arg.Any<Organization>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterOrganization_WhenIdempotencyKeyAlreadyCompleted_SkipsRegistration()
    {
        _idempotencyRepo.AcquireAsync("idem-1", Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.AlreadyCompleted);

        RegisterOrganizationCommand command = ValidCommand() with { IdempotencyKey = "idem-1" };
        await CreateHandler().Handle(command, CancellationToken.None);

        await _orgRepo.DidNotReceive().AddAsync(Arg.Any<Organization>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterOrganization_WhenSaveFails_MarksIdempotencyFailedSoRetryCanProceed()
    {
        SetupDefaultPlan();
        SetupDefaultSlug();
        _idempotencyRepo.AcquireAsync("idem-retry", Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<int>(new InvalidOperationException("db down")));

        RegisterOrganizationCommand command = ValidCommand() with { IdempotencyKey = "idem-retry" };

        Func<Task> act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _idempotencyRepo.Received(1).MarkFailedAsync("idem-retry", Arg.Any<CancellationToken>());
        await _idempotencyRepo.DidNotReceive().MarkCompletedAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
