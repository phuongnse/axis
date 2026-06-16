using Axis.Identity.Application.Commands.VerifyEmail;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.Legal;
using Axis.Identity.Domain.Provisioning;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class VerifyEmailHandlerTests
{
    private readonly IEmailVerificationTokenStore _tokenStore = Substitute.For<IEmailVerificationTokenStore>();
    private readonly ITeamAccountRegistrationTokenStore _teamAccountTokenStore =
        Substitute.For<ITeamAccountRegistrationTokenStore>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly ITeamAccountMembershipRepository _membershipRepo = Substitute.For<ITeamAccountMembershipRepository>();
    private readonly ITeamAccountRepository _teamAccountRepo = Substitute.For<ITeamAccountRepository>();
    private readonly ITenantModuleProvisioningRepository _provisioningRepo =
        Substitute.For<ITenantModuleProvisioningRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public VerifyEmailHandlerTests()
    {
        _teamAccountTokenStore.ResolveVerificationAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(ErrorCodes.BusinessRule, "Invalid verification link."));
    }

    private VerifyEmailHandler CreateHandler() =>
        new(
            _tokenStore,
            _teamAccountTokenStore,
            _userRepo,
            _membershipRepo,
            _teamAccountRepo,
            _provisioningRepo,
            _roleRepo,
            _uow);

    private static (User User, TeamAccount TeamAccount, TeamAccountMembership Membership) MakeUnverifiedUserWithTeamAccount()
    {
        Email email = Email.Create("alice@acme.com").Value!;
        TeamAccount teamAccount = TeamAccount.RegisterForContactVerification(
            "Acme",
            TeamAccountSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId,
            WellKnownLegalDocuments.TermsVersion,
            WellKnownLegalDocuments.PrivacyVersion);
        User user = User.Create("Alice", "Smith", email);
        user.SetPasswordHash("hashed");
        TeamAccountMembership membership = TeamAccountMembership.Create(user.Id, teamAccount.Id);
        return (user, teamAccount, membership);
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenIsValid_VerifiesEmailAndRaisesDomainEvent()
    {
        (User user, TeamAccount teamAccount, TeamAccountMembership membership) = MakeUnverifiedUserWithTeamAccount();
        Role adminRole = Role.CreateSystem("Admin", teamAccount.Id, ["users:read"]);
        string rawToken = "valid-raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);

        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.Valid, user.Id));
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _teamAccountRepo.GetByIdAsync(teamAccount.Id).Returns(teamAccount);
        _roleRepo.GetByNameAsync("Admin", teamAccount.Id).Returns(adminRole);

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be("alice@acme.com");
        result.Value.NextStep.Should().Be(VerifyEmailNextStep.WorkspaceProvisioning);
        user.IsEmailVerified.Should().BeTrue();
        teamAccount.Status.Should().Be(TeamAccountStatus.Provisioning);

        await _provisioningRepo.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<TenantModuleProvisioning>>(rows => rows.Count() == TenantModuleNames.All.Count),
            Arg.Any<CancellationToken>());

        teamAccount.DomainEvents.Should().ContainSingle(e => e is TeamAccountVerified)
            .Which.Should().BeOfType<TeamAccountVerified>()
            .Which.TeamAccountId.Should().Be(teamAccount.Id);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _tokenStore.DidNotReceive().InvalidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_WhenTeamAccountContactTokenIsValid_StartsProvisioningAndReturnsSetupToken()
    {
        Email email = Email.Create("admin@acme.com").Value!;
        TeamAccount teamAccount = TeamAccount.RegisterForContactVerification(
            "Acme",
            TeamAccountSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId,
            WellKnownLegalDocuments.TermsVersion,
            WellKnownLegalDocuments.PrivacyVersion);
        string rawToken = "team-account-contact-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);

        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.NotFound, null));
        _teamAccountTokenStore.ResolveVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(Result.Success(teamAccount.Id));
        _teamAccountRepo.GetByIdAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns(teamAccount);

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().BeNull();
        result.Value.TeamAccountId.Should().Be(teamAccount.Id);
        result.Value.NextStep.Should().Be(VerifyEmailNextStep.RegisterUser);
        result.Value.TeamAccountSetupToken.Should().NotBeNullOrWhiteSpace();
        teamAccount.Status.Should().Be(TeamAccountStatus.Provisioning);

        await _provisioningRepo.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<TenantModuleProvisioning>>(rows => rows.Count() == TenantModuleNames.All.Count),
            Arg.Any<CancellationToken>());
        await _teamAccountTokenStore.Received(1).CreateFirstUserSetupAsync(
            teamAccount.Id,
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenIsUnknown_DoesNotSaveOrRaiseEvent()
    {
        string tokenHash = OpaqueTokenGenerator.Hash("unknown-token");
        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.NotFound, null));

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand("unknown-token"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
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
        result.Error.Should().Contain("expired");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenAlreadyUsed_ReturnsBusinessRuleFailure()
    {
        (User user, TeamAccount _, TeamAccountMembership _) = MakeUnverifiedUserWithTeamAccount();
        string rawToken = "used-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.AlreadyUsed, user.Id));

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand(rawToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("already been used");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_WhenUserAlreadyVerified_ReturnsBusinessRuleFailure()
    {
        (User user, TeamAccount _, TeamAccountMembership _) = MakeUnverifiedUserWithTeamAccount();
        user.VerifyEmail();
        user.ClearDomainEvents();
        string rawToken = "still-valid-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.Valid, user.Id));
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand(rawToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("already been used");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
