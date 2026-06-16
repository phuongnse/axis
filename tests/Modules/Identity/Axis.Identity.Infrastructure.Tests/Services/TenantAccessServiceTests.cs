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

public sealed class TenantAccessServiceTests
{
    private readonly ITenantRepository _TenantRepository = Substitute.For<ITenantRepository>();

    private TenantAccessService CreateSut() =>
        new(_TenantRepository);

    private static Tenant ActiveTenant()
    {
        Tenant Tenant = Tenant.Create(
            "Acme",
            TenantSlug.Create("acme").Value!,
            Email.Create("admin@acme.com").Value!,
            WellKnownSubscriptionPlans.FreeId);
        Tenant.BeginProvisioning();
        Tenant.CompleteProvisioning();
        return Tenant;
    }

    [Fact]
    public async Task EvaluateAsync_WhenTenantNotFound_ReturnsForbidden()
    {
        Guid tenantId = Guid.NewGuid();
        _TenantRepository.GetByIdAsync(tenantId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        Result result = await CreateSut().EvaluateAsync(tenantId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Error.Should().Be("Tenant is not available.");
    }

    [Fact]
    public async Task EvaluateAsync_WhenTenantIsActive_ReturnsSuccess()
    {
        Tenant Tenant = ActiveTenant();
        _TenantRepository.GetByIdAsync(Tenant.Id, Arg.Any<CancellationToken>())
            .Returns(Tenant);

        Result result = await CreateSut().EvaluateAsync(Tenant.Id);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_WhenTenantIsArchived_ReturnsForbidden()
    {
        Tenant Tenant = ActiveTenant();
        Tenant.Archive();
        _TenantRepository.GetByIdAsync(Tenant.Id, Arg.Any<CancellationToken>())
            .Returns(Tenant);

        Result result = await CreateSut().EvaluateAsync(Tenant.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Error.Should().Be("Tenant is not available.");
    }

    [Fact]
    public async Task EvaluateAsync_WhenTenantIsProvisioning_ReturnsNotReadyMessage()
    {
        Tenant Tenant = Tenant.Create(
            "Acme",
            TenantSlug.Create("acme").Value!,
            Email.Create("admin@acme.com").Value!,
            WellKnownSubscriptionPlans.FreeId);
        Tenant.BeginProvisioning();
        _TenantRepository.GetByIdAsync(Tenant.Id, Arg.Any<CancellationToken>())
            .Returns(Tenant);

        Result result = await CreateSut().EvaluateAsync(Tenant.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Error.Should().Be("Workspace is still being set up. Try again shortly.");
    }
}
