using Axis.Identity.Application.Commands.CancelTenantDeletion;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public class CancelTenantDeletionHandlerTests
{
    private readonly ITenantRepository _tenantRepo = Substitute.For<ITenantRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task CancelTenantDeletion_WhenScheduled_RestoresActive()
    {
        Tenant Tenant = Tenant.Create(
            "Acme",
            TenantSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);
        Tenant.ScheduleDeletion(DateTime.UtcNow);
        _tenantRepo.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Tenant);

        Result result = await new CancelTenantDeletionHandler(_tenantRepo, _uow).Handle(
            new CancelTenantDeletionCommand(TenantId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Tenant.Status.Should().Be(TenantStatus.Active);
    }
}
