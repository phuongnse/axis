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
    private readonly IWorkspaceRegistrationTokenStore _WorkspaceTokenStore =
        Substitute.For<IWorkspaceRegistrationTokenStore>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IWorkspaceMembershipRepository _membershipRepo = Substitute.For<IWorkspaceMembershipRepository>();
    private readonly IWorkspaceRepository _WorkspaceRepo = Substitute.For<IWorkspaceRepository>();
    private readonly IWorkspaceModuleProvisioningRepository _provisioningRepo =
        Substitute.For<IWorkspaceModuleProvisioningRepository>();

    public GetProvisioningStatusHandlerTests()
    {
        _WorkspaceTokenStore.ResolveWorkspaceIdForProvisioningPollAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
    }

    private GetProvisioningStatusHandler CreateHandler() =>
        new(
            _verificationTokenStore,
            _WorkspaceTokenStore,
            _userRepo,
            _membershipRepo,
            _WorkspaceRepo,
            _provisioningRepo);

    private void StubProvisioningPollForUser(User user)
    {
        string tokenHash = OpaqueTokenGenerator.Hash(PollToken);
        _verificationTokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
    }

    private static (User User, Workspace Workspace, WorkspaceMembership Membership) MakeVerifiedUserWithWorkspace()
    {
        Email email = Email.Create("alice@acme.com").Value!;
        Workspace Workspace = Workspace.Create(
            "Acme",
            WorkspaceSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("Alice", "Smith", email);
        user.VerifyEmail();
        WorkspaceMembership membership = WorkspaceMembership.Create(user.Id, Workspace.Id);
        return (user, Workspace, membership);
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
        Workspace Workspace = Workspace.Create(
            "Acme",
            WorkspaceSlug.Create("acme").Value!,
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
    public async Task Handle_WhenAllModulesSucceededAndWorkspaceActive_ReturnsReady()
    {
        (User user, Workspace Workspace, WorkspaceMembership membership) = MakeVerifiedUserWithWorkspace();
        IReadOnlyList<WorkspaceModuleProvisioning> modules = WorkspaceModuleNames.All
            .Select(m =>
            {
                WorkspaceModuleProvisioning row = WorkspaceModuleProvisioning.CreatePending(Workspace.Id, m);
                row.RecordSuccess();
                return row;
            })
            .ToList();

        StubProvisioningPollForUser(user);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _membershipRepo.GetByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns([membership]);
        _WorkspaceRepo.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>()).Returns(Workspace);
        _provisioningRepo.GetAllForWorkspaceAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns(modules);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(PollToken),
            CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.IsReady.Should().BeTrue();
        dto.WorkspaceStatus.Should().Be(nameof(WorkspaceStatus.Active));
        dto.Modules.Should().HaveCount(WorkspaceModuleNames.All.Count);
        dto.Modules.Should().OnlyContain(m => m.Status == nameof(WorkspaceModuleProvisioningStatus.Succeeded));
    }

    [Fact]
    public async Task Handle_WhenWorkspaceVerificationTokenResolves_ReturnsWorkspaceStatus()
    {
        Email email = Email.Create("admin@acme.com").Value!;
        Workspace Workspace = Workspace.Create(
            "Acme",
            WorkspaceSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        Workspace.BeginProvisioning();
        IReadOnlyList<WorkspaceModuleProvisioning> modules =
        [
            WorkspaceModuleProvisioning.CreatePending(Workspace.Id, WorkspaceModuleNames.DataModeling),
        ];

        string tokenHash = OpaqueTokenGenerator.Hash(PollToken);
        _WorkspaceTokenStore.ResolveWorkspaceIdForProvisioningPollAsync(
                tokenHash,
                Arg.Any<CancellationToken>())
            .Returns(Workspace.Id);
        _WorkspaceRepo.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns(Workspace);
        _provisioningRepo.GetAllForWorkspaceAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns(modules);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(PollToken),
            CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.workspaceId.Should().Be(Workspace.Id);
        dto.WorkspaceStatus.Should().Be(nameof(WorkspaceStatus.Provisioning));
        await _verificationTokenStore.DidNotReceive().ResolveUserIdForProvisioningPollAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenOnlySubsetOfModulesSucceeded_ReturnsNotReady()
    {
        (User user, Workspace Workspace, WorkspaceMembership membership) = MakeVerifiedUserWithWorkspace();
        WorkspaceModuleProvisioning succeeded = WorkspaceModuleProvisioning.CreatePending(
            Workspace.Id,
            WorkspaceModuleNames.DataModeling);
        succeeded.RecordSuccess();
        IReadOnlyList<WorkspaceModuleProvisioning> modules = [succeeded];

        StubProvisioningPollForUser(user);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _membershipRepo.GetByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns([membership]);
        _WorkspaceRepo.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>()).Returns(Workspace);
        _provisioningRepo.GetAllForWorkspaceAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns(modules);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(PollToken),
            CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenWorkspaceStillProvisioning_ReturnsNotReady()
    {
        (User user, Workspace Workspace, WorkspaceMembership membership) = MakeVerifiedUserWithWorkspace();
        Workspace.BeginProvisioning();
        IReadOnlyList<WorkspaceModuleProvisioning> modules =
        [
            WorkspaceModuleProvisioning.CreatePending(Workspace.Id, WorkspaceModuleNames.DataModeling),
        ];

        StubProvisioningPollForUser(user);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _membershipRepo.GetByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns([membership]);
        _WorkspaceRepo.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>()).Returns(Workspace);
        _provisioningRepo.GetAllForWorkspaceAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns(modules);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(PollToken),
            CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.IsReady.Should().BeFalse();
        dto.WorkspaceStatus.Should().Be(nameof(WorkspaceStatus.Provisioning));
    }
}
