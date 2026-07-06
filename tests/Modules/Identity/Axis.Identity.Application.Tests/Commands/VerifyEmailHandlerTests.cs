using Axis.Identity.Application;
using Axis.Identity.Application.Commands.VerifyEmail;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public class VerifyEmailHandlerTests
{
    private readonly IEmailVerificationTokenStore _tokenStore = Substitute.For<IEmailVerificationTokenStore>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IWorkspaceRepository _workspaceRepo = Substitute.For<IWorkspaceRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private VerifyEmailHandler CreateHandler() =>
        new(
            _tokenStore,
            _userRepo,
            _workspaceRepo,
            _uow);

    private static (User User, Workspace Workspace) MakeUnverifiedUserWithWorkspace()
    {
        Email email = Email.Create("alice@acme.com").Value;
        User user = User.Create("Alice Smith", email);
        user.SetPasswordHash("hashed");
        Workspace workspace = Workspace.CreatePersonal(
            "Alice Smith",
            WorkspaceSlug.Create("alice-smith").Value,
            email,
            user.Id);
        return (user, workspace);
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenIsValid_VerifiesUserActivatesWorkspaceAndRoutesToDashboard()
    {
        (User user, Workspace workspace) = MakeUnverifiedUserWithWorkspace();
        string rawToken = "valid-raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);

        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.Valid, user.Id));
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _workspaceRepo.GetPersonalByOwnerUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(workspace);

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(user.Id);
        result.Value.workspaceId.Should().Be(workspace.Id);
        result.Value.Email.Should().Be("alice@acme.com");
        result.Value.NextStep.Should().Be(VerifyEmailNextStep.Dashboard);
        user.IsEmailVerified.Should().BeTrue();
        workspace.Status.Should().Be(WorkspaceStatus.Active);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenIsUnknown_ReturnsBusinessRuleWithoutSaving()
    {
        string rawToken = "unknown-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.NotFound, null));

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand(rawToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.ProblemCode.Should().Be(IdentityProblemCodes.EmailVerificationInvalidToken);
        result.Error.Should().Contain("Invalid verification link");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenExpired_ReturnsExpiredMessage()
    {
        string rawToken = "expired-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.Expired, Guid.NewGuid()));

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand(rawToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ProblemCode.Should().Be(IdentityProblemCodes.EmailVerificationExpiredToken);
        result.Error.Should().Contain("expired");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
