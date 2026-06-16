using Axis.Identity.Application.Commands.RetryTenantProvisioning;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.Provisioning;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public class RetryTenantProvisioningHandlerTests
{
    private readonly IEmailVerificationTokenStore _tokenStore =
        Substitute.For<IEmailVerificationTokenStore>();
    private readonly ITeamAccountRegistrationTokenStore _teamAccountTokenStore =
        Substitute.For<ITeamAccountRegistrationTokenStore>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly ITeamAccountMembershipRepository _membershipRepo = Substitute.For<ITeamAccountMembershipRepository>();
    private readonly ITeamAccountRepository _teamAccountRepo = Substitute.For<ITeamAccountRepository>();
    private readonly ITenantModuleProvisioningRepository _provisioningRepo =
        Substitute.For<ITenantModuleProvisioningRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public RetryTenantProvisioningHandlerTests()
    {
        _teamAccountTokenStore.ResolveTeamAccountIdForProvisioningPollAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
    }

    private RetryTenantProvisioningHandler CreateHandler() =>
        new(
            _tokenStore,
            _teamAccountTokenStore,
            _userRepo,
            _membershipRepo,
            _teamAccountRepo,
            _provisioningRepo,
            _uow);

    private static (User User, TeamAccount TeamAccount, TeamAccountMembership Membership) MakeVerifiedUserWithTeamAccount()
    {
        Email email = Email.Create("alice@acme.com").Value!;
        TeamAccount teamAccount = TeamAccount.Create(
            "Acme",
            TeamAccountSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("Alice", "Smith", email);
        user.SetPasswordHash("hashed");
        user.VerifyEmail();
        user.ClearDomainEvents();
        TeamAccountMembership membership = TeamAccountMembership.Create(user.Id, teamAccount.Id);
        return (user, teamAccount, membership);
    }

    private static TeamAccount FailProvisioning(TeamAccount teamAccount)
    {
        teamAccount.BeginProvisioning();
        teamAccount.MarkProvisioningFailed();
        return teamAccount;
    }

    [Fact]
    public async Task RetryProvisioning_WhenTokenIsBlank_ReturnsBusinessRuleFailure()
    {
        Result result = await CreateHandler().Handle(
            new RetryTenantProvisioningCommand("   "),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenTokenCannotBeResolved_ReturnsNotFound()
    {
        string rawToken = "unknown-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        Result result = await CreateHandler().Handle(
            new RetryTenantProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenUserNotVerified_ReturnsBusinessRuleFailure()
    {
        Email email = Email.Create("bob@acme.com").Value!;
        TeamAccount teamAccount = TeamAccount.Create(
            "Acme",
            TeamAccountSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("Bob", "Smith", email);
        string rawToken = "raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);

        Result result = await CreateHandler().Handle(
            new RetryTenantProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenTeamAccountMissing_ReturnsNotFound()
    {
        (User user, TeamAccount _, TeamAccountMembership membership) = MakeVerifiedUserWithTeamAccount();
        string rawToken = "raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id).Returns(membership);
        _teamAccountRepo.GetByIdAsync(membership.TeamAccountId).Returns((TeamAccount?)null);

        Result result = await CreateHandler().Handle(
            new RetryTenantProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenTeamAccountNotFailed_IsIdempotentSuccessWithoutChanges()
    {
        (User user, TeamAccount teamAccount, TeamAccountMembership membership) = MakeVerifiedUserWithTeamAccount();
        string rawToken = "raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id).Returns(membership);
        _teamAccountRepo.GetByIdAsync(membership.TeamAccountId).Returns(teamAccount);

        Result result = await CreateHandler().Handle(
            new RetryTenantProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        teamAccount.Status.Should().Be(TeamAccountStatus.Active);
        await _provisioningRepo.DidNotReceive()
            .GetAllForTeamAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenFailed_ResetsFailedModulesAndRequeues()
    {
        (User user, TeamAccount teamAccount, TeamAccountMembership membership) = MakeVerifiedUserWithTeamAccount();
        FailProvisioning(teamAccount);

        TenantModuleProvisioning failedModule =
            TenantModuleProvisioning.CreatePending(teamAccount.Id, "data-modeling");
        failedModule.RecordFailure("boom", attemptCount: 3);
        TenantModuleProvisioning succeededModule =
            TenantModuleProvisioning.CreatePending(teamAccount.Id, "workflow-builder");
        succeededModule.RecordSuccess();

        string rawToken = "raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id).Returns(membership);
        _teamAccountRepo.GetByIdAsync(membership.TeamAccountId).Returns(teamAccount);
        _provisioningRepo.GetAllForTeamAccountAsync(membership.TeamAccountId, Arg.Any<CancellationToken>())
            .Returns([failedModule, succeededModule]);

        Result result = await CreateHandler().Handle(
            new RetryTenantProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        failedModule.Status.Should().Be(TenantModuleProvisioningStatus.Pending);
        failedModule.AttemptCount.Should().Be(0);
        failedModule.LastError.Should().BeNull();

        succeededModule.Status.Should().Be(TenantModuleProvisioningStatus.Succeeded);

        teamAccount.Status.Should().Be(TeamAccountStatus.Provisioning);
        teamAccount.DomainEvents.Should().ContainSingle(e => e is TeamAccountVerified)
            .Which.Should().BeOfType<TeamAccountVerified>()
            .Which.TeamAccountId.Should().Be(teamAccount.Id);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenTeamAccountTokenResolves_RequeuesFailedTeamAccount()
    {
        Email email = Email.Create("admin@acme.com").Value!;
        TeamAccount teamAccount = TeamAccount.Create(
            "Acme",
            TeamAccountSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        FailProvisioning(teamAccount);
        TenantModuleProvisioning failedModule =
            TenantModuleProvisioning.CreatePending(teamAccount.Id, "data-modeling");
        failedModule.RecordFailure("boom", attemptCount: 3);

        string rawToken = "team-account-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _teamAccountTokenStore.ResolveTeamAccountIdForProvisioningPollAsync(
                tokenHash,
                Arg.Any<CancellationToken>())
            .Returns(teamAccount.Id);
        _teamAccountRepo.GetByIdAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns(teamAccount);
        _provisioningRepo.GetAllForTeamAccountAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns([failedModule]);

        Result result = await CreateHandler().Handle(
            new RetryTenantProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        failedModule.Status.Should().Be(TenantModuleProvisioningStatus.Pending);
        teamAccount.Status.Should().Be(TeamAccountStatus.Provisioning);
        await _tokenStore.DidNotReceive().ResolveUserIdForProvisioningPollAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
