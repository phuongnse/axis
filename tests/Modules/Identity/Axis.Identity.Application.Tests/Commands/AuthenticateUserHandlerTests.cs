using Axis.Identity.Application.Commands.AuthenticateUser;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using FluentValidation;
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
        var user = User.Create("Alice", "Smith", Email.Create(email).Value, OrgId);
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
        var user = MakeActiveUser();
        var role = MakeRole(["workflow:definition:read", "users:read"]);
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);
        _hasher.Verify("password123", "hashed").Returns(true);
        _roleRepo.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), OrgId).Returns([role]);

        var result = await CreateHandler().Handle(
            new AuthenticateUserCommand("alice@acme.com", "password123"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.UserId.Should().Be(user.Id);
        result.OrganizationId.Should().Be(OrgId);
        result.Permissions.Should().Contain("workflow:definition:read");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>()); // reset failed logins
    }

    [Fact]
    public async Task Wrong_password_returns_failure_and_increments_failed_logins()
    {
        var user = MakeActiveUser();
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);
        _hasher.Verify("wrong", "hashed").Returns(false);

        var result = await CreateHandler().Handle(
            new AuthenticateUserCommand("alice@acme.com", "wrong"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be(AuthFailureReason.InvalidCredentials);
        user.FailedLoginAttempts.Should().Be(1);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_email_returns_generic_invalid_credentials()
    {
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).ReturnsNull();

        var result = await CreateHandler().Handle(
            new AuthenticateUserCommand("unknown@acme.com", "password"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be(AuthFailureReason.InvalidCredentials);
    }

    [Fact]
    public async Task Locked_out_account_returns_account_locked()
    {
        var user = MakeActiveUser();
        // Exhaust all attempts to trigger lockout
        for (int i = 0; i < 5; i++) user.RecordFailedLogin();
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);

        var result = await CreateHandler().Handle(
            new AuthenticateUserCommand("alice@acme.com", "password"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be(AuthFailureReason.AccountLocked);
        result.LockedUntil.Should().NotBeNull();
    }

    [Fact]
    public async Task Deactivated_account_returns_account_deactivated()
    {
        var user = MakeActiveUser();
        user.Deactivate();
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);

        var result = await CreateHandler().Handle(
            new AuthenticateUserCommand("alice@acme.com", "password"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be(AuthFailureReason.AccountDeactivated);
    }

    [Fact]
    public async Task Unverified_email_returns_email_not_verified()
    {
        var user = User.Create("Alice", "Smith", Email.Create("alice@acme.com").Value, OrgId);
        user.SetPasswordHash("hashed");
        // Email NOT verified
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);

        var result = await CreateHandler().Handle(
            new AuthenticateUserCommand("alice@acme.com", "password"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be(AuthFailureReason.EmailNotVerified);
    }

    [Fact]
    public async Task Invalid_email_format_throws_validation_exception()
    {
        var act = async () => await CreateHandler().Handle(
            new AuthenticateUserCommand("not-an-email", "password"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Fifth_failed_attempt_triggers_lockout()
    {
        var user = MakeActiveUser();
        for (int i = 0; i < 4; i++) user.RecordFailedLogin(); // 4 prior failures
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>()).Returns(user);
        _hasher.Verify("wrong", "hashed").Returns(false);

        var result = await CreateHandler().Handle(
            new AuthenticateUserCommand("alice@acme.com", "wrong"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        user.IsLockedOut.Should().BeTrue();
    }
}
