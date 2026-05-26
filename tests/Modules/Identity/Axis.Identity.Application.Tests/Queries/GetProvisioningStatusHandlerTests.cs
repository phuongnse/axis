using Axis.Identity.Application.Queries.GetProvisioningStatus;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.Provisioning;
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
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IOrganizationRepository _organizationRepo = Substitute.For<IOrganizationRepository>();
    private readonly ITenantModuleProvisioningRepository _provisioningRepo =
        Substitute.For<ITenantModuleProvisioningRepository>();

    private GetProvisioningStatusHandler CreateHandler() =>
        new(_verificationTokenStore, _userRepo, _organizationRepo, _provisioningRepo);

    private void StubProvisioningPollForUser(User user)
    {
        string tokenHash = OpaqueTokenGenerator.Hash(PollToken);
        _verificationTokenStore.ResolveUserIdForProvisioningPollAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(user.Id);
    }

    private static (User User, Organization Organization) MakeVerifiedUserWithOrg()
    {
        Email email = Email.Create("alice@acme.com").Value!;
        Organization organization = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("Alice", "Smith", email, organization.Id);
        user.VerifyEmail();
        return (user, organization);
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
        Organization organization = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value!,
            email,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("Bob", "Smith", email, organization.Id);
        StubProvisioningPollForUser(user);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(PollToken),
            CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenAllModulesSucceededAndOrgActive_ReturnsReady()
    {
        (User user, Organization organization) = MakeVerifiedUserWithOrg();
        IReadOnlyList<TenantModuleProvisioning> modules = TenantModuleNames.All
            .Select(m =>
            {
                TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(organization.Id, m);
                row.RecordSuccess();
                return row;
            })
            .ToList();

        StubProvisioningPollForUser(user);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _organizationRepo.GetByIdAsync(organization.Id, Arg.Any<CancellationToken>()).Returns(organization);
        _provisioningRepo.GetAllForOrganizationAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(modules);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(PollToken),
            CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.IsReady.Should().BeTrue();
        dto.OrganizationStatus.Should().Be(nameof(OrganizationStatus.Active));
        dto.Modules.Should().HaveCount(TenantModuleNames.All.Count);
        dto.Modules.Should().OnlyContain(m => m.Status == nameof(TenantModuleProvisioningStatus.Succeeded));
    }

    [Fact]
    public async Task Handle_WhenOnlySubsetOfModulesSucceeded_ReturnsNotReady()
    {
        (User user, Organization organization) = MakeVerifiedUserWithOrg();
        TenantModuleProvisioning succeeded = TenantModuleProvisioning.CreatePending(
            organization.Id,
            TenantModuleNames.DataModeling);
        succeeded.RecordSuccess();
        IReadOnlyList<TenantModuleProvisioning> modules = [succeeded];

        StubProvisioningPollForUser(user);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _organizationRepo.GetByIdAsync(organization.Id, Arg.Any<CancellationToken>()).Returns(organization);
        _provisioningRepo.GetAllForOrganizationAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(modules);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(PollToken),
            CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenOrganizationStillProvisioning_ReturnsNotReady()
    {
        (User user, Organization organization) = MakeVerifiedUserWithOrg();
        organization.BeginProvisioning();
        IReadOnlyList<TenantModuleProvisioning> modules =
        [
            TenantModuleProvisioning.CreatePending(organization.Id, TenantModuleNames.DataModeling),
        ];

        StubProvisioningPollForUser(user);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _organizationRepo.GetByIdAsync(organization.Id, Arg.Any<CancellationToken>()).Returns(organization);
        _provisioningRepo.GetAllForOrganizationAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(modules);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(PollToken),
            CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.IsReady.Should().BeFalse();
        dto.OrganizationStatus.Should().Be(nameof(OrganizationStatus.Provisioning));
    }
}
