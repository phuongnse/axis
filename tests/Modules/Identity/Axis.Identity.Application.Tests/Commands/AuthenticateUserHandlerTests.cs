using Axis.Identity.Application.Commands.AuthenticateUser;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class AuthenticateUserHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();

    private AuthenticateUserHandler CreateHandler() =>
        new(_userRepo, _roleRepo, _hasher, _uow);

    private static User MakeActiveUser(string email = "alice@acme.com")
    {
        User user = User.Create("Alice", "Smith", Email.Create(email).Value, OrgId);
        user.SetPasswordHash("hashed");
        user.VerifyEmail();
        user.AssignRole(RoleId);
        return user;
    }

    private static Role MakeRole(string[] permissions) =>
        Role.CreateSystem("Admin", OrgId, permissions);

    [Fact]
    public async Task Happy_path_returns_success_with_user_info_and_permissions()
    {
        User user = MakeActiveUser();
        Role role = MakeRole(["workflow:definition:read", "users:read"]);
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);
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
    public async Task Wrong_password_returns_failure_and_increments_failed_logins()
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
    public async Task Unknown_email_returns_generic_invalid_credentials()
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
    public async Task Locked_out_account_returns_account_locked()
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
    public async Task Deactivated_account_returns_account_deactivated()
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
    public async Task Unverified_email_returns_email_not_verified()
    {
        User user = User.Create("Alice", "Smith", Email.Create("alice@acme.com").Value, OrgId);
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
    public async Task Invalid_email_format_returns_invalid_credentials()
    {
        Result<AuthenticationResult> result = await CreateHandler().Handle(
            new AuthenticateUserCommand("not-an-email", "password"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeFalse();
        result.Value.FailureReason.Should().Be(AuthFailureReason.InvalidCredentials);
    }

    [Fact]
    public async Task Fifth_failed_attempt_triggers_lockout()
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
