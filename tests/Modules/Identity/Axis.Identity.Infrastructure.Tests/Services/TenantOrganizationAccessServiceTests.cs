using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Services;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Infrastructure.Tests.Services;

public sealed class TenantOrganizationAccessServiceTests
{
    private readonly IOrganizationRepository _organizationRepository = Substitute.For<IOrganizationRepository>();

    private TenantOrganizationAccessService CreateSut() =>
        new(_organizationRepository);

    private static Organization ActiveOrganization()
    {
        Organization organization = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value!,
            Email.Create("admin@acme.com").Value!,
            WellKnownSubscriptionPlans.FreeId);
        organization.BeginProvisioning();
        organization.CompleteProvisioning();
        return organization;
    }

    [Fact]
    public async Task EvaluateAsync_WhenOrganizationNotFound_ReturnsNotFound()
    {
        Guid organizationId = Guid.NewGuid();
        _organizationRepository.GetByIdAsync(organizationId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        TenantOrganizationAccessResult result = await CreateSut().EvaluateAsync(organizationId);

        result.Status.Should().Be(TenantOrganizationAccessStatus.OrganizationNotFound);
    }

    [Fact]
    public async Task EvaluateAsync_WhenOrganizationIsActive_ReturnsAllowed()
    {
        Organization organization = ActiveOrganization();
        _organizationRepository.GetByIdAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(organization);

        TenantOrganizationAccessResult result = await CreateSut().EvaluateAsync(organization.Id);

        result.Status.Should().Be(TenantOrganizationAccessStatus.Allowed);
    }

    [Fact]
    public async Task EvaluateAsync_WhenOrganizationIsArchived_ReturnsSuspended()
    {
        Organization organization = ActiveOrganization();
        organization.Archive();
        _organizationRepository.GetByIdAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(organization);

        TenantOrganizationAccessResult result = await CreateSut().EvaluateAsync(organization.Id);

        result.Status.Should().Be(TenantOrganizationAccessStatus.OrganizationSuspended);
    }

    [Fact]
    public async Task EvaluateAsync_WhenOrganizationIsProvisioning_ReturnsNotReady()
    {
        Organization organization = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value!,
            Email.Create("admin@acme.com").Value!,
            WellKnownSubscriptionPlans.FreeId);
        organization.BeginProvisioning();
        _organizationRepository.GetByIdAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(organization);

        TenantOrganizationAccessResult result = await CreateSut().EvaluateAsync(organization.Id);

        result.Status.Should().Be(TenantOrganizationAccessStatus.OrganizationNotReady);
    }
}
