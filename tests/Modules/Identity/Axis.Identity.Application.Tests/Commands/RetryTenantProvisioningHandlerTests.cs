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
    private readonly ITenantRegistrationTokenStore _TenantTokenStore =
        Substitute.For<ITenantRegistrationTokenStore>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly ITenantMembershipRepository _membershipRepo = Substitute.For<ITenantMembershipRepository>();
    private readonly ITenantRepository _TenantRepo = Substitute.For<ITenantRepository>();
    private readonly ITenantModuleProvisioningRepository _provisioningRepo =
        Substitute.For<ITenantModuleProvisioningRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public RetryTenantProvisioningHandlerTests()
    {
        _TenantTokenStore.ResolvetenantIdForProvisioningPollAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
    }

    private RetryTenantProvisioningHandler CreateHandler() =>
        new(
            _tokenStore,
            _TenantTokenStore,
            _userRepo,
            _membershipRepo,
            _TenantRepo,
            _provisioningRepo,
            _uow);

    private static (User User, Tenant Tenant, TenantMembership Membership) MakeVerifiedUserWithTenant()
    {
        Email email = Email.Create("alice@acme.com").Value!;
        Tenant Tenant = Tenant.Create(
            "Acme",
            TenantSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("Alice", "Smith", email);
        user.SetPasswordHash("hashed");
        user.VerifyEmail();
        user.ClearDomainEvents();
        TenantMembership membership = TenantMembership.Create(user.Id, Tenant.Id);
        return (user, Tenant, membership);
    }

    private static Tenant FailProvisioning(Tenant Tenant)
    {
        Tenant.BeginProvisioning();
        Tenant.MarkProvisioningFailed();
        return Tenant;
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
        Tenant Tenant = Tenant.Create(
            "Acme",
            TenantSlug.Create("acme").Value!,
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
    public async Task RetryProvisioning_WhenTenantMissing_ReturnsNotFound()
    {
        (User user, Tenant _, TenantMembership membership) = MakeVerifiedUserWithTenant();
        string rawToken = "raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id).Returns(membership);
        _TenantRepo.GetByIdAsync(membership.tenantId).Returns((Tenant?)null);

        Result result = await CreateHandler().Handle(
            new RetryTenantProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenTenantNotFailed_IsIdempotentSuccessWithoutChanges()
    {
        (User user, Tenant Tenant, TenantMembership membership) = MakeVerifiedUserWithTenant();
        string rawToken = "raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id).Returns(membership);
        _TenantRepo.GetByIdAsync(membership.tenantId).Returns(Tenant);

        Result result = await CreateHandler().Handle(
            new RetryTenantProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Tenant.Status.Should().Be(TenantStatus.Active);
        await _provisioningRepo.DidNotReceive()
            .GetAllForTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenFailed_ResetsFailedModulesAndRequeues()
    {
        (User user, Tenant Tenant, TenantMembership membership) = MakeVerifiedUserWithTenant();
        FailProvisioning(Tenant);

        TenantModuleProvisioning failedModule =
            TenantModuleProvisioning.CreatePending(Tenant.Id, "data-modeling");
        failedModule.RecordFailure("boom", attemptCount: 3);
        TenantModuleProvisioning succeededModule =
            TenantModuleProvisioning.CreatePending(Tenant.Id, "workflow-builder");
        succeededModule.RecordSuccess();

        string rawToken = "raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id).Returns(membership);
        _TenantRepo.GetByIdAsync(membership.tenantId).Returns(Tenant);
        _provisioningRepo.GetAllForTenantAsync(membership.tenantId, Arg.Any<CancellationToken>())
            .Returns([failedModule, succeededModule]);

        Result result = await CreateHandler().Handle(
            new RetryTenantProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        failedModule.Status.Should().Be(TenantModuleProvisioningStatus.Pending);
        failedModule.AttemptCount.Should().Be(0);
        failedModule.LastError.Should().BeNull();

        succeededModule.Status.Should().Be(TenantModuleProvisioningStatus.Succeeded);

        Tenant.Status.Should().Be(TenantStatus.Provisioning);
        Tenant.DomainEvents.Should().ContainSingle(e => e is TenantVerified)
            .Which.Should().BeOfType<TenantVerified>()
            .Which.tenantId.Should().Be(Tenant.Id);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenTenantTokenResolves_RequeuesFailedTenant()
    {
        Email email = Email.Create("admin@acme.com").Value!;
        Tenant Tenant = Tenant.Create(
            "Acme",
            TenantSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        FailProvisioning(Tenant);
        TenantModuleProvisioning failedModule =
            TenantModuleProvisioning.CreatePending(Tenant.Id, "data-modeling");
        failedModule.RecordFailure("boom", attemptCount: 3);

        string rawToken = "Tenant-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _TenantTokenStore.ResolvetenantIdForProvisioningPollAsync(
                tokenHash,
                Arg.Any<CancellationToken>())
            .Returns(Tenant.Id);
        _TenantRepo.GetByIdAsync(Tenant.Id, Arg.Any<CancellationToken>())
            .Returns(Tenant);
        _provisioningRepo.GetAllForTenantAsync(Tenant.Id, Arg.Any<CancellationToken>())
            .Returns([failedModule]);

        Result result = await CreateHandler().Handle(
            new RetryTenantProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        failedModule.Status.Should().Be(TenantModuleProvisioningStatus.Pending);
        Tenant.Status.Should().Be(TenantStatus.Provisioning);
        await _tokenStore.DidNotReceive().ResolveUserIdForProvisioningPollAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
