using Axis.Identity.Application.Queries.GetProvisioningStatus;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class GetProvisioningStatusHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IOrganizationRepository _organizationRepo = Substitute.For<IOrganizationRepository>();
    private readonly ITenantModuleProvisioningRepository _provisioningRepo =
        Substitute.For<ITenantModuleProvisioningRepository>();

    private GetProvisioningStatusHandler CreateHandler() =>
        new(_userRepo, _organizationRepo, _provisioningRepo);

    private static (User User, Organization Organization) MakeVerifiedUserWithOrg()
    {
        Email email = Email.Create("alice@acme.com").Value!;
        Organization organization = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value!,
            email);
        User user = User.Create("Alice", "Smith", email, organization.Id);
        user.VerifyEmail();
        return (user, organization);
    }

    [Fact]
    public async Task Handle_WhenTokenIsNotGuid_ReturnsNull()
    {
        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery("not-a-guid"),
            CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNull()
    {
        Guid userId = Guid.NewGuid();
        _userRepo.GetByIdPlatformWideAsync(userId, Arg.Any<CancellationToken>()).ReturnsNull();

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(userId.ToString()),
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
            email);
        User user = User.Create("Bob", "Smith", email, organization.Id);
        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(user.Id.ToString()),
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

        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _organizationRepo.GetByIdAsync(organization.Id, Arg.Any<CancellationToken>()).Returns(organization);
        _provisioningRepo.GetAllForOrganizationAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(modules);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(user.Id.ToString()),
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

        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _organizationRepo.GetByIdAsync(organization.Id, Arg.Any<CancellationToken>()).Returns(organization);
        _provisioningRepo.GetAllForOrganizationAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(modules);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(user.Id.ToString()),
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

        _userRepo.GetByIdPlatformWideAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _organizationRepo.GetByIdAsync(organization.Id, Arg.Any<CancellationToken>()).Returns(organization);
        _provisioningRepo.GetAllForOrganizationAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(modules);

        ProvisioningStatusDto? dto = await CreateHandler().Handle(
            new GetProvisioningStatusQuery(user.Id.ToString()),
            CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.IsReady.Should().BeFalse();
        dto.OrganizationStatus.Should().Be(nameof(OrganizationStatus.Provisioning));
    }
}
