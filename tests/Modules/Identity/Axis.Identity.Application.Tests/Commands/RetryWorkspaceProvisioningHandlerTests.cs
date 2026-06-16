using Axis.Identity.Application.Commands.RetryWorkspaceProvisioning;
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

public class RetryWorkspaceProvisioningHandlerTests
{
    private readonly IEmailVerificationTokenStore _tokenStore =
        Substitute.For<IEmailVerificationTokenStore>();
    private readonly IWorkspaceRegistrationTokenStore _WorkspaceTokenStore =
        Substitute.For<IWorkspaceRegistrationTokenStore>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IWorkspaceMembershipRepository _membershipRepo = Substitute.For<IWorkspaceMembershipRepository>();
    private readonly IWorkspaceRepository _WorkspaceRepo = Substitute.For<IWorkspaceRepository>();
    private readonly IWorkspaceModuleProvisioningRepository _provisioningRepo =
        Substitute.For<IWorkspaceModuleProvisioningRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public RetryWorkspaceProvisioningHandlerTests()
    {
        _WorkspaceTokenStore.ResolveWorkspaceIdForProvisioningPollAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
    }

    private RetryWorkspaceProvisioningHandler CreateHandler() =>
        new(
            _tokenStore,
            _WorkspaceTokenStore,
            _userRepo,
            _membershipRepo,
            _WorkspaceRepo,
            _provisioningRepo,
            _uow);

    private static (User User, Workspace Workspace, WorkspaceMembership Membership) MakeVerifiedUserWithWorkspace()
    {
        Email email = Email.Create("alice@acme.com").Value!;
        Workspace Workspace = Workspace.Create(
            "Acme",
            WorkspaceSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("Alice", "Smith", email);
        user.SetPasswordHash("hashed");
        user.VerifyEmail();
        user.ClearDomainEvents();
        WorkspaceMembership membership = WorkspaceMembership.Create(user.Id, Workspace.Id);
        return (user, Workspace, membership);
    }

    private static Workspace FailProvisioning(Workspace Workspace)
    {
        Workspace.BeginProvisioning();
        Workspace.MarkProvisioningFailed();
        return Workspace;
    }

    [Fact]
    public async Task RetryProvisioning_WhenTokenIsBlank_ReturnsBusinessRuleFailure()
    {
        Result result = await CreateHandler().Handle(
            new RetryWorkspaceProvisioningCommand("   "),
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
            new RetryWorkspaceProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenUserNotVerified_ReturnsBusinessRuleFailure()
    {
        Email email = Email.Create("bob@acme.com").Value!;
        Workspace Workspace = Workspace.Create(
            "Acme",
            WorkspaceSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("Bob", "Smith", email);
        string rawToken = "raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);

        Result result = await CreateHandler().Handle(
            new RetryWorkspaceProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenWorkspaceMissing_ReturnsNotFound()
    {
        (User user, Workspace _, WorkspaceMembership membership) = MakeVerifiedUserWithWorkspace();
        string rawToken = "raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _membershipRepo.GetByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns([membership]);
        _WorkspaceRepo.GetByIdAsync(membership.workspaceId, Arg.Any<CancellationToken>()).Returns((Workspace?)null);

        Result result = await CreateHandler().Handle(
            new RetryWorkspaceProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenWorkspaceNotFailed_IsIdempotentSuccessWithoutChanges()
    {
        (User user, Workspace Workspace, WorkspaceMembership membership) = MakeVerifiedUserWithWorkspace();
        string rawToken = "raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _membershipRepo.GetByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns([membership]);
        _WorkspaceRepo.GetByIdAsync(membership.workspaceId, Arg.Any<CancellationToken>()).Returns(Workspace);

        Result result = await CreateHandler().Handle(
            new RetryWorkspaceProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Workspace.Status.Should().Be(WorkspaceStatus.Active);
        await _provisioningRepo.DidNotReceive()
            .GetAllForWorkspaceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenFailed_ResetsFailedModulesAndRequeues()
    {
        (User user, Workspace Workspace, WorkspaceMembership membership) = MakeVerifiedUserWithWorkspace();
        FailProvisioning(Workspace);

        WorkspaceModuleProvisioning failedModule =
            WorkspaceModuleProvisioning.CreatePending(Workspace.Id, "data-modeling");
        failedModule.RecordFailure("boom", attemptCount: 3);
        WorkspaceModuleProvisioning succeededModule =
            WorkspaceModuleProvisioning.CreatePending(Workspace.Id, "workflow-builder");
        succeededModule.RecordSuccess();

        string rawToken = "raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _membershipRepo.GetByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns([membership]);
        _WorkspaceRepo.GetByIdAsync(membership.workspaceId, Arg.Any<CancellationToken>()).Returns(Workspace);
        _provisioningRepo.GetAllForWorkspaceAsync(membership.workspaceId, Arg.Any<CancellationToken>())
            .Returns([failedModule, succeededModule]);

        Result result = await CreateHandler().Handle(
            new RetryWorkspaceProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        failedModule.Status.Should().Be(WorkspaceModuleProvisioningStatus.Pending);
        failedModule.AttemptCount.Should().Be(0);
        failedModule.LastError.Should().BeNull();

        succeededModule.Status.Should().Be(WorkspaceModuleProvisioningStatus.Succeeded);

        Workspace.Status.Should().Be(WorkspaceStatus.Provisioning);
        Workspace.DomainEvents.Should().ContainSingle(e => e is WorkspaceVerified)
            .Which.Should().BeOfType<WorkspaceVerified>()
            .Which.workspaceId.Should().Be(Workspace.Id);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenWorkspaceTokenResolves_RequeuesFailedWorkspace()
    {
        Email email = Email.Create("admin@acme.com").Value!;
        Workspace Workspace = Workspace.Create(
            "Acme",
            WorkspaceSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        FailProvisioning(Workspace);
        WorkspaceModuleProvisioning failedModule =
            WorkspaceModuleProvisioning.CreatePending(Workspace.Id, "data-modeling");
        failedModule.RecordFailure("boom", attemptCount: 3);

        string rawToken = "Workspace-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _WorkspaceTokenStore.ResolveWorkspaceIdForProvisioningPollAsync(
                tokenHash,
                Arg.Any<CancellationToken>())
            .Returns(Workspace.Id);
        _WorkspaceRepo.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns(Workspace);
        _provisioningRepo.GetAllForWorkspaceAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns([failedModule]);

        Result result = await CreateHandler().Handle(
            new RetryWorkspaceProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        failedModule.Status.Should().Be(WorkspaceModuleProvisioningStatus.Pending);
        Workspace.Status.Should().Be(WorkspaceStatus.Provisioning);
        await _tokenStore.DidNotReceive().ResolveUserIdForProvisioningPollAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
