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
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IOrganizationMembershipRepository _membershipRepo = Substitute.For<IOrganizationMembershipRepository>();
    private readonly IOrganizationRepository _organizationRepo = Substitute.For<IOrganizationRepository>();
    private readonly ITenantModuleProvisioningRepository _provisioningRepo =
        Substitute.For<ITenantModuleProvisioningRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private RetryTenantProvisioningHandler CreateHandler() =>
        new(_tokenStore, _userRepo, _membershipRepo, _organizationRepo, _provisioningRepo, _uow);

    private static (User User, Organization Organization, OrganizationMembership Membership) MakeVerifiedUserWithOrg()
    {
        Email email = Email.Create("alice@acme.com").Value!;
        Organization organization = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("Alice", "Smith", email);
        user.SetPasswordHash("hashed");
        user.VerifyEmail();
        user.ClearDomainEvents();
        OrganizationMembership membership = OrganizationMembership.Create(user.Id, organization.Id);
        return (user, organization, membership);
    }

    private static Organization FailProvisioning(Organization organization)
    {
        organization.BeginProvisioning();
        organization.MarkProvisioningFailed();
        return organization;
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
        Organization organization = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value!,
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
    public async Task RetryProvisioning_WhenOrganizationMissing_ReturnsNotFound()
    {
        (User user, Organization _, OrganizationMembership membership) = MakeVerifiedUserWithOrg();
        string rawToken = "raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id).Returns(membership);
        _organizationRepo.GetByIdAsync(membership.OrganizationId).Returns((Organization?)null);

        Result result = await CreateHandler().Handle(
            new RetryTenantProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenOrganizationNotFailed_IsIdempotentSuccessWithoutChanges()
    {
        (User user, Organization organization, OrganizationMembership membership) = MakeVerifiedUserWithOrg();
        string rawToken = "raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id).Returns(membership);
        _organizationRepo.GetByIdAsync(membership.OrganizationId).Returns(organization);

        Result result = await CreateHandler().Handle(
            new RetryTenantProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        organization.Status.Should().Be(OrganizationStatus.Active);
        await _provisioningRepo.DidNotReceive()
            .GetAllForOrganizationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryProvisioning_WhenFailed_ResetsFailedModulesAndRequeues()
    {
        (User user, Organization organization, OrganizationMembership membership) = MakeVerifiedUserWithOrg();
        FailProvisioning(organization);

        TenantModuleProvisioning failedModule =
            TenantModuleProvisioning.CreatePending(organization.Id, "data-modeling");
        failedModule.RecordFailure("boom", attemptCount: 3);
        TenantModuleProvisioning succeededModule =
            TenantModuleProvisioning.CreatePending(organization.Id, "workflow-builder");
        succeededModule.RecordSuccess();

        string rawToken = "raw-token";
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        _tokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id).Returns(user);
        _membershipRepo.GetFirstActiveByUserIdAsync(user.Id).Returns(membership);
        _organizationRepo.GetByIdAsync(membership.OrganizationId).Returns(organization);
        _provisioningRepo.GetAllForOrganizationAsync(membership.OrganizationId, Arg.Any<CancellationToken>())
            .Returns([failedModule, succeededModule]);

        Result result = await CreateHandler().Handle(
            new RetryTenantProvisioningCommand(rawToken),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        failedModule.Status.Should().Be(TenantModuleProvisioningStatus.Pending);
        failedModule.AttemptCount.Should().Be(0);
        failedModule.LastError.Should().BeNull();

        succeededModule.Status.Should().Be(TenantModuleProvisioningStatus.Succeeded);

        organization.Status.Should().Be(OrganizationStatus.Provisioning);
        organization.DomainEvents.Should().ContainSingle(e => e is OrganizationVerified)
            .Which.Should().BeOfType<OrganizationVerified>()
            .Which.OrganizationId.Should().Be(organization.Id);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
