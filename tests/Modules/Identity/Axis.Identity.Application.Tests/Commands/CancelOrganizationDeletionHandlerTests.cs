using Axis.Identity.Application.Commands.CancelOrganizationDeletion;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public class CancelOrganizationDeletionHandlerTests
{
    private readonly IOrganizationRepository _orgRepo = Substitute.For<IOrganizationRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public async Task CancelOrganizationDeletion_WhenScheduled_RestoresActive()
    {
        Organization org = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);
        org.ScheduleDeletion(DateTime.UtcNow);
        _orgRepo.GetByIdAsync(OrgId, Arg.Any<CancellationToken>()).Returns(org);

        Result result = await new CancelOrganizationDeletionHandler(_orgRepo, _uow).Handle(
            new CancelOrganizationDeletionCommand(OrgId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        org.Status.Should().Be(OrganizationStatus.Active);
    }
}
