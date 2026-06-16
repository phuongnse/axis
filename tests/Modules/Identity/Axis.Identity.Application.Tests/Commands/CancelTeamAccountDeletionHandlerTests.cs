using Axis.Identity.Application.Commands.CancelTeamAccountDeletion;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public class CancelTeamAccountDeletionHandlerTests
{
    private readonly ITeamAccountRepository _teamAccountRepo = Substitute.For<ITeamAccountRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid TeamAccountId = Guid.NewGuid();

    [Fact]
    public async Task CancelTeamAccountDeletion_WhenScheduled_RestoresActive()
    {
        TeamAccount teamAccount = TeamAccount.Create(
            "Acme",
            TeamAccountSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);
        teamAccount.ScheduleDeletion(DateTime.UtcNow);
        _teamAccountRepo.GetByIdAsync(TeamAccountId, Arg.Any<CancellationToken>()).Returns(teamAccount);

        Result result = await new CancelTeamAccountDeletionHandler(_teamAccountRepo, _uow).Handle(
            new CancelTeamAccountDeletionCommand(TeamAccountId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        teamAccount.Status.Should().Be(TeamAccountStatus.Active);
    }
}
