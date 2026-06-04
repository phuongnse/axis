using Axis.Identity.Application.Commands.AuthenticateUser;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class AuthenticateUserHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IOrganizationMembershipRepository _membershipRepo = Substitute.For<IOrganizationMembershipRepository>();
    private readonly IOrganizationRepository _orgRepo = Substitute.For<IOrganizationRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();

    private AuthenticateUserHandler CreateHandler()
    {
        Organization organization = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);
        _orgRepo.GetByIdAsync(OrgId, Arg.Any<CancellationToken>()).Returns(organization);
        return new(_userRepo, _membershipRepo, _orgRepo, _roleRepo, _hasher, _uow);
    }

    private static User MakeActiveUser(string email = "alice@acme.com")
    {
        User user = User.Create("Alice", "Smith", Email.Create(email).Value);
        user.SetPasswordHash("hashed");
        user.VerifyEmail();
        return user;
    }

    private static OrganizationMembership MakeMembership(User user)
    {
        OrganizationMembership membership = OrganizationMembership.Create(user.Id, OrgId);
        membership.AssignRole(RoleId);
        return membership;
    }

    private static Role MakeRole(string[] permissions) =>
        Role.CreateSystem("Admin", OrgId, permissions);

    [Fact]
    public async Task AuthenticateUser_WhenOrganizationDeleted_ReturnsOrganizationDeleted()
    {
        User user = MakeActiveUser();
        Organization organization = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);
        organization.MarkDeleted();
        OrganizationMembership membership = MakeMembership(user);
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _orgRepo.GetByIdAsync(OrgId, Arg.Any<CancellationToken>()).Returns(organization);
        _hasher.Verify("password123", "hashed").Returns(true);

        AuthenticateUserHandler handler = new(_userRepo, _membershipRepo, _orgRepo, _roleRepo, _hasher, _uow);
        Result<AuthenticationResult> result = await handler.Handle(
            new AuthenticateUserCommand("alice@acme.com", "password123"),
            CancellationToken.None);

        result.Value.Success.Should().BeFalse();
        result.Value.FailureReason.Should().Be(AuthFailureReason.OrganizationDeleted);
    }

    [Fact]
    public async Task AuthenticateUser_WhenCredentialsAreValid_ReturnsSuccessWithUserInfoAndPermissions()
    {
        User user = MakeActiveUser();
        OrganizationMembership membership = MakeMembership(user);
        Role role = MakeRole(["workflow:definition:read", "users:read"]);
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _hasher.Verify("password123", "hashed").Returns(true);
        _roleRepo.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), OrgId).Returns([role]);

        Result<AuthenticationResult> result = await CreateHandler().Handle(
            new AuthenticateUserCommand("alice@acme.com", "password123"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();
        result.Value.UserId.Should().Be(user.Id);
        result.Value.OrganizationId.Should().Be(OrgId);
        result.Value.Permissions.Should().Contain("workflow:definition:read");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>()); // reset failed logins
    }

    [Fact]
    public async Task AuthenticateUser_WhenPasswordIsWrong_ReturnsFailureAndIncrementsFailedLogins()
    {
        User user = MakeActiveUser();
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);
        _hasher.Verify("wrong", "hashed").Returns(false);

        Result<AuthenticationResult> result = await CreateHandler().Handle(
            new AuthenticateUserCommand("alice@acme.com", "wrong"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeFalse();
        result.Value.FailureReason.Should().Be(AuthFailureReason.InvalidCredentials);
        user.FailedLoginAttempts.Should().Be(1);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthenticateUser_WhenEmailIsUnknown_ReturnsInvalidCredentials()
    {
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).ReturnsNull();

        Result<AuthenticationResult> result = await CreateHandler().Handle(
            new AuthenticateUserCommand("unknown@acme.com", "password"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeFalse();
        result.Value.FailureReason.Should().Be(AuthFailureReason.InvalidCredentials);
    }

    [Fact]
    public async Task AuthenticateUser_WhenAccountIsLockedOut_ReturnsAccountLocked()
    {
        User user = MakeActiveUser();
        // Exhaust all attempts to trigger lockout
        for (int i = 0; i < 5; i++) user.RecordFailedLogin();
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);

        Result<AuthenticationResult> result = await CreateHandler().Handle(
            new AuthenticateUserCommand("alice@acme.com", "password"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeFalse();
        result.Value.FailureReason.Should().Be(AuthFailureReason.AccountLocked);
        result.Value.LockedUntil.Should().NotBeNull();
    }

    [Fact]
    public async Task AuthenticateUser_WhenAccountIsDeactivated_ReturnsAccountDeactivated()
    {
        User user = MakeActiveUser();
        user.Deactivate();
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);

        Result<AuthenticationResult> result = await CreateHandler().Handle(
            new AuthenticateUserCommand("alice@acme.com", "password"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeFalse();
        result.Value.FailureReason.Should().Be(AuthFailureReason.AccountDeactivated);
    }

    [Fact]
    public async Task AuthenticateUser_WhenEmailNotVerified_ReturnsEmailNotVerified()
    {
        User user = User.Create("Alice", "Smith", Email.Create("alice@acme.com").Value);
        user.SetPasswordHash("hashed");
        // Email NOT verified
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);

        Result<AuthenticationResult> result = await CreateHandler().Handle(
            new AuthenticateUserCommand("alice@acme.com", "password"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeFalse();
        result.Value.FailureReason.Should().Be(AuthFailureReason.EmailNotVerified);
    }

    [Fact]
    public async Task AuthenticateUser_WhenEmailFormatIsInvalid_ReturnsInvalidCredentials()
    {
        Result<AuthenticationResult> result = await CreateHandler().Handle(
            new AuthenticateUserCommand("not-an-email", "password"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeFalse();
        result.Value.FailureReason.Should().Be(AuthFailureReason.InvalidCredentials);
    }

    [Fact]
    public async Task AuthenticateUser_WhenFifthFailedAttempt_TriggersLockout()
    {
        User user = MakeActiveUser();
        for (int i = 0; i < 4; i++) user.RecordFailedLogin(); // 4 prior failures
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);
        _hasher.Verify("wrong", "hashed").Returns(false);

        Result<AuthenticationResult> result = await CreateHandler().Handle(
            new AuthenticateUserCommand("alice@acme.com", "wrong"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeFalse();
        user.IsLockedOut.Should().BeTrue();
    }
}
