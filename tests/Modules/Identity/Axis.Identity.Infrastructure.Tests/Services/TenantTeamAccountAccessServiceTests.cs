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

public sealed class TenantTeamAccountAccessServiceTests
{
    private readonly ITeamAccountRepository _teamAccountRepository = Substitute.For<ITeamAccountRepository>();

    private TenantTeamAccountAccessService CreateSut() =>
        new(_teamAccountRepository);

    private static TeamAccount ActiveTeamAccount()
    {
        TeamAccount teamAccount = TeamAccount.Create(
            "Acme",
            TeamAccountSlug.Create("acme").Value!,
            Email.Create("admin@acme.com").Value!,
            WellKnownSubscriptionPlans.FreeId);
        teamAccount.BeginProvisioning();
        teamAccount.CompleteProvisioning();
        return teamAccount;
    }

    [Fact]
    public async Task EvaluateAsync_WhenTeamAccountNotFound_ReturnsForbidden()
    {
        Guid teamAccountId = Guid.NewGuid();
        _teamAccountRepository.GetByIdAsync(teamAccountId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        Result result = await CreateSut().EvaluateAsync(teamAccountId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Error.Should().Be("Team account is not available.");
    }

    [Fact]
    public async Task EvaluateAsync_WhenTeamAccountIsActive_ReturnsSuccess()
    {
        TeamAccount teamAccount = ActiveTeamAccount();
        _teamAccountRepository.GetByIdAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns(teamAccount);

        Result result = await CreateSut().EvaluateAsync(teamAccount.Id);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_WhenTeamAccountIsArchived_ReturnsForbidden()
    {
        TeamAccount teamAccount = ActiveTeamAccount();
        teamAccount.Archive();
        _teamAccountRepository.GetByIdAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns(teamAccount);

        Result result = await CreateSut().EvaluateAsync(teamAccount.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Error.Should().Be("Team account is not available.");
    }

    [Fact]
    public async Task EvaluateAsync_WhenTeamAccountIsProvisioning_ReturnsNotReadyMessage()
    {
        TeamAccount teamAccount = TeamAccount.Create(
            "Acme",
            TeamAccountSlug.Create("acme").Value!,
            Email.Create("admin@acme.com").Value!,
            WellKnownSubscriptionPlans.FreeId);
        teamAccount.BeginProvisioning();
        _teamAccountRepository.GetByIdAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns(teamAccount);

        Result result = await CreateSut().EvaluateAsync(teamAccount.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Error.Should().Be("Workspace is still being set up. Try again shortly.");
    }
}
