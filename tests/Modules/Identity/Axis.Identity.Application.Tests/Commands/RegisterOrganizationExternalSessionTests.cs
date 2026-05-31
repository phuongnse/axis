using Axis.Identity.Application.Commands.RegisterOrganization;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public class RegisterOrganizationExternalSessionTests
{
    private readonly IOrganizationRepository _orgRepo = Substitute.For<IOrganizationRepository>();
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IRegistrationIdempotencyRepository _idempotencyRepo =
        Substitute.For<IRegistrationIdempotencyRepository>();
    private readonly IExternalRegistrationSessionRepository _externalSessionRepo =
        Substitute.For<IExternalRegistrationSessionRepository>();
    private readonly IUserExternalLoginRepository _externalLoginRepo =
        Substitute.For<IUserExternalLoginRepository>();
    private readonly ITenantModuleProvisioningRepository _provisioningRepo =
        Substitute.For<ITenantModuleProvisioningRepository>();
    private readonly IEmailVerificationTokenStore _verificationTokenStore =
        Substitute.For<IEmailVerificationTokenStore>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private RegisterOrganizationHandler CreateHandler() =>
        new(
            _orgRepo,
            _planRepo,
            _userRepo,
            _roleRepo,
            _idempotencyRepo,
            _externalSessionRepo,
            _externalLoginRepo,
            _provisioningRepo,
            _verificationTokenStore,
            _hasher,
            _emailSender,
            _uow);

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

    [Fact]
    public async Task RegisterOrganization_WithExternalSession_CreatesPasswordlessUserAndExternalLogin()
    {
        SetupDefaultPlan();
        Email email = Email.Create("oauth@example.com").Value!;
        ExternalRegistrationSession session = ExternalRegistrationSession.Create(
            ExternalIdentityProvider.Google,
            "google-123",
            email,
            "OAuth User");

        _orgRepo.SlugExistsAsync(Arg.Any<OrganizationSlug>()).Returns(false);
        _userRepo.EmailExistsPlatformWideAsync(Arg.Any<Email>()).Returns(false);
        _externalSessionRepo.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _idempotencyRepo.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);

        RegisterOrganizationCommand command = new(
            OrgName: "OAuth Org",
            AdminFirstName: "OAuth",
            AdminLastName: "User",
            AdminEmail: "oauth@example.com",
            Password: string.Empty,
            PasswordConfirmation: string.Empty,
            ExternalRegistrationSessionId: session.Id,
            AcceptedTermsVersion: "1.0",
            AcceptedPrivacyVersion: "1.0");

        await CreateHandler().Handle(command, CancellationToken.None);

        await _externalLoginRepo.Received(1).AddAsync(
            Arg.Is<UserExternalLogin>(login =>
                login.Provider == ExternalIdentityProvider.Google
                && login.ProviderKey == "google-123"),
            Arg.Any<CancellationToken>());
        await _userRepo.Received(1).AddAsync(
            Arg.Is<User>(user =>
                user.PasswordHash == null
                && user.IsEmailVerified
                && user.AcceptedTermsVersion == "1.0"),
            Arg.Any<CancellationToken>());
        await _provisioningRepo.Received(1).AddRangeAsync(
            Arg.Any<IEnumerable<TenantModuleProvisioning>>(),
            Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        _hasher.DidNotReceive().Hash(Arg.Any<string>());
    }
}
