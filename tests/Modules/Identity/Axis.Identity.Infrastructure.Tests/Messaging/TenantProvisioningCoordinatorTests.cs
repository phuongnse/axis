using axis.identity.events;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Messaging;
using FluentAssertions;
using NSubstitute;
using Wolverine;

namespace Axis.Identity.Infrastructure.Tests.Messaging;

public sealed class TenantProvisioningCoordinatorTests
{
    private readonly ITeamAccountRepository _teamAccountRepository = Substitute.For<ITeamAccountRepository>();
    private readonly ITenantModuleProvisioningRepository _provisioningRepository =
        Substitute.For<ITenantModuleProvisioningRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();
    private readonly IPlatformProvisioningAlert _alert = Substitute.For<IPlatformProvisioningAlert>();

    [Fact]
    public async Task HandleReportAsync_WhenProvisioningFailsBeforeMaxAttempt_SchedulesRetry()
    {
        TeamAccount teamAccount = CreateProvisioningTeamAccount();
        TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(
            teamAccount.Id,
            TenantModuleNames.DataModeling);
        TenantModuleProvisionReportEvent report = TenantModuleProvisionReportEventFactory.Create(
            teamAccount.Id,
            TenantModuleNames.DataModeling,
            succeeded: false,
            attempt: 1,
            errorMessage: "db timeout");

        _teamAccountRepository.GetByIdAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns(teamAccount);
        _provisioningRepository.GetAsync(
                teamAccount.Id,
                TenantModuleNames.DataModeling,
                Arg.Any<CancellationToken>())
            .Returns(row);

        await TenantProvisioningCoordinator.HandleReportAsync(
            report,
            _teamAccountRepository,
            _provisioningRepository,
            _unitOfWork,
            _messageBus,
            _alert,
            CancellationToken.None);

        row.Status.Should().Be(TenantModuleProvisioningStatus.Pending);
        row.AttemptCount.Should().Be(2);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _messageBus.Received(1).PublishAsync(
            Arg.Is<RetryTenantModuleProvisionMessage>(message =>
                message.TeamAccountId == teamAccount.Id
                && message.Module == TenantModuleNames.DataModeling
                && message.Attempt == 2),
            Arg.Any<DeliveryOptions>());
        await _alert.DidNotReceive().AlertProvisioningFailedAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleReportAsync_WhenAllModulesSucceeded_CompletesTeamAccountProvisioning()
    {
        TeamAccount teamAccount = CreateProvisioningTeamAccount();
        TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(
            teamAccount.Id,
            TenantModuleNames.DataModeling);
        List<TenantModuleProvisioning> allRows =
        [
            row,
            CreateSucceededRow(teamAccount.Id, TenantModuleNames.FormBuilder),
            CreateSucceededRow(teamAccount.Id, TenantModuleNames.WorkflowBuilder),
            CreateSucceededRow(teamAccount.Id, TenantModuleNames.WorkflowEngine),
        ];

        TenantModuleProvisionReportEvent report = TenantModuleProvisionReportEventFactory.Create(
            teamAccount.Id,
            TenantModuleNames.DataModeling,
            succeeded: true,
            attempt: 1);

        _teamAccountRepository.GetByIdAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns(teamAccount);
        _provisioningRepository.GetAsync(
                teamAccount.Id,
                TenantModuleNames.DataModeling,
                Arg.Any<CancellationToken>())
            .Returns(row);
        _provisioningRepository.GetAllForTeamAccountAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns(allRows);

        await TenantProvisioningCoordinator.HandleReportAsync(
            report,
            _teamAccountRepository,
            _provisioningRepository,
            _unitOfWork,
            _messageBus,
            _alert,
            CancellationToken.None);

        row.Status.Should().Be(TenantModuleProvisioningStatus.Succeeded);
        teamAccount.Status.Should().Be(TeamAccountStatus.Active);
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleReportAsync_WhenProvisioningFailsAtMaxAttempt_MarksFailedAndAlerts()
    {
        TeamAccount teamAccount = CreateProvisioningTeamAccount();
        TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(
            teamAccount.Id,
            TenantModuleNames.DataModeling);
        TenantModuleProvisionReportEvent report = TenantModuleProvisionReportEventFactory.Create(
            teamAccount.Id,
            TenantModuleNames.DataModeling,
            succeeded: false,
            attempt: TenantProvisioningCoordinator.MaxAttempts,
            errorMessage: "fatal");

        _teamAccountRepository.GetByIdAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns(teamAccount);
        _provisioningRepository.GetAsync(
                teamAccount.Id,
                TenantModuleNames.DataModeling,
                Arg.Any<CancellationToken>())
            .Returns(row);
        _provisioningRepository.GetAllForTeamAccountAsync(teamAccount.Id, Arg.Any<CancellationToken>())
            .Returns([row]);

        await TenantProvisioningCoordinator.HandleReportAsync(
            report,
            _teamAccountRepository,
            _provisioningRepository,
            _unitOfWork,
            _messageBus,
            _alert,
            CancellationToken.None);

        row.Status.Should().Be(TenantModuleProvisioningStatus.Failed);
        row.AttemptCount.Should().Be(TenantProvisioningCoordinator.MaxAttempts);
        teamAccount.Status.Should().Be(TeamAccountStatus.ProvisioningFailed);
        await _alert.Received(1).AlertProvisioningFailedAsync(
            teamAccount.Id,
            TenantModuleNames.DataModeling,
            TenantProvisioningCoordinator.MaxAttempts,
            "fatal",
            Arg.Any<CancellationToken>());
    }

    private static TeamAccount CreateProvisioningTeamAccount()
    {
        TeamAccount teamAccount = TeamAccount.Create(
            "Acme",
            TeamAccountSlug.Create("acme").Value!,
            Email.Create("admin@acme.com").Value!,
            WellKnownSubscriptionPlans.FreeId);
        teamAccount.BeginProvisioning();
        return teamAccount;
    }

    private static TenantModuleProvisioning CreateSucceededRow(Guid teamAccountId, string module)
    {
        TenantModuleProvisioning row = TenantModuleProvisioning.CreatePending(teamAccountId, module);
        row.RecordSuccess();
        return row;
    }
}
