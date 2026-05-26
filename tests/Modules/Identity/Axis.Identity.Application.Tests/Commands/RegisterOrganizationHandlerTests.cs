using Axis.Identity.Application.Commands.RegisterOrganization;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
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
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IRegistrationIdempotencyRepository _idempotencyRepo =
        Substitute.For<IRegistrationIdempotencyRepository>();
    private readonly IEmailVerificationTokenStore _verificationTokenStore =
        Substitute.For<IEmailVerificationTokenStore>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private RegisterOrganizationHandler CreateHandler() =>
        new(_orgRepo, _planRepo, _userRepo, _roleRepo, _idempotencyRepo, _verificationTokenStore, _hasher, _emailSender, _uow);

    private static RegisterOrganizationCommand ValidCommand() => new(
        OrgName: "Acme Corp",
        AdminFirstName: "Alice",
        AdminLastName: "Smith",
        AdminEmail: "alice@acme.com",
        Password: "SecurePass1",
        PasswordConfirmation: "SecurePass1");

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
    public async Task RegisterOrganization_WhenCommandIsValid_CreatesOrgUserAndSystemRoles()
    {
        SetupDefaultPlan();
        _orgRepo.SlugExistsAsync(Arg.Any<OrganizationSlug>()).Returns(false);
        _userRepo.EmailExistsPlatformWideAsync(Arg.Any<Email>()).Returns(false);
        _idempotencyRepo.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await _orgRepo.Received(1).AddAsync(Arg.Any<Organization>(), Arg.Any<CancellationToken>());
        await _userRepo.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _roleRepo.Received(4).AddAsync(Arg.Any<Role>(), Arg.Any<CancellationToken>()); // 4 system roles
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterOrganization_WhenCommandIsValid_SendsVerificationEmail()
    {
        SetupDefaultPlan();
        _orgRepo.SlugExistsAsync(Arg.Any<OrganizationSlug>()).Returns(false);
        _userRepo.EmailExistsPlatformWideAsync(Arg.Any<Email>()).Returns(false);
        _idempotencyRepo.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await _verificationTokenStore.Received(1).CreateAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _emailSender.Received(1).SendVerificationEmailAsync(
            "alice@acme.com", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterOrganization_WhenEmailAlreadyExists_ReturnsSuccessWithoutCreatingAnything()
    {
        // Per US-001: no information leakage — same response whether email exists or not
        _userRepo.EmailExistsPlatformWideAsync(Arg.Any<Email>()).Returns(true);

        Func<Task<Shared.Domain.Primitives.Result>> act = async () => await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _orgRepo.DidNotReceive().AddAsync(Arg.Any<Organization>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterOrganization_WhenSlugCollides_GeneratesUniqueSlug()
    {
        SetupDefaultPlan();
        // First slug attempt collides, second should be unique
        _orgRepo.SlugExistsAsync(Arg.Any<OrganizationSlug>())
            .Returns(true, false); // first call: exists, second: free
        _userRepo.EmailExistsPlatformWideAsync(Arg.Any<Email>()).Returns(false);
        _idempotencyRepo.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await _orgRepo.Received(1).AddAsync(Arg.Any<Organization>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterOrganization_WhenCommandIsValid_HashesAndStoresPassword()
    {
        SetupDefaultPlan();
        _orgRepo.SlugExistsAsync(Arg.Any<OrganizationSlug>()).Returns(false);
        _userRepo.EmailExistsPlatformWideAsync(Arg.Any<Email>()).Returns(false);
        _idempotencyRepo.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);
        _hasher.Hash("SecurePass1").Returns("hashed_password");

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        _hasher.Received(1).Hash("SecurePass1");
        await _userRepo.Received(1).AddAsync(
            Arg.Is<User>(u => u.PasswordHash == "hashed_password"),
            Arg.Any<CancellationToken>());
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
        _orgRepo.SlugExistsAsync(Arg.Any<OrganizationSlug>()).Returns(false);
        _userRepo.EmailExistsPlatformWideAsync(Arg.Any<Email>()).Returns(false);
        _idempotencyRepo.AcquireAsync("idem-retry", Arg.Any<CancellationToken>())
            .Returns(RegistrationIdempotencyAcquireResult.Acquired);
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");
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
