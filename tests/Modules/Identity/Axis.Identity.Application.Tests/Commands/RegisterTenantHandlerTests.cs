using Axis.Identity.Application.Commands.RegisterTenant;
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

public class RegisterTenantHandlerTests
{
    private readonly ITenantRepository _tenantRepo = Substitute.For<ITenantRepository>();
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IRegistrationIdempotencyRepository _idempotencyRepo =
        Substitute.For<IRegistrationIdempotencyRepository>();
    private readonly ITenantRegistrationTokenStore _tenantTokenStore =
        Substitute.For<ITenantRegistrationTokenStore>();
    private readonly ITenantSlugGenerator _slugGenerator = Substitute.For<ITenantSlugGenerator>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private RegisterTenantHandler CreateHandler() =>
        new(
            _tenantRepo,
            _planRepo,
            _roleRepo,
            _idempotencyRepo,
            _tenantTokenStore,
            _slugGenerator,
            _emailSender,
            _uow);

    private static RegisterTenantCommand ValidCommand() => new(
        TenantName: "Acme Corp",
        TenantContactEmail: "admin@acme.com",
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
            .Returns(TenantSlug.Create("acme-corp").Value!);
    }

    [Fact]
    public async Task RegisterTenant_WhenCommandIsValid_CreatesPendingTenantAndSystemRoles()
    {
        SetupDefaultPlan();
        SetupDefaultSlug();
        _idempotencyRepo.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await _tenantRepo.Received(1).AddAsync(
            Arg.Is<Tenant>(o =>
                o.Status == TenantStatus.PendingVerification
                && o.OwnerEmail.Value == "admin@acme.com"
                && o.AcceptedTermsVersion == WellKnownLegalDocuments.TermsVersion
                && o.AcceptedPrivacyVersion == WellKnownLegalDocuments.PrivacyVersion),
            Arg.Any<CancellationToken>());
        await _roleRepo.Received(4).AddAsync(Arg.Any<Role>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterTenant_WhenCommandIsValid_SendsVerificationEmail()
    {
        SetupDefaultPlan();
        SetupDefaultSlug();
        _idempotencyRepo.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await _tenantTokenStore.Received(1).CreateVerificationAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _emailSender.Received(1).SendVerificationEmailAsync(
            "admin@acme.com", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterTenant_WhenEmailIsInvalid_ReturnsFailureWithoutCreatingAnything()
    {
        RegisterTenantCommand command = ValidCommand() with
        {
            TenantContactEmail = "not-an-email",
        };

        Shared.Domain.Primitives.Result result =
            await CreateHandler().Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _tenantRepo.DidNotReceive().AddAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterTenant_WhenHandled_DelegatesSlugGenerationToGenerator()
    {
        SetupDefaultPlan();
        SetupDefaultSlug();
        _idempotencyRepo.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        // Uniqueness/collision retry lives in TenantSlugGenerator (covered by its own
        // unit tests); the handler only delegates to it.
        await _slugGenerator.Received(1).GenerateUniqueSlugAsync("Acme Corp", Arg.Any<CancellationToken>());
        await _tenantRepo.Received(1).AddAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterTenant_WhenIdempotencyKeyAlreadyCompleted_SkipsRegistration()
    {
        _idempotencyRepo.AcquireAsync("idem-1", Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.AlreadyCompleted);

        RegisterTenantCommand command = ValidCommand() with { IdempotencyKey = "idem-1" };
        await CreateHandler().Handle(command, CancellationToken.None);

        await _tenantRepo.DidNotReceive().AddAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterTenant_WhenSaveFails_MarksIdempotencyFailedSoRetryCanProceed()
    {
        SetupDefaultPlan();
        SetupDefaultSlug();
        _idempotencyRepo.AcquireAsync("idem-retry", Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<int>(new InvalidOperationException("db down")));

        RegisterTenantCommand command = ValidCommand() with { IdempotencyKey = "idem-retry" };

        Func<Task> act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _idempotencyRepo.Received(1).MarkFailedAsync("idem-retry", Arg.Any<CancellationToken>());
        await _idempotencyRepo.DidNotReceive().MarkCompletedAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
