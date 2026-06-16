using Axis.Identity.Application.Queries.GetProvisioningStatus;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class GetProvisioningStatusHandlerTests
{
    private const string PollToken = "provisioning-poll-token";

    private readonly IEmailVerificationTokenStore _verificationTokenStore =
        Substitute.For<IEmailVerificationTokenStore>();
    private readonly ITeamAccountRegistrationTokenStore _teamAccountTokenStore =
        Substitute.For<ITeamAccountRegistrationTokenStore>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly ITeamAccountMembershipRepository _membershipRepo = Substitute.For<ITeamAccountMembershipRepository>();
    private readonly ITeamAccountRepository _teamAccountRepo = Substitute.For<ITeamAccountRepository>();
    private readonly ITenantModuleProvisioningRepository _provisioningRepo =
        Substitute.For<ITenantModuleProvisioningRepository>();

    public GetProvisioningStatusHandlerTests()
    {
        _teamAccountTokenStore.ResolveTeamAccountIdForProvisioningPollAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
    }

    private GetProvisioningStatusHandler CreateHandler() =>
        new(
            _verificationTokenStore,
            _teamAccountTokenStore,
            _userRepo,
            _membershipRepo,
            _teamAccountRepo,
            _provisioningRepo);

    private void StubProvisioningPollForUser(User user)
    {
        string tokenHash = OpaqueTokenGenerator.Hash(PollToken);
        _verificationTokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
    }

    private static (User User, TeamAccount TeamAccount, TeamAccountMembership Membership) MakeVerifiedUserWithTeamAccount()
    {
        Email email = Email.Create("alice@acme.com").Value!;
        TeamAccount teamAccount = TeamAccount.Create(
            "Acme",
            TeamAccountSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("Alice", "Smith", email);
        user.VerifyEmail();
        TeamAccountMembership membership = TeamAccountMembership.Create(user.Id, teamAccount.Id);
        return (user, teamAccount, membership);
    }

    [Fact]
    public async Task Handle_WhenTokenUnknown_ReturnsNull()
    {
        string tokenHash = OpaqueTokenGenerator.Hash("unknown");
        _verificationTokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery("unknown"),
            CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNull()
    {
        Guid userId = Guid.NewGuid();
        string tokenHash = OpaqueTokenGenerator.Hash(PollToken);
        _verificationTokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(userId);
        _userRepo.GetByIdPlatformWideAsync(userId, Arg.Any<CancellationToken>()).ReturnsNull();

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(PollToken),
            CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenEmailNotVerified_ReturnsNull()
    {
        Email email = Email.Create("bob@acme.com").Value!;
        TeamAccount teamAccount = TeamAccount.Create(
            "Acme",
            TeamAccountSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("Bob", "Smith", email);
        StubProvisioningPollForUser(user);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(PollToken),
            CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenAllModulesSucceededAndTeamAccountActive_ReturnsReady()
    {
        (User user, TeamAccount teamAccount, TeamAccountMembership membership) = MakeVerifiedUserWithTeamAccount();
        IReadOnlyList<TenantModuleProvisioning> modules = TenantModuleNames.All
            .Select(m =>
            {
                TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(teamAccount.Id, m);
                row.RecordSuccess();
                return row;
            })
            .ToList();

        StubProvisioningPollForUser(user);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _teamAccountRepo.GetByIdAsync(teamAccount.Id, Arg.Any<CancellationToken>()).Returns(teamAccount);
        _provisioningRepo.GetAllForTeamAccountAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns(modules);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(PollToken),
            CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.IsReady.Should().BeTrue();
        dto.TeamAccountStatus.Should().Be(nameof(TeamAccountStatus.Active));
        dto.Modules.Should().HaveCount(TenantModuleNames.All.Count);
        dto.Modules.Should().OnlyContain(m => m.Status == nameof(TenantModuleProvisioningStatus.Succeeded));
    }

    [Fact]
    public async Task Handle_WhenTeamAccountVerificationTokenResolves_ReturnsTeamAccountStatus()
    {
        Email email = Email.Create("admin@acme.com").Value!;
        TeamAccount teamAccount = TeamAccount.Create(
            "Acme",
            TeamAccountSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        teamAccount.BeginProvisioning();
        IReadOnlyList<TenantModuleProvisioning> modules =
        [
            TenantModuleProvisioning.CreatePending(teamAccount.Id, TenantModuleNames.DataModeling),
        ];

        string tokenHash = OpaqueTokenGenerator.Hash(PollToken);
        _teamAccountTokenStore.ResolveTeamAccountIdForProvisioningPollAsync(
                tokenHash,
                Arg.Any<CancellationToken>())
            .Returns(teamAccount.Id);
        _teamAccountRepo.GetByIdAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns(teamAccount);
        _provisioningRepo.GetAllForTeamAccountAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns(modules);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(PollToken),
            CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.TeamAccountId.Should().Be(teamAccount.Id);
        dto.TeamAccountStatus.Should().Be(nameof(TeamAccountStatus.Provisioning));
        await _verificationTokenStore.DidNotReceive().ResolveUserIdForProvisioningPollAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenOnlySubsetOfModulesSucceeded_ReturnsNotReady()
    {
        (User user, TeamAccount teamAccount, TeamAccountMembership membership) = MakeVerifiedUserWithTeamAccount();
        TenantModuleProvisioning succeeded = TenantModuleProvisioning.CreatePending(
            teamAccount.Id,
            TenantModuleNames.DataModeling);
        succeeded.RecordSuccess();
        IReadOnlyList<TenantModuleProvisioning> modules = [succeeded];

        StubProvisioningPollForUser(user);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _teamAccountRepo.GetByIdAsync(teamAccount.Id, Arg.Any<CancellationToken>()).Returns(teamAccount);
        _provisioningRepo.GetAllForTeamAccountAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns(modules);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(PollToken),
            CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenTeamAccountStillProvisioning_ReturnsNotReady()
    {
        (User user, TeamAccount teamAccount, TeamAccountMembership membership) = MakeVerifiedUserWithTeamAccount();
        teamAccount.BeginProvisioning();
        IReadOnlyList<TenantModuleProvisioning> modules =
        [
            TenantModuleProvisioning.CreatePending(teamAccount.Id, TenantModuleNames.DataModeling),
        ];

        StubProvisioningPollForUser(user);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _teamAccountRepo.GetByIdAsync(teamAccount.Id, Arg.Any<CancellationToken>()).Returns(teamAccount);
        _provisioningRepo.GetAllForTeamAccountAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns(modules);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(PollToken),
            CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.IsReady.Should().BeFalse();
        dto.TeamAccountStatus.Should().Be(nameof(TeamAccountStatus.Provisioning));
    }
}
