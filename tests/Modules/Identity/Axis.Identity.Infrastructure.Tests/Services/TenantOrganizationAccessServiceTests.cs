using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Services;
using Axis.Shared.Domain.Primitives;
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
    public async Task EvaluateAsync_WhenOrganizationNotFound_ReturnsForbidden()
    {
        Guid organizationId = Guid.NewGuid();
        _organizationRepository.GetByIdAsync(organizationId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        Result result = await CreateSut().EvaluateAsync(organizationId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Error.Should().Be("Organization is not available.");
    }

    [Fact]
    public async Task EvaluateAsync_WhenOrganizationIsActive_ReturnsSuccess()
    {
        Organization organization = ActiveOrganization();
        _organizationRepository.GetByIdAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(organization);

        Result result = await CreateSut().EvaluateAsync(organization.Id);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_WhenOrganizationIsArchived_ReturnsForbidden()
    {
        Organization organization = ActiveOrganization();
        organization.Archive();
        _organizationRepository.GetByIdAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(organization);

        Result result = await CreateSut().EvaluateAsync(organization.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Error.Should().Be("Organization is not available.");
    }

    [Fact]
    public async Task EvaluateAsync_WhenOrganizationIsProvisioning_ReturnsNotReadyMessage()
    {
        Organization organization = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value!,
            Email.Create("admin@acme.com").Value!,
            WellKnownSubscriptionPlans.FreeId);
        organization.BeginProvisioning();
        _organizationRepository.GetByIdAsync(organization.Id, Arg.Any<CancellationToken>())
            .Returns(organization);

        Result result = await CreateSut().EvaluateAsync(organization.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Error.Should().Be("Workspace is still being set up. Try again shortly.");
    }
}
