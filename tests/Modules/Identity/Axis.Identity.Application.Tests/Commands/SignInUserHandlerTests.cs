using System.Reflection;
using Axis.Identity.Application.Commands.SignInUser;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class SignInUserHandlerTests
{
    private const string GenericCredentialError = "Email or password is incorrect.";

    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IWorkspaceRepository _workspaceRepo = Substitute.For<IWorkspaceRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();

    private SignInUserHandler CreateHandler() => new(_userRepo, _workspaceRepo, _hasher);

    private static User MakeVerifiedUser(string email = "alice@acme.com")
    {
        User user = User.Create("Alice Smith", Email.Create(email).Value);
        user.SetPasswordHash("hashed_password");
        user.VerifyEmail();
        return user;
    }

    private static Workspace MakeActivePersonalWorkspace(User user)
    {
        Workspace workspace = Workspace.CreatePersonal(
            user.FullName,
            WorkspaceSlug.Create("alice-smith").Value,
            user.Email,
            user.Id);
        workspace.ActivateAfterOwnerVerification();
        return workspace;
    }

    private static void SetUserStatus(User user, UserStatus status)
    {
        PropertyInfo statusProperty = typeof(User).GetProperty(nameof(User.Status))!;
        statusProperty.SetValue(user, status);
    }

    [Fact]
    public async Task SignInUser_WhenCredentialsAreValid_ReturnsSessionClaimsForActivePersonalWorkspace()
    {
        User user = MakeVerifiedUser();
        Workspace workspace = MakeActivePersonalWorkspace(user);
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(user);
        _workspaceRepo.GetPersonalByOwnerUserIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(workspace);
        _hasher.Verify("maple river sunrise", "hashed_password").Returns(true);

        Result<SignInSuccessDto> result = await CreateHandler().Handle(
            new SignInUserCommand("alice@acme.com", "maple river sunrise"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(user.Id);
        result.Value.workspaceId.Should().Be(workspace.Id);
        result.Value.Email.Should().Be("alice@acme.com");
        result.Value.FullName.Should().Be("Alice Smith");
        result.Value.NextStep.Should().Be(SignInNextStep.Dashboard);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("wrong-password")]
    [InlineData("inactive")]
    public async Task SignInUser_WhenCredentialsDoNotIdentifyActiveUser_ReturnsGenericCredentialFailure(
        string scenario)
    {
        User? user = scenario == "unknown" ? null : MakeVerifiedUser();
        if (user is not null)
        {
            _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
                .Returns(user);
            _hasher.Verify(Arg.Any<string>(), user.PasswordHash!).Returns(scenario != "wrong-password");
            if (scenario == "inactive")
                SetUserStatus(user, UserStatus.Inactive);
        }
        else
        {
            _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
                .ReturnsNull();
        }

        Result<SignInSuccessDto> result = await CreateHandler().Handle(
            new SignInUserCommand("alice@acme.com", "maple river sunrise"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Be(GenericCredentialError);
        await _workspaceRepo.DidNotReceive().GetPersonalByOwnerUserIdAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignInUser_WhenEmailIsUnverified_ReturnsVerificationRequiredWithoutSessionClaims()
    {
        User user = User.Create("Alice Smith", Email.Create("alice@acme.com").Value);
        user.SetPasswordHash("hashed_password");
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(user);
        _hasher.Verify("maple river sunrise", "hashed_password").Returns(true);

        Result<SignInSuccessDto> result = await CreateHandler().Handle(
            new SignInUserCommand("alice@acme.com", "maple river sunrise"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Be("Email verification is required before sign-in.");
        await _workspaceRepo.DidNotReceive().GetPersonalByOwnerUserIdAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignInUser_WhenPersonalWorkspaceIsUnavailable_ReturnsAccountUnavailable()
    {
        User user = MakeVerifiedUser();
        Workspace workspace = Workspace.CreatePersonal(
            user.FullName,
            WorkspaceSlug.Create("alice-smith").Value,
            user.Email,
            user.Id);
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(user);
        _hasher.Verify("maple river sunrise", "hashed_password").Returns(true);
        _workspaceRepo.GetPersonalByOwnerUserIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(workspace);

        Result<SignInSuccessDto> result = await CreateHandler().Handle(
            new SignInUserCommand("alice@acme.com", "maple river sunrise"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Be("Account is not available for sign-in.");
    }

    [Fact]
    public async Task SignInUser_WhenEmailHasOuterWhitespace_TrimsEmailBeforeLookup()
    {
        User user = MakeVerifiedUser();
        Workspace workspace = MakeActivePersonalWorkspace(user);
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(user);
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _workspaceRepo.GetPersonalByOwnerUserIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(workspace);

        await CreateHandler().Handle(
            new SignInUserCommand("  Alice@Acme.COM  ", "maple river sunrise"),
            CancellationToken.None);

        await _userRepo.Received(1).FindByEmailGloballyAsync(
            Arg.Is<Email>(email => email.Value == "alice@acme.com"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignInUser_WhenPasswordHasOuterWhitespace_VerifiesPasswordExactlyAsEntered()
    {
        User user = MakeVerifiedUser();
        Workspace workspace = MakeActivePersonalWorkspace(user);
        _userRepo.FindByEmailGloballyAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(user);
        _hasher.Verify("  maple river sunrise  ", "hashed_password").Returns(true);
        _workspaceRepo.GetPersonalByOwnerUserIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(workspace);

        Result<SignInSuccessDto> result = await CreateHandler().Handle(
            new SignInUserCommand("alice@acme.com", "  maple river sunrise  "),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _hasher.Received(1).Verify("  maple river sunrise  ", "hashed_password");
    }
}
