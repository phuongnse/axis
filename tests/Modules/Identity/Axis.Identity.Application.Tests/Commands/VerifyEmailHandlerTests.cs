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
    private readonly IWorkspaceRegistrationTokenStore _WorkspaceTokenStore =
        Substitute.For<IWorkspaceRegistrationTokenStore>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IWorkspaceMembershipRepository _membershipRepo = Substitute.For<IWorkspaceMembershipRepository>();
    private readonly IWorkspaceRepository _WorkspaceRepo = Substitute.For<IWorkspaceRepository>();
    private readonly IWorkspaceModuleProvisioningRepository _provisioningRepo =
        Substitute.For<IWorkspaceModuleProvisioningRepository>();
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public VerifyEmailHandlerTests()
    {
        _WorkspaceTokenStore.ResolveVerificationAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(ErrorCodes.BusinessRule, "Invalid verification link."));
    }

    private VerifyEmailHandler CreateHandler() =>
        new(
            _tokenStore,
            _WorkspaceTokenStore,
            _userRepo,
            _membershipRepo,
            _WorkspaceRepo,
            _provisioningRepo,
            _roleRepo,
            _uow);

    private static (User User, Workspace Workspace, WorkspaceMembership Membership) MakeUnverifiedUserWithWorkspace()
    {
        Email email = Email.Create("alice@acme.com").Value!;
        Workspace Workspace = Workspace.RegisterTeamForContactVerification(
            "Acme",
            WorkspaceSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId,
            WellKnownLegalDocuments.TermsVersion,
            WellKnownLegalDocuments.PrivacyVersion);
        User user = User.Create("Alice", "Smith", email);
        user.SetPasswordHash("hashed");
        WorkspaceMembership membership = WorkspaceMembership.Create(user.Id, Workspace.Id);
        return (user, Workspace, membership);
    }

    [Fact]
    public async Task VerifyEmail_WhenTokenIsValid_VerifiesEmailAndRaisesDomainEvent()
    {
        (User user, Workspace Workspace, WorkspaceMembership membership) = MakeUnverifiedUserWithWorkspace();
        Role adminRole = Role.CreateSystem("Admin", Workspace.Id, ["users:read"]);
        string rawToken = "valid-raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);

        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.Valid, user.Id));
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _membershipRepo.GetByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns([membership]);
        _WorkspaceRepo.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>()).Returns(Workspace);
        _roleRepo.GetByNameAsync("Admin", Workspace.Id).Returns(adminRole);

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be("alice@acme.com");
        result.Value.NextStep.Should().Be(VerifyEmailNextStep.WorkspaceProvisioning);
        user.IsEmailVerified.Should().BeTrue();
        Workspace.Status.Should().Be(WorkspaceStatus.Provisioning);

        await _provisioningRepo.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<WorkspaceModuleProvisioning>>(rows => rows.Count() == WorkspaceModuleNames.All.Count),
            Arg.Any<CancellationToken>());

        Workspace.DomainEvents.Should().ContainSingle(e => e is WorkspaceVerified)
            .Which.Should().BeOfType<WorkspaceVerified>()
            .Which.workspaceId.Should().Be(Workspace.Id);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _tokenStore.DidNotReceive().InvalidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_WhenStandalonePersonalWorkspaceIsPendingVerification_RoutesToDashboard()
    {
        Email email = Email.Create("solo@example.com").Value!;
        User user = User.Create("Solo", "User", email);
        user.SetPasswordHash("hashed");
        Workspace personalWorkspace = Workspace.CreatePersonal(
            "Solo User",
            WorkspaceSlug.Create("solo-user").Value!,
            email,
            user.Id,
            WellKnownSubscriptionPlans.FreeId);
        WorkspaceMembership membership = WorkspaceMembership.Create(user.Id, personalWorkspace.Id);
        Role adminRole = Role.CreateSystem("Admin", personalWorkspace.Id, ["users:read"]);
        string rawToken = "valid-standalone-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);

        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.Valid, user.Id));
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _membershipRepo.GetByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns([membership]);
        _WorkspaceRepo.GetByIdAsync(personalWorkspace.Id, Arg.Any<CancellationToken>())
            .Returns(personalWorkspace);
        _roleRepo.GetByNameAsync("Admin", personalWorkspace.Id, Arg.Any<CancellationToken>())
            .Returns(adminRole);
        _roleRepo.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), personalWorkspace.Id, Arg.Any<CancellationToken>())
            .Returns([adminRole]);

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.workspaceId.Should().Be(personalWorkspace.Id);
        result.Value.NextStep.Should().Be(VerifyEmailNextStep.Dashboard);
        user.IsEmailVerified.Should().BeTrue();
        personalWorkspace.Status.Should().Be(WorkspaceStatus.Provisioning);

        await _provisioningRepo.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<WorkspaceModuleProvisioning>>(rows => rows.Count() == WorkspaceModuleNames.All.Count),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_WhenWorkspaceContactTokenIsValid_StartsProvisioningAndReturnsSetupToken()
    {
        Email email = Email.Create("admin@acme.com").Value!;
        Workspace Workspace = Workspace.RegisterTeamForContactVerification(
            "Acme",
            WorkspaceSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId,
            WellKnownLegalDocuments.TermsVersion,
            WellKnownLegalDocuments.PrivacyVersion);
        string rawToken = "Workspace-contact-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);

        _tokenStore.ResolveForVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationTokenResolveResult(EmailVerificationTokenState.NotFound, null));
        _WorkspaceTokenStore.ResolveVerificationAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(Result.Success(Workspace.Id));
        _WorkspaceRepo.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns(Workspace);

        Result<VerifyEmailSuccessDto> result = await CreateHandler().Handle(
            new VerifyEmailCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().BeNull();
        result.Value.workspaceId.Should().Be(Workspace.Id);
        result.Value.NextStep.Should().Be(VerifyEmailNextStep.RegisterUser);
        result.Value.WorkspaceSetupToken.Should().NotBeNullOrWhiteSpace();
        Workspace.Status.Should().Be(WorkspaceStatus.Provisioning);

        await _provisioningRepo.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<WorkspaceModuleProvisioning>>(rows => rows.Count() == WorkspaceModuleNames.All.Count),
            Arg.Any<CancellationToken>());
        await _WorkspaceTokenStore.Received(1).CreateFirstUserSetupAsync(
            Workspace.Id,
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
        (User user, Workspace _, WorkspaceMembership _) = MakeUnverifiedUserWithWorkspace();
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
        (User user, Workspace _, WorkspaceMembership _) = MakeUnverifiedUserWithWorkspace();
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
